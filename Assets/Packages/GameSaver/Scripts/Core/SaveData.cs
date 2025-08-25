using System.Collections.Generic;
using System.Threading.Tasks;
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

        public async Task<T> GetDataAsync<T>() where T : class, ISaveData, new()
        {
            ISaveData saveData = await GameSaver.Instance.LoadModule<T>();

            if (saveData != null) return saveData as T;

            Debug.Log("<color=yellow>[GameSaver] Data does not exist yet. CREATE AND RETURN new data!!!</color>");
            T data = new();
            CreateData(data);
            return data;
        }

        public T GetData<T>() where T : class, ISaveData, new()
        {
            string key = typeof(T).Name;

            if (DataModules.TryGetValue(key, out ISaveData savedData))
            {
                return savedData as T;
            }

            Debug.Log("<color=yellow>[GameSaver] Data does not exist yet. CREATE AND RETURN new data!!!</color>");
            T data = new();
            CreateData(data);
            return data;
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
