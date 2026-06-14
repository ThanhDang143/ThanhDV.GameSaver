namespace ThanhDV.SaveKeeper.AutoSave
{
    /// <summary>
    /// Configuration for the optional auto-save service (<see cref="SKAutoSave"/>).
    /// Split out of the core save settings — SaveKeeper itself no longer knows about auto-save.
    /// </summary>
    public class AutoSaveSettings
    {
        /// <summary>
        /// Seconds between periodic auto-saves while a profile is active.
        /// AutoSaveTime <= 0 disables auto-save
        /// </summary>
        public float AutoSaveTime { get; init; } = 300f;

        /// <summary>
        /// When true, the service flushes pending saves on application quit (and on mobile focus loss).
        /// </summary>
        public bool AutoSaveOnQuit { get; init; } = true;

        /// <summary>
        /// Milliseconds to wait for in-flight async saves to drain on quit before forcing a synchronous save.
        /// </summary>
        public int AutoSaveOnQuitTimeout { get; init; } = 3000;
    }
}

namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
