using UnityEngine;

namespace ThanhDV.SaveKeeper.CustomAttribute
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