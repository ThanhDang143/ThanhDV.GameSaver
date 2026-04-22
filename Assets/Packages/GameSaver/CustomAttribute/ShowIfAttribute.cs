using UnityEngine;

namespace ThanhDV.GameSaver.CustomAttribute
{
    public class ShowIfAttribute : PropertyAttribute
    {
        public string ConditionFieldName { get; private set; }

        public ShowIfAttribute(string _conditionFieldName)
        {
            ConditionFieldName = _conditionFieldName;
        }
    }
}