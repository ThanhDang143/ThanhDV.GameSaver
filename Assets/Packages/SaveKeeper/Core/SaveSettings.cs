using ThanhDV.SaveKeeper.Common;

namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Project-wide configuration for SaveKeeper.
    /// </summary>
    public class SaveSettings
    {
        public bool UseEncryption { get; init; } = true;
        public string FileName { get; init; } = Constant.DEFAULT_FILE_NAME;
        public string SaveExtension { get; init; } = Constant.DEFAULT_FILE_SAVE_EXTENSION;
        public string MetaExtension { get; init; } = Constant.DEFAULT_FILE_META_EXTENSION;

        public bool EnableAutoSave { get; init; } = true;
        public float AutoSaveTime { get; init; } = 300f;

        public bool AutoSaveOnQuit { get; init; } = true;
        public int AutoSaveOnQuitTimeout { get; init; } = 3000;
    }
}
