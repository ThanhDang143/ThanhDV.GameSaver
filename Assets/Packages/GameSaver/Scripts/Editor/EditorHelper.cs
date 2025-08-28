using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ThanhDV.GameSaver.Editor
{
    public static class EditorHelper
    {
        public static void CreateHeader(string title, string subtitle)
        {
            // Header
            GUIStyle titleStyle = new(EditorStyles.boldLabel)
            {
                fontSize = 30,
                alignment = TextAnchor.MiddleCenter
            };

            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, titleStyle, GUILayout.Height(50));
            EditorGUILayout.LabelField(subtitle, subtitleStyle);
            DrawHorizontalLine();
        }

        public static void DrawHorizontalLine(int thickness = 1, int padding = 10, Color? color = null)
        {
            Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(padding + thickness));
            r.height = thickness;
            r.y += padding / 2f;
            r.x -= 2;
            r.width += 6;
            EditorGUI.DrawRect(r, color ?? new Color(0.5f, 0.5f, 0.5f, 1));
        }
    }
}
