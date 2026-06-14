using System;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;
using UnityEngine;

namespace ThanhDV.SaveKeeper.AutoSave
{
    /// <summary>
    /// Optional auto-save service. Drives periodic implicit saves of the active profile and flushes pending saves on application quit (and on mobile focus loss).
    /// </summary>
    /// <remarks>
    /// Construct with an <see cref="ISaveKeeper"/> and an <see cref="AutoSaveSettings"/> (built via <c>new AutoSaveSettings { ... }</c>), then call <see cref="Start"/>. Dispose before (or together with) the underlying SaveKeeper.
    /// <para>Use ONE service per <see cref="ISaveKeeper"/>. Two services on the same keeper double-run the timer and quit-flush (wasteful, not corrupting). Multiple services on different keepers are independent and fine.</para>
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
        /// Countdown tick driven each frame by <see cref="AutoSaveTicker"/> (unscaled time, so it runs while paused).
        /// Does nothing if auto-save is disabled or no profile is active.
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
        /// Resets the countdown when the active profile was just saved — no need to auto-save it again soon.
        /// Fires on the SaveKeeper's captured context (typically the main thread).
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
        /// On mobile, flushes pending saves when focus is lost (app going to background may be OS-killed).
        /// Skipped on desktop where focus changes are transient (alt-tab, click outside).
        /// </summary>
        private void OnFocusChanged(bool focused)
        {
            if (focused) return;
            if (!Application.isMobilePlatform) return;
            if (!_settings.AutoSaveOnQuit) return;

            FlushOnExit();
        }

        /// <summary>
        /// Drains in-flight async saves (bounded by AutoSaveOnQuitTimeout), then forces a final SaveImmediate.
        /// Uses only the public ISaveKeeper API. Blocking here is safe: the save pipeline awaits with
        /// ConfigureAwait(false), so it does not depend on the main thread to complete.
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
