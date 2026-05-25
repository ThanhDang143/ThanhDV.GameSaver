using UnityEngine;

namespace ThanhDV.SaveKeeper.CustomAttribute
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