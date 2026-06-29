namespace ThanhDV.SaveKeeper.AutoSave
{
    /// <summary>Configuration for <see cref="SKAutoSave"/> (deliberately separated from core SaveKeeper).</summary>
    public class AutoSaveSettings
    {
        /// <summary>Seconds between periodic auto-saves while a profile is active. Values ≤ 0 disable ticking.</summary>
        public float AutoSaveTime { get; init; } = 300f;

        /// <summary>If true, flushes pending saves on application quit (and on mobile focus loss).</summary>
        public bool AutoSaveOnQuit { get; init; } = true;

        /// <summary>Milliseconds to wait for in-flight async saves to drain on quit before forcing a SaveImmediate.</summary>
        public int AutoSaveOnQuitTimeout { get; init; } = 3000;
    }
}

namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
