using System.Threading.Tasks;
using ThanhDV.GameSaver.Core;
using ThanhDV.GameSaver.Helper;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ThanhDV.GameSaver.Editor
{
    public class SaveSettingsEditorWindow : EditorWindow
    {
        private SaveSettings saveSettings;
        private UnityEditor.Editor settingsEditor;
        private bool isLoadingSaveSettings = false;

        [MenuItem("Tools/ThanhDV/Game Saver/Save Settings", false)]
        public static void ShowWindow()
        {
            SaveSettingsEditorWindow window = GetWindow<SaveSettingsEditorWindow>();
            window.titleContent = new GUIContent("Save Settings");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnEnable()
        {
            isLoadingSaveSettings = false;
            LoadSaveSettings();
        }

        private void OnGUI()
        {
            if (isLoadingSaveSettings)
            {
                EditorGUILayout.HelpBox("Loading SaveSettings. Please wait a moment!!!", MessageType.Info);
                return;
            }

            if (saveSettings == null)
            {
                EditorGUILayout.HelpBox("SaveSettings asset not found. Please create one!!!", MessageType.Warning);
                if (GUILayout.Button("Find or Create SaveSettings"))
                {
                    LoadSaveSettings();
                }
                return;
            }

            if (settingsEditor != null)
            {
                string title = "GameSaver - Settings";
                string subtitle = "Created by ThanhDV";
                EditorHelper.CreateHeader(title, subtitle);

                settingsEditor.serializedObject.Update();

                SerializedProperty property = settingsEditor.serializedObject.GetIterator();
                property.NextVisible(true);

                EditorGUIUtility.labelWidth = 200f;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(10f);
                EditorGUILayout.BeginVertical();

                while (property.NextVisible(false))
                {
                    EditorGUILayout.PropertyField(property, true);
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(10f);
                EditorGUILayout.EndHorizontal();

                settingsEditor.serializedObject.ApplyModifiedProperties();
            }
        }

        private async Task<SaveSettings> TryLoadSettings()
        {
            if (saveSettings != null) return saveSettings;

            var handle = Addressables.LoadAssetAsync<SaveSettings>(Constant.SAVE_SETTINGS_SO_NAME);
            saveSettings = await handle.Task;

            handle.Release();

            return saveSettings;
        }

        private async void LoadSaveSettings()
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

            if (saveSettings != null)
            {
                settingsEditor = UnityEditor.Editor.CreateEditor(saveSettings);
            }
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
    }
}
