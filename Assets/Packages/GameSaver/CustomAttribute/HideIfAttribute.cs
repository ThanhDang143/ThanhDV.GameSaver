using UnityEngine;

namespace ThanhDV.GameSaver.CustomAttribute
{
    public class HideIfAttribute : PropertyAttribute
    {
        public string ConditionFieldName { get; private set; }

        public HideIfAttribute(string _conditionFieldName)
        {
            ConditionFieldName = _conditionFieldName;
        }
    }
}