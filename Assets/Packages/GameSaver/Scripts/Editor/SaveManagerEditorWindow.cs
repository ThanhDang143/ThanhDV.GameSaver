using ThanhDV.GameSaver.Editor;
using UnityEditor;
using UnityEngine;

namespace ThanhDV.GameSaver
{
    public class SaveManagerEditorWindow : EditorWindow
    {
        [MenuItem("Tools/GameSaver/Save Manager")]
        public static void ShowWindow()
        {
            SaveManagerEditorWindow window = GetWindow<SaveManagerEditorWindow>();
            window.titleContent = new GUIContent("Save Manager");
            window.minSize = new Vector2(400, 300);
            window.Show();
        }

        private void OnGUI()
        {
            string title = "GameSaver - Manager";
            string subtitle = "Created by ThanhDV";
            EditorHelper.CreateHeader(title, subtitle);
        }
    }
}
