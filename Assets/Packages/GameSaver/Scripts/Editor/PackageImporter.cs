using UnityEditor;
using UnityEngine;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using ThanhDV.GameSaver.Core;
using AddressableGroupSchemas = UnityEditor.AddressableAssets.Settings.GroupSchemas;
using ThanhDV.GameSaver.Helper;

namespace ThanhDV.GameSaver.Editor
{
    [InitializeOnLoad]
    public static class PackageImporter
    {
        private const string DEFAULT_SAVE_SETTINGS_SO_PATH = Constant.DEFAULT_SAVE_SETTINGS_SO_FOLDER + "/" + Constant.SAVE_SETTINGS_NAME + ".asset";

        static PackageImporter()
        {
            string packageVersion = GetPackageVersion();
            string editorPrefsKey = $"ThanhDV.GameSaver.Version.{packageVersion}.Initialized";

            if (!EditorPrefs.HasKey(editorPrefsKey)) EditorPrefs.SetBool(editorPrefsKey, false);
            if (!EditorPrefs.GetBool(editorPrefsKey, false))
            {
                MakeAddressable();
                EditorPrefs.SetBool(editorPrefsKey, true);
            }
        }

        private static string FindSaveSettingsPath()
        {
            if (AssetDatabase.LoadAssetAtPath<SaveSettings>(DEFAULT_SAVE_SETTINGS_SO_PATH) != null)
                return DEFAULT_SAVE_SETTINGS_SO_PATH;

            string[] guids = AssetDatabase.FindAssets($"{Constant.SAVE_SETTINGS_NAME} t:{nameof(SaveSettings)}");
            if (guids == null || guids.Length == 0) guids = AssetDatabase.FindAssets($"t:{nameof(SaveSettings)}");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<SaveSettings>(path) != null)
                    return path;
            }

            return CreateSaveSettingsIfNotExist();
        }

        private static string CreateSaveSettingsIfNotExist()
        {
            EnsureFolderPath(Constant.DEFAULT_SAVE_SETTINGS_SO_FOLDER);

            string assetPath = $"{Constant.DEFAULT_SAVE_SETTINGS_SO_FOLDER}/{Constant.SAVE_SETTINGS_NAME}.asset";

            ScriptableObject existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (existing != null) return assetPath;

            SaveSettings instance = ScriptableObject.CreateInstance<SaveSettings>();
            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=yellow>[GameSaver] Auto-created SaveSettings at {assetPath}</color>");
            return assetPath;
        }

        private static void EnsureFolderPath(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath)) return;

            string[] parts = folderPath.Split('/');
            if (parts.Length == 0) return;

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static string GetPackageVersion()
        {
            string[] guids = AssetDatabase.FindAssets("t:Script PackageImporter");
            if (guids.Length == 0)
            {
                Debug.Log("<color=yellow>[GameSaver] Could not find PackageImporter script to determine version!!!</color>");
                return "Undefined version";
            }
            string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(scriptPath);

            if (packageInfo == null) return "Undefined version";

            return packageInfo.version;
        }

        public static void MakeAddressable()
        {
            string assetName = $"{Constant.SAVE_SETTINGS_NAME}.asset";
            string assetPath = FindSaveSettingsPath();
            string guid = AssetDatabase.AssetPathToGUID(assetPath);

            if (string.IsNullOrEmpty(guid))
            {
                Debug.Log($"<color=red>[GameSaver] Could not find SaveSettings at path {assetPath} to make it addressable. GUID is null or empty!!!</color>");
                return;
            }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                try
                {
                    AddressableAssetSettingsDefaultObject.GetSettings(true);
                    settings = AddressableAssetSettingsDefaultObject.Settings;
                }
                catch
                {
                    Debug.Log($"<color=red>[GameSaver] Addressable Asset Settings not found. Please initialize Addressables in your project (Window > Asset Management > Addressables > Groups, then click 'Create Addressables Settings')!!!</color>");
                    return;
                }
            }

            if (settings == null)
            {
                Debug.Log($"<color=red>[GameSaver] Failed to initialize Addressable Asset Settings!!!</color>");
                return;
            }

            AddressableAssetGroup group = settings.FindGroup("GameSaver");
            if (group == null)
            {
                try
                {
                    group = settings.CreateGroup("GameSaver", false, false, true, null, typeof(AddressableGroupSchemas.BundledAssetGroupSchema));
                    if (group == null)
                    {
                        Debug.Log($"<color=red>[GameSaver] Failed to create Addressable Asset Group 'GameSaver'!!!</color>");
                        return;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.Log($"<color=red>[GameSaver] Exception creating Addressable group: {ex.Message}. Please manually initialize Addressables first!!!</color>");
                    return;
                }
            }

            if (group.GetSchema<AddressableGroupSchemas.BundledAssetGroupSchema>() == null)
            {
                group.AddSchema<AddressableGroupSchemas.BundledAssetGroupSchema>();
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupSchemaAdded, group, true);
            }

            try
            {
                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
                if (entry != null)
                {
                    entry.address = Constant.SAVE_SETTINGS_NAME;
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
                    Debug.Log($"<color=green>[GameSaver] Made ScriptableObject '{assetName}' addressable with address '{entry.address}' in group '{group.Name}'!!!</color>");
                }
                else
                {
                    Debug.Log($"<color=red>[GameSaver] Failed to create or move Addressable entry for {assetName} (GUID: {guid})!!!</color>");
                }
            }
            catch (System.Exception ex)
            {
                Debug.Log($"<color=red>[GameSaver] Exception creating Addressable entry: {ex.Message}!!!</color>");
            }
        }
    }
}