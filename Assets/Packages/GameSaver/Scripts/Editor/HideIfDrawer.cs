using UnityEngine;
using UnityEditor;
using System.Reflection;
using ThanhDV.GameSaver.CustomAttribute;

namespace ThanhDV.GameSaver.Editor
{
    [CustomPropertyDrawer(typeof(HideIfAttribute))]
    public class HideIfDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (ShouldShow(property))
            {
                return EditorGUI.GetPropertyHeight(property, label, true);
            }
            else
            {
                return 0f;
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (ShouldShow(property))
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }

        private bool ShouldShow(SerializedProperty property)
        {
            HideIfAttribute hideIfAttr = attribute as HideIfAttribute;
            object targetObject = property.serializedObject.targetObject;

            FieldInfo conditionField = targetObject.GetType()
                .GetField(hideIfAttr.ConditionFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (conditionField != null && conditionField.FieldType == typeof(bool))
            {
                bool condition = (bool)conditionField.GetValue(targetObject);
                return !condition;
            }

            return true;
        }
    }
}