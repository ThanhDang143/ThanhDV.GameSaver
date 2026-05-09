using System.Collections.Generic;

namespace ThanhDV.GameSaver.Core
{
    /// <summary>
    /// Represents the root container for all saveable data modules in the game.
    /// </summary>
    [System.Serializable]
    public class SaveData
    {
        /// <summary>
        /// Dictionary storing the data modules. Each implementation of ISaveData is considered a module.
        /// Must be public for Newtonsoft.Json serialization to work properly.
        /// </summary>
        public Dictionary<string, ISaveData> DataModules { get; set; } = new();

        /// <summary>
        /// Dictionary storing individual values for direct in-memory read and write operations.
        /// Use this for standalone values that do not warrant a dedicated ISaveData module.
        /// </summary>
        public Dictionary<string, string> SimpleData { get; set; } = new();

        /// <summary>
        /// Creates a shallow copy of this SaveData. 
        /// The two dictionaries are new instances, but ISaveData entries and string values are shared by reference. 
        /// Used internally for thread-safe snapshotting during save.
        /// </summary>
        /// <returns>A shallow clone of SaveData.</returns>
        public SaveData Clone()
        {
            SaveData clone = new()
            {
                DataModules = new(DataModules),
                SimpleData = new(SimpleData)
            };

            return clone;
        }

        /*
        /// <summary>
        /// Retrieves a specific data module of type T. Creates a new instance if it doesn't exist.
        /// </summary>
        /// <typeparam name="T">The type of the data module, which must implement ISaveData.</typeparam>
        /// <param name="customKey">An optional custom key to identify the data module. Defaults to the type name.</param>
        /// <returns>The requested data module of type T.</returns>
        public T GetData<T>(string customKey = null) where T : class, ISaveData, new()
        {
            string key = string.IsNullOrEmpty(customKey) ? typeof(T).Name : customKey;

            if (DataModules.TryGetValue(key, out ISaveData data))
            {
                return data as T;
            }

            // Create a new instance if it doesn't exist
            T newData = new();
            DataModules[key] = newData;
            return newData;
        }

        /// <summary>
        /// Stores or updates a specific data module.
        /// </summary>
        /// <typeparam name="T">The type of the data module, which must implement ISaveData.</typeparam>
        /// <param name="data">The data module instance to store.</param>
        /// <param name="customKey">An optional custom key to identify the data module. Defaults to the type name.</param>
        public void SetData<T>(T data, string customKey = null) where T : class, ISaveData
        {
            if (data == null) return;
            string key = string.IsNullOrEmpty(customKey) ? typeof(T).Name : customKey;

            DataModules[key] = data;
        }

        /// <summary>
        /// Removes a specific data module from the save data.
        /// </summary>
        /// <typeparam name="T">The type of the data module, which must implement ISaveData.</typeparam>
        /// <param name="customKey">An optional custom key identifying the data module to remove. Defaults to the type name.</param>
        public void RemoveData<T>(string customKey = null) where T : class, ISaveData
        {
            string key = string.IsNullOrEmpty(customKey) ? typeof(T).Name : customKey;

            if (DataModules.ContainsKey(key)) DataModules.Remove(key);
        }
        */
    }
}
