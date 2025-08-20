using System.Collections.Generic;
using UnityEngine;

namespace ThanhDV.GameSaver.Core
{
    [System.Serializable]
    public class GameData
    {
        public int IntData { get; set; }
        public float FloatData { get; set; }
        public Dictionary<string, Vector3> DictionaryData { get; set; }

        public GameData()
        {
            IntData = 0;
            FloatData = 0f;
            DictionaryData = new();
        }
    }
}
