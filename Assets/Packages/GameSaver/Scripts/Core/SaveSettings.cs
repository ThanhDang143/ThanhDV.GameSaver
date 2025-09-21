using ThanhDV.GameSaver.CustomAttribute;
using ThanhDV.GameSaver.Helper;
using UnityEngine;

namespace ThanhDV.GameSaver.Core
{
    [CreateAssetMenu(fileName = "SaveSettings", menuName = "GameSaver/SaveSettings", order = 0)]
    public class SaveSettings : ScriptableObject
    {
        [UnderlineHeader("Storage Settings")]
        [SerializeField] private bool createProfileIfNull = true; public bool CreateProfileIfNull => createProfileIfNull;
        [SerializeField] private bool useEncryption = true; public bool UseEncryption => useEncryption;

        [SerializeField, Tooltip("If checked, each data module will be saved as a separate file.")]
        private bool saveAsSeparateFiles = false; public bool SaveAsSeparateFiles => saveAsSeparateFiles;
        [SerializeField, HideIf("saveAsSeparateFiles")] private string fileName = Constant.DEFAULT_FILE_NAME; public string FileName => fileName;
        [SerializeField, Tooltip("File extension (include leading dot).")] private string fileExtension = Constant.DEFAULT_FILE_SAVE_EXTENSION; public string FileExtension => fileExtension;

        [UnderlineHeader("Auto Save")]
        [SerializeField] private bool enableAutoSave = true; public bool EnableAutoSave => enableAutoSave;
        [SerializeField, Tooltip("Second."), ShowIf("enableAutoSave")] private float autoSaveTime = 300f; public float AutoSaveTime => autoSaveTime;
    }
}
