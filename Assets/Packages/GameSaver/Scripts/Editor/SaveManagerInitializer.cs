using ThanhDV.GameSaver.Helper;
using UnityEditor;

namespace ThanhDV.GameSaver.Editor
{
    public class SaveManagerInitializer : EditorWindow
    {
        [MenuItem("Tools/ThanhDV/Game Saver/Initialize", false, 1)]
        public static void Initialize()
        {
            string packageVersion = PackageImporter.GetPackageVersion();
            string editorPrefsKey = $"{Constant.EDITOR_PREF_KEY_PREFIX}{packageVersion}";

            if (!EditorPrefs.HasKey(editorPrefsKey)) EditorPrefs.SetBool(editorPrefsKey, false);

            PackageImporter.MakeAddressable();
            EditorPrefs.SetBool(editorPrefsKey, true);
        }

    }
}
