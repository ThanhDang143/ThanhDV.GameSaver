using ThanhDV.GameSaver.Helper;
using UnityEditor;

namespace ThanhDV.GameSaver.Editor
{
    public class SaveManagerInitializer : EditorWindow
    {
        [MenuItem("Tools/ThanhDV/Game Saver/Initialize", false)]
        public static void Initialize()
        {
            if (PackageImporter.IsInitializedCorrectly())
            {
                SessionState.SetBool(Constant.SESSION_KEY_CHECKED, true);
                return;
            }

            PackageImporter.MakeAddressable();
            SessionState.SetBool(Constant.SESSION_KEY_CHECKED, PackageImporter.IsInitializedCorrectly());
        }
    }
}
