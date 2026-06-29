using System;

namespace ThanhDV.SaveKeeper.AutoSave
{
    /// <summary>
    /// Optional service driving periodic saves + quit/focus-loss flushes on top of an
    /// <see cref="ThanhDV.SaveKeeper.Core.ISaveKeeper"/>. Construct, then call <see cref="Start"/>.
    /// </summary>
    public interface ISKAutoSave : IDisposable
    {
        /// <summary>True while ticking and subscribed to application lifecycle events.</summary>
        bool IsRunning { get; }

        /// <summary>Starts periodic ticking and subscribes to quit/focus-loss flushes. Idempotent.</summary>
        void Start();

        /// <summary>Stops ticking and unsubscribes from all events. Idempotent.</summary>
        void Stop();
    }
}
