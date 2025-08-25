using System.Collections.Generic;
using UnityEngine;

namespace ThanhDV.GameSaver.Core
{
    [System.Serializable]
    public class SaveData
    {
        public Dictionary<string, ISaveData> DataModules { get; set; }

        public SaveData()
        {
            DataModules = new();
        }

        public bool TryGetData<T>(out T data) where T : class, ISaveData, new()
        {
            string key = typeof(T).Name;

            if (DataModules.TryGetValue(key, out ISaveData saveData))
            {
                data = saveData as T;
                return true;
            }

            Debug.Log("<color=yellow>[GameSaver] Data does not exist yet. CREATE AND RETURN new data!!!</color>");
            data = new();
            CreateData(data);
            return false;
        }

        public void CreateData<T>(T newData) where T : class, ISaveData, new()
        {
            string key = typeof(T).Name;

            if (!DataModules.TryAdd(key, newData ?? new()))
            {
                Debug.Log("<color=yellow>[GameSaver] Data already exists!!!</color>");
                return;
            }
        }
    }
}
