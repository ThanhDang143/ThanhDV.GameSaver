using System.Collections.Generic;
using UnityEngine;

namespace ThanhDV.GameSaver.Core
{
    [System.Serializable]
    public class SaveData
    {
        public Dictionary<string, ISaveData> Data { get; private set; }

        public SaveData()
        {
            Data = new();
        }

        public T TryGetData<T>() where T : ISaveData, new()
        {
            string key = typeof(T).Name;

            if (Data.TryGetValue(key, out ISaveData saveData))
            {
                return (T)saveData;
            }

            Debug.Log("<color=yellow>[GameSaver] Data does not exist yet. CREATE AND RETURN new data!!!</color>");
            T data = new();
            CreateData(data);
            return data;
        }

        public void CreateData<T>(T newData) where T : ISaveData, new()
        {
            string key = typeof(T).Name;

            if (!Data.TryAdd(key, newData == null ? new() : newData))
            {
                Debug.Log("<color=yellow>[GameSaver] Data already exists!!!</color>");
                return;
            }
        }
    }
}
