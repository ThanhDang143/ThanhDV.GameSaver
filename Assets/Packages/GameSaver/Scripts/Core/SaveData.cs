using System.Collections.Generic;

namespace ThanhDV.GameSaver.Core
{
    [System.Serializable]
    public class SaveData
    {
        // this field must be public
        public Dictionary<string, ISaveData> DataModules { get; }

        public SaveData()
        {
            DataModules = new();
        }

        public Dictionary<string, ISaveData> GetDataModules() => DataModules;

        public bool TryGetData<T>(out T data) where T : class, ISaveData, new()
        {
            string key = typeof(T).Name;

            if (DataModules.TryGetValue(key, out ISaveData savedData))
            {
                data = savedData as T;
                return true;
            }

            // Debug.Log("<color=yellow>[GameSaver] Data does not exist yet. CREATE AND RETURN new data!!!</color>");
            data = new();
            CreateData(data);
            return false;
        }

        private void CreateData<T>(T newData) where T : class, ISaveData, new()
        {
            string key = typeof(T).Name;

            if (!DataModules.TryAdd(key, newData ?? new()))
            {
                // Debug.Log("<color=yellow>[GameSaver] Data already exists!!!</color>");
                return;
            }
        }

        public bool TryGetData(string moduleKey, out ISaveData data)
        {
            return DataModules.TryGetValue(moduleKey, out data);
        }

        public bool TryAddData(string moduleKey, ISaveData newData)
        {
            if (!DataModules.TryAdd(moduleKey, newData))
            {
                // Debug.Log("<color=yellow>[GameSaver] Data already exists!!!</color>");
                return false;
            }

            return true;
        }
    }
}
