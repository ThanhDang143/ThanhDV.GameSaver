using ThanhDV.GameSaver.CustomAttribute;
using UnityEngine;

namespace ThanhDV.GameSaver.Core
{
    [CreateAssetMenu(fileName = "SaveSettings", menuName = "GameSaver/SaveSettings", order = 0)]
    public class SaveSettings : ScriptableObject
    {
        [Space]
        [SerializeField] private bool useEncryption = true; public bool UseEncryption => useEncryption;
        [SerializeField, Tooltip("Include extension.")] private string fileName = Constant.DEFAULT_FILE_NAME; public string FileName => fileName;

        [Space]
        [SerializeField] private bool enableAutoSave; public bool EnableAutoSave => enableAutoSave;
        [SerializeField, Tooltip("Second."), ShowIf("enableAutoSave")] private float autoSaveTime = 300f; public float AutoSaveTime => autoSaveTime;
    }
}
