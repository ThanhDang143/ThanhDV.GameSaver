using System;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;
using UnityEngine;

namespace ThanhDV.SaveKeeper.AutoSave
{
    /// <summary>
    /// Default <see cref="ISKAutoSave"/>. Drives periodic implicit saves of the active profile and flushes
    /// pending saves on application quit (and on mobile focus loss).
    /// </summary>
    /// <remarks>
    /// Construct with an <see cref="ISaveKeeper"/> + <see cref="AutoSaveSettings"/>, then call <see cref="Start"/>.
    /// Dispose before (or together with) the underlying SaveKeeper.
    /// <para>Use ONE service per keeper. Two services on the same keeper double-run timers + quit-flush
    /// (wasteful, not corrupting). Services on different keepers are independent.</para>
    /// </remarks>
    public class SKAutoSave : ISKAutoSave
    {
        private readonly ISaveKeeper _saveKeeper;
        private readonly AutoSaveSettings _settings;

        private float _countdown;

        public bool IsRunning { get; private set; }

        public SKAutoSave(ISaveKeeper saveKeeper, AutoSaveSettings settings)
        {
            _saveKeeper = saveKeeper ?? throw new ArgumentNullException(nameof(saveKeeper));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void Start()
        {
            if (IsRunning) return;
            IsRunning = true;

            _saveKeeper.OnSaveCompleted += OnSaveCompleted;
            Application.quitting += OnApplicationQuitting;
            Application.focusChanged += OnFocusChanged;

            ResetCountdown();
            AutoSaveTicker.Subscribe(this);
        }

        public void Stop()
        {
            if (!IsRunning) return;
            IsRunning = false;

            AutoSaveTicker.Unsubscribe(this);
            _saveKeeper.OnSaveCompleted -= OnSaveCompleted;
            Application.quitting -= OnApplicationQuitting;
            Application.focusChanged -= OnFocusChanged;
        }

        public void Dispose()
        {
            Stop();
        }

        #region Periodic tick

        /// <summary>
        /// Per-frame countdown driven by <see cref="AutoSaveTicker"/> in unscaled time (continues while paused).
        /// No-op when auto-save is disabled or no profile is active.
        /// </summary>
        internal void Tick(float deltaTime)
        {
            if (_settings.AutoSaveTime <= 0) return;
            if (string.IsNullOrEmpty(_saveKeeper.CurrentProfileId)) return;

            _countdown -= deltaTime;
            if (_countdown > 0) return;

            _ = _saveKeeper.SaveAsync();
            ResetCountdown();
        }

        /// <summary>
        /// Resets the countdown after the active profile was saved (manual or auto) — avoids a duplicate auto-save
        /// right after a manual one. Fires on the SaveKeeper's captured context (typically the main thread).
        /// </summary>
        private void OnSaveCompleted(string profileId)
        {
            if (profileId == _saveKeeper.CurrentProfileId) ResetCountdown();
        }

        private void ResetCountdown()
        {
            _countdown = _settings.AutoSaveTime;
        }

        #endregion

        #region Quit / focus-loss flush

        private void OnApplicationQuitting()
        {
            if (!_settings.AutoSaveOnQuit) return;
            FlushOnExit();
        }

        /// <summary>
        /// Mobile only: flush pending saves when focus is lost (app may be OS-killed in background).
        /// Skipped on desktop where focus loss is transient (alt-tab, click outside).
        /// </summary>
        private void OnFocusChanged(bool focused)
        {
            if (focused) return;
            if (!Application.isMobilePlatform) return;
            if (!_settings.AutoSaveOnQuit) return;

            FlushOnExit();
        }

        /// <summary>
        /// Drains in-flight async saves (capped by <see cref="AutoSaveSettings.AutoSaveOnQuitTimeout"/>),
        /// then forces a final SaveImmediate. Blocking is safe because the pipeline uses ConfigureAwait(false).
        /// </summary>
        private void FlushOnExit()
        {
            try
            {
                _saveKeeper.WaitForPendingOperationsAsync().Wait(_settings.AutoSaveOnQuitTimeout);
            }
            catch
            {
                // Per-operation errors are already routed to each handle; this only awaits completion.
            }

            try
            {
                _saveKeeper.SaveImmediate();
            }
            catch (InvalidOperationException)
            {
                SKLogger.Warning("AutoSaveOnQuit has been skipped (no current profile, or async drain timed out).");
            }
            catch (Exception e)
            {
                SKLogger.Error($"AutoSaveOnQuit has failed: {e.Message}");
            }
        }
        #endregion
    }
}
