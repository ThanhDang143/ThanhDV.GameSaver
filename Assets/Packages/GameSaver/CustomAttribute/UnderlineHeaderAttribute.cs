using UnityEngine;

namespace ThanhDV.GameSaver.CustomAttribute
{
    /// <summary>
    /// Draws a header with a line underneath it in the inspector.
    /// </summary>
    public class UnderlineHeaderAttribute : PropertyAttribute
    {
        public readonly string header;

        public UnderlineHeaderAttribute(string header)
        {
            this.header = header;
        }
    }
}
