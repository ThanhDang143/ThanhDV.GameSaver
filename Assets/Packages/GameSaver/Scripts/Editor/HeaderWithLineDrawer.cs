using UnityEditor;
using UnityEngine;

namespace ThanhDV.GameSaver.Editor
{
    [CustomPropertyDrawer(typeof(HeaderAttribute))]
    public class HeaderWithLineDrawer : DecoratorDrawer
    {
        public override float GetHeight()
        {
            return EditorGUIUtility.singleLineHeight + 12;
        }

        public override void OnGUI(Rect position)
        {
            HeaderAttribute headerAttribute = (HeaderAttribute)attribute;
            string headerText = headerAttribute.header;

            Rect headerRect = position;
            headerRect.y += 8;
            headerRect.height = EditorGUIUtility.singleLineHeight;

            EditorGUI.LabelField(headerRect, headerText, EditorStyles.boldLabel);

            Rect lineRect = headerRect;
            lineRect.y += headerRect.height - 1;
            lineRect.height = 1;

            EditorGUI.DrawRect(lineRect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
    }
}