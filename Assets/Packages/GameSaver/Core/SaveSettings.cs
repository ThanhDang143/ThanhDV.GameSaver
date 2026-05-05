using ThanhDV.GameSaver.Common;
using ThanhDV.GameSaver.CustomAttribute;
using UnityEngine;

namespace ThanhDV.GameSaver.Core
{
    public class SaveSettings : ScriptableObject
    {
        [UnderlineHeader("Storage Settings")]
        [SerializeField] private bool _createProfileIfNull = true; public bool CreateProfileIfNull => _createProfileIfNull;
        [SerializeField] private bool _useEncryption = true; public bool UseEncryption => _useEncryption;

        [SerializeField] private string _fileName = Constant.DEFAULT_FILE_NAME; public string FileName => _fileName;
        [SerializeField, Tooltip("File extension (include leading dot). ")] private string _saveExtension = Constant.DEFAULT_FILE_SAVE_EXTENSION; public string SaveExtension => _saveExtension;
        [SerializeField, Tooltip("File extension (include leading dot). ")] private string _metaExtension = Constant.DEFAULT_FILE_META_EXTENSION; public string MetaExtension => _metaExtension;

        [UnderlineHeader("Auto Save")]
        [SerializeField] private bool _enableAutoSave = true; public bool EnableAutoSave => _enableAutoSave;
        [SerializeField, Tooltip("Second."), ShowIf("enableAutoSave")] private float _autoSaveTime = 300f; public float AutoSaveTime => _autoSaveTime;
    }
}
