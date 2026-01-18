using ThanhDV.GameSaver.Core;
using ThanhDV.GameSaver.Helper;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System;
using System.Linq;
using UnityEngine.AddressableAssets;
using Newtonsoft.Json.Linq;

namespace ThanhDV.GameSaver.Editor
{
    public class SaveManagerEditorWindow : EditorWindow
    {
        private Dictionary<string, JObject> allProfilesData;
        private SaveSettings saveSettings;
        private bool isLoadingSaveSettings = false;
        private Vector2 scrollPosition;
        private string[] profileIds;
        private int selectedProfileIndex = -1;
        private bool isLoading = false;
        private IDataHandler dataHandler;
        private readonly Dictionary<string, bool> foldoutStates = new();
        private bool hasDataUnsaved = false;

        [MenuItem("Tools/ThanhDV/Game Saver/Save Manager", false)]
        public static void ShowWindow()
        {
            SaveManagerEditorWindow window = GetWindow<SaveManagerEditorWindow>();
            window.titleContent = new GUIContent("Save Manager");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private async void OnEnable()
        {
            isLoadingSaveSettings = false;
            await LoadSaveSettings();
            InitializeDataHandler();
            LoadAllProfilesData();
        }

        private void InitializeDataHandler()
        {
            string fileName = string.IsNullOrEmpty(saveSettings.FileName) ? Constant.DEFAULT_FILE_NAME : saveSettings.FileName;
            fileName += string.IsNullOrEmpty(saveSettings.FileExtension) ? Constant.DEFAULT_FILE_SAVE_EXTENSION : saveSettings.FileExtension;
            dataHandler = new FileHandler(Application.persistentDataPath, fileName, saveSettings.UseEncryption, saveSettings.SaveAsSeparateFiles);
        }

        private void OnGUI()
        {
            string title = "GameSaver - Manager";
            string subtitle = "Created by ThanhDV";
            EditorHelper.CreateHeader(title, subtitle);

            if (saveSettings == null)
            {
                if (isLoadingSaveSettings)
                {
                    EditorGUILayout.HelpBox("Loading SaveSettings. Please wait a moment!!!", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("SaveSettings asset not found. Please create one.", MessageType.Error);
                    if (!isLoading)
                    {
                        if (GUILayout.Button("Find SaveSettings"))
                        {
                            _ = LoadSaveSettings();
                        }
                    }
                }

                return;
            }

            EditorGUI.BeginDisabledGroup(hasDataUnsaved);
            if (GUILayout.Button("Refresh Data"))
            {
                LoadAllProfilesData();
            }
            EditorGUI.EndDisabledGroup();
            if (selectedProfileIndex >= 0 && selectedProfileIndex < profileIds.Length)
            {
                string selectedProfileId = profileIds[selectedProfileIndex];
                EditorGUI.BeginDisabledGroup(!hasDataUnsaved);
                if (GUILayout.Button("Save Changes"))
                {
                    SaveChanges(selectedProfileId);
                }
                EditorGUI.EndDisabledGroup();
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Delete Profile"))
                {
                    DeleteProfile(selectedProfileId);
                }
                GUI.backgroundColor = originalColor;
            }

            EditorHelper.DrawHorizontalLine();

            if (isLoading)
            {
                EditorGUILayout.HelpBox("Loading saved data...", MessageType.Info);
                return;
            }

            if (allProfilesData == null || allProfilesData.Count == 0)
            {
                EditorGUILayout.HelpBox("No save data found.", MessageType.Info);
                return;
            }

            // Dropdown to select profile
            EditorGUI.BeginDisabledGroup(hasDataUnsaved);
            int newProfileIndex = EditorGUILayout.Popup("Select Profile", selectedProfileIndex, profileIds);
            if (newProfileIndex != selectedProfileIndex)
            {
                selectedProfileIndex = newProfileIndex;
                hasDataUnsaved = false;
            }
            EditorGUI.EndDisabledGroup();

            EditorHelper.DrawHorizontalLine();

            // Display data for selected profile
            if (selectedProfileIndex >= 0 && selectedProfileIndex < profileIds.Length)
            {
                string selectedProfileId = profileIds[selectedProfileIndex];



                if (allProfilesData.TryGetValue(selectedProfileId, out JObject data))
                {
                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                    try
                    {
                        if (data["DataModules"] is JObject dataModules)
                        {
                            foreach (var moduleProperty in dataModules.Properties())
                            {
                                if (moduleProperty.Name == "$type") continue;
                                DisplayToken(moduleProperty.Value, moduleProperty.Name);
                            }
                        }
                        else
                        {
                            DisplayToken(data);
                        }
                    }
                    finally { EditorGUILayout.EndScrollView(); }
                }
            }
        }

        private void DisplayToken(JToken token, string key = null)
        {
            if (token == null) return;
            switch (token.Type)
            {
                case JTokenType.Object: DisplayObject(token as JObject, key); break;
                case JTokenType.Array: DisplayArray(token as JArray, key); break;
                default: DisplayValue(token as JValue, key); break;
            }
        }

        private void DisplayObject(JObject obj, string key)
        {
            string foldoutKey = key ?? "Root";
            if (!foldoutStates.ContainsKey(foldoutKey)) foldoutStates[foldoutKey] = true;
            foldoutStates[foldoutKey] = EditorGUILayout.Foldout(foldoutStates[foldoutKey], foldoutKey, true);
            if (foldoutStates[foldoutKey])
            {
                EditorGUI.indentLevel++;
                foreach (var property in obj.Properties())
                {
                    if (property.Name == "$type") continue;

                    DisplayToken(property.Value, property.Name);
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DisplayArray(JArray array, string key)
        {
            string foldoutKey = key ?? "Array";
            if (!foldoutStates.ContainsKey(foldoutKey)) foldoutStates[foldoutKey] = true;
            foldoutStates[foldoutKey] = EditorGUILayout.Foldout(foldoutStates[foldoutKey], $"{key} [{array.Count}]", true);
            if (foldoutStates[foldoutKey])
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < array.Count; i++)
                {
                    DisplayToken(array[i], $"Element {i}");
                }
                EditorGUI.indentLevel--;
            }
        }

        private void DisplayValue(JValue value, string key)
        {
            var content = new GUIContent(key);

            EditorGUI.BeginChangeCheck();

            JToken newValue = null;

            switch (value.Type)
            {
                case JTokenType.Integer:
                    long currentInt = Convert.ToInt64(value.Value);
                    long editedInt = EditorGUILayout.LongField(content, currentInt);
                    if (editedInt != currentInt) newValue = new JValue(editedInt);
                    break;
                case JTokenType.Float:
                    double currentFloat = Convert.ToDouble(value.Value);
                    double editedFloat = EditorGUILayout.DoubleField(content, currentFloat);
                    if (Math.Abs(editedFloat - currentFloat) > double.Epsilon) newValue = new JValue(editedFloat);
                    break;
                case JTokenType.String:
                    string newString = EditorGUILayout.TextField(content, (string)value.Value);
                    newValue = new JValue(newString);
                    break;
                case JTokenType.Boolean:
                    bool newBool = EditorGUILayout.Toggle(content, (bool)value.Value);
                    newValue = new JValue(newBool);
                    break;
                case JTokenType.Date:
                    EditorGUILayout.LabelField(content, new GUIContent(((DateTime)value.Value).ToString("g")));
                    break;
                default:
                    EditorGUILayout.LabelField(content, new GUIContent(value.ToString()));
                    break;
            }

            // Nếu có sự thay đổi, cập nhật lại JValue và đặt cờ
            if (EditorGUI.EndChangeCheck() && newValue != null)
            {
                value.Replace(newValue);
                hasDataUnsaved = true;
            }
        }

        /// <summary>
        /// Loads the SaveSettings ScriptableObject.
        /// </summary>
        private async Task LoadSaveSettings()
        {
            isLoadingSaveSettings = true;
            saveSettings = await TryLoadSettings();
            if (saveSettings == null)
            {
                string[] guids = AssetDatabase.FindAssets($"t:{typeof(SaveSettings).Name}");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    saveSettings = AssetDatabase.LoadAssetAtPath<SaveSettings>(path);
                }
                else
                {
                    string newPath = CreateSaveSettingsAsset();
                    saveSettings = AssetDatabase.LoadAssetAtPath<SaveSettings>(newPath);
                }
            }
            isLoadingSaveSettings = false;
        }

        private async Task<SaveSettings> TryLoadSettings()
        {
            if (saveSettings != null) return saveSettings;

            var handle = Addressables.LoadAssetAsync<SaveSettings>(Constant.SAVE_SETTINGS_SO_NAME);
            saveSettings = await handle.Task;

            handle.Release();

            return saveSettings;
        }

        private string CreateSaveSettingsAsset()
        {
            const string rootFolder = "Assets/Plugins/GameSaver";
            const string soFolder = rootFolder + "/SO";
            string assetPath = $"{soFolder}/{Constant.SAVE_SETTINGS_SO_NAME}.asset";

            if (!AssetDatabase.IsValidFolder("Assets/Plugins")) AssetDatabase.CreateFolder("Assets", "Plugins");
            if (!AssetDatabase.IsValidFolder(rootFolder)) AssetDatabase.CreateFolder("Assets/Plugins", "GameSaver");
            if (!AssetDatabase.IsValidFolder(soFolder)) AssetDatabase.CreateFolder(rootFolder, "SO");

            SaveSettings instance = CreateInstance<SaveSettings>();
            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            DebugLog.Success($"Created SaveSettings at {assetPath}");
            return assetPath;
        }

        /// <summary>
        /// Asynchronously loads all save data from the persistent data path.
        /// </summary>
        private async void LoadAllProfilesData()
        {
            if (saveSettings == null) return;
            isLoading = true;
            hasDataUnsaved = false;
            Repaint();

            allProfilesData = new Dictionary<string, JObject>();
            string savePath = Application.persistentDataPath;

            if (!Directory.Exists(savePath))
            {
                isLoading = false;
                Repaint();
                return;
            }

            var loadedData = await dataHandler.ReadAllProfile();
            allProfilesData = loadedData.ToDictionary(
                kvp => kvp.Key,
                kvp => JObject.FromObject(kvp.Value, JsonSerializer.Create(JsonUtilities.UnityJsonSettings))
            );

            profileIds = allProfilesData.Keys.ToArray();
            foldoutStates.Clear();
            selectedProfileIndex = profileIds.Length > 0 ? 0 : -1;

            isLoading = false;
            Repaint();
        }

        /// <summary>
        /// Reads the data for a single profile, handling decryption and deserialization.
        /// This method mimics the logic from your FileHandler.cs.
        /// </summary>
        private async Task<SaveData> ReadProfileData(string profileId)
        {
            string profilePath = Path.Combine(Application.persistentDataPath, profileId);
            string fullPath = Path.Combine(profilePath, saveSettings.FileName + saveSettings.FileExtension);

            if (!File.Exists(fullPath)) return null;

            try
            {
                string dataToLoad = await File.ReadAllTextAsync(fullPath);

                if (saveSettings.UseEncryption)
                {
                    string secret = AES.GetPassphrase();
                    dataToLoad = AES.Decrypt(dataToLoad, secret);
                }

                // Using a custom converter to handle the interface type
                var settings = new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto,
                    Converters = new List<JsonConverter> { new SaveDataConverter() }
                };

                return JsonConvert.DeserializeObject<SaveData>(dataToLoad, settings);
            }
            catch (Exception e)
            {
                DebugLog.Error($"Failed to read or deserialize data for profile {profileId}!!!\n{e}");
                return null;
            }
        }

        private async void SaveChanges(string profileId)
        {
            if (allProfilesData.TryGetValue(profileId, out JObject dataToSave))
            {
                try
                {
                    SaveData saveData = dataToSave.ToObject<SaveData>(JsonSerializer.Create(JsonUtilities.UnityJsonSettings));
                    await dataHandler.WriteAsync(saveData, profileId);
                    EditorUtility.DisplayDialog("Save Successful", $"The save file for profile '{profileId}' was updated successfully.", "OK");
                    hasDataUnsaved = false;
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("Save Failed", $"Failed to save file for profile '{profileId}'. See Console for details.", "OK");
                    DebugLog.Error($"Failed to save to profile {profileId}!!!\n{e}");
                }
                finally
                {
                    Repaint();
                }
            }
        }

        private void DeleteProfile(string profileId)
        {
            if (EditorUtility.DisplayDialog("Delete Profile?",
                $"This will permanently delete all save data for profile '{profileId}'.\nThis action cannot be undone.",
                "Delete", "Cancel"))
            {
                dataHandler.Delete(profileId);
                LoadAllProfilesData();
            }
        }
    }

