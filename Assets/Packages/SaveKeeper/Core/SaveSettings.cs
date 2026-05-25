using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.CustomAttribute;
using UnityEngine;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Project-wide configuration for SaveKeeper. Edit values via the Inspector on the
    /// SaveSettings ScriptableObject asset, or programmatically before constructing a SaveKeeper.
    /// </summary>
    /// <remarks>
    /// Encryption note: <see cref="UseEncryption"/> only toggles whether the configured
    /// <c>IEncryptionProvider</c> is applied. The provider itself (e.g., <c>AESProvider</c>) is
    /// injected via the SaveKeeper constructor — toggling this flag does not configure the key.
    /// IMPORTANT: do NOT change the master key after shipping; saves encrypted with the previous
    /// key cannot be decrypted by a new key, causing player progress loss.
    /// </remarks>
    public class SaveSettings : ScriptableObject
    {
        [UnderlineHeader("Storage Settings")]
        [SerializeField, Tooltip("If true, calling SaveAsync/LoadAsync with a profile that doesn't exist yet creates it. If false, those calls throw.")]
        private bool _createProfileIfNull = true; public bool CreateProfileIfNull => _createProfileIfNull;

        [SerializeField, Tooltip("If true, save files are encrypted using the IEncryptionProvider injected into SaveKeeper (typically AESProvider). The provider must be configured separately — this flag only gates whether the pipeline calls Encrypt/Decrypt. Recommended ON for production builds to deter casual save editing.")]
        private bool _useEncryption = true; public bool UseEncryption => _useEncryption;

        [SerializeField] private string _fileName = Constant.DEFAULT_FILE_NAME; public string FileName => _fileName;
        [SerializeField, Tooltip("File extension (include leading dot). ")] private string _saveExtension = Constant.DEFAULT_FILE_SAVE_EXTENSION; public string SaveExtension => _saveExtension;
        [SerializeField, Tooltip("File extension (include leading dot). ")] private string _metaExtension = Constant.DEFAULT_FILE_META_EXTENSION; public string MetaExtension => _metaExtension;

        [UnderlineHeader("Auto Save")]
        [SerializeField] private bool _enableAutoSave = true; public bool EnableAutoSave => _enableAutoSave;
        [SerializeField, Tooltip("Second."), ShowIf("enableAutoSave")] private float _autoSaveTime = 300f; public float AutoSaveTime => _autoSaveTime;
        [SerializeField, Tooltip("Automatically drain pending async saves and call SaveImmediate when the application is quitting or paused on mobile. Recommended ON for production builds.")]
        private bool _autoSaveOnQuit = true; public bool AutoSaveOnQuit => _autoSaveOnQuit;

        [SerializeField, ShowIf("autoSaveOnQuit"), Tooltip("Maximum time (milliseconds) to wait for in-flight async saves to drain. After timeout, SaveImmediate is forced anyway as best-effort. Default 3000ms (3s).")]
        private int _autoSaveOnQuitTimeoutMs = 3000; public int AutoSaveOnQuitTimeout => _autoSaveOnQuitTimeoutMs;
    }
}
