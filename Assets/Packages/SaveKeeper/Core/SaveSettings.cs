using ThanhDV.SaveKeeper.Common;

namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>Configuration for a SaveKeeper instance: encryption toggle and file naming.</summary>
    public class SaveSettings
    {
        public bool UseEncryption { get; init; } = true;
        public string FileName { get; init; } = Constant.DEFAULT_FILE_NAME;
        public string SaveExtension { get; init; } = Constant.DEFAULT_FILE_SAVE_EXTENSION;
        public string MetaExtension { get; init; } = Constant.DEFAULT_FILE_META_EXTENSION;
    }
}
