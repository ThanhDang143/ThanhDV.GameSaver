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
            string[] guids = AssetDatabase.FindAssets($"{Constant.SAVE_SETTINGS_NAME} t:ScriptableObject");
            foreach (var g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith("/SO/SaveSettings.asset"))
                {
                    return p;
                }
            }

            return CreateSaveSettingsIfNotExist();
        }

        private static string CreateSaveSettingsIfNotExist()
        {
            const string rootFolder = "Assets/Plugins/GameSaver";
            const string soFolder = rootFolder + "/SO";
            string assetPath = $"{soFolder}/{Constant.SAVE_SETTINGS_NAME}.asset";

            if (!AssetDatabase.IsValidFolder("Assets/Plugins")) AssetDatabase.CreateFolder("Assets", "Plugins");
            if (!AssetDatabase.IsValidFolder(rootFolder)) AssetDatabase.CreateFolder("Assets/Plugins", "GameSaver");
            if (!AssetDatabase.IsValidFolder(soFolder)) AssetDatabase.CreateFolder(rootFolder, "SO");

            ScriptableObject existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (existing != null) return assetPath;

            SaveSettings instance = ScriptableObject.CreateInstance<SaveSettings>();
            AssetDatabase.CreateAsset(instance, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=yellow>[GameSaver] Auto-created SaveSettings at {assetPath}</color>");
            return assetPath;
        }

        private static string GetPackageVersion()
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

        private static void MakeAddressable()
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
                Debug.Log($"<color=red>[GameSaver] Addressable Asset Settings not found. Please initialize Addressables in your project (Window > Asset Management > Addressables > Groups, then click 'Create Addressables Settings')!!!</color>");
                return;
            }

            AddressableAssetGroup group = settings.FindGroup("GameSaver");
            if (group == null)
            {
                group = settings.CreateGroup("GameSaver", false, false, true, null, typeof(AddressableGroupSchemas.BundledAssetGroupSchema));
                if (group == null)
                {
                    Debug.Log($"<color=red>[GameSaver] No Addressable Asset Group found or could be created. Cannot make asset addressable!!!</color>");
                }
            }
            else
            {
                if (group.GetSchema<AddressableGroupSchemas.BundledAssetGroupSchema>() == null)
                {
                    group.AddSchema<AddressableGroupSchemas.BundledAssetGroupSchema>();
                    settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupSchemaAdded, group, true);
                }

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
        }
    }
}
