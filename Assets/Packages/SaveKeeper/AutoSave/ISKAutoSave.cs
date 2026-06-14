using System;

namespace ThanhDV.SaveKeeper.AutoSave
{
    /// <summary>
    /// Optional auto-save service that drives periodic saves and quit/focus-loss flushes on top of an
    /// <see cref="ThanhDV.SaveKeeper.Core.ISaveKeeper"/>. Construct it, then call <see cref="Start"/>.
    /// </summary>
    public interface ISKAutoSave : IDisposable
    {
        /// <summary>
        /// True while the service is actively ticking and hooked to application lifecycle events.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Begins periodic auto-save and subscribes to quit/focus-loss flushes. Idempotent.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops ticking and unsubscribes from all events. Idempotent.
        /// </summary>
        void Stop();
    }
}
