using UnityEngine;
using UnityEditor;
using System.Reflection;
using ThanhDV.GameSaver.CustomAttribute;

namespace ThanhDV.GameSaver.Editor
{
    [CustomPropertyDrawer(typeof(ShowIfAttribute))]
    public class ShowIfDrawer : PropertyDrawer
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
            ShowIfAttribute showIfAttr = attribute as ShowIfAttribute;

            object targetObject = property.serializedObject.targetObject;

            FieldInfo conditionField = targetObject.GetType().GetField(showIfAttr.ConditionFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (conditionField != null && conditionField.FieldType == typeof(bool))
            {
                return (bool)conditionField.GetValue(targetObject);
            }

            return true;
        }
    }
}