    /// <summary>
    /// Custom JsonConverter to handle deserializing the dictionary of ISaveData.
    /// Newtonsoft.Json needs help to know which concrete class to create for an interface.
    /// This is a simplified approach; a more robust solution might involve writing type information into the JSON.
    /// </summary>
    public class SaveDataConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(SaveData);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var saveData = new SaveData();
            var jObject = JObject.Load(reader);

            var dataModules = jObject["DataModules"];
            if (dataModules != null)
            {
                foreach (var prop in (dataModules as Newtonsoft.Json.Linq.JObject).Properties())
                {
                    // This is a simplified example. For a real-world scenario, you would need a way
                    // to map the key (e.g., "PlayerStatsData") back to a concrete C# Type.
                    // This might involve reflection or a pre-defined map.
                    // For now, we'll deserialize as a generic object which is better than nothing.
                    var genericData = prop.Value.ToObject<object>(serializer);
                    saveData.TryAddData(prop.Name, new GenericSaveData(genericData));
                }
            }

            return saveData;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            // We only need to read data in the editor, so writing is not implemented.
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// A wrapper to hold deserialized data when the concrete type isn't known.
    /// </summary>
    public class GenericSaveData : ISaveData
    {
        public object Data;
        public GenericSaveData(object data) { Data = data; }
    }
}