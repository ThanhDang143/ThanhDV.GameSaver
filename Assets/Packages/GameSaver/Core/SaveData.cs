using System.Collections.Generic;

namespace ThanhDV.GameSaver.Core
{
    /// <summary>
    /// Root save-file data, including savable objects, SimpleData entries, and metadata.
    /// Metadata in this file is used if an error occurs while saving the separate .meta file.
    /// </summary>
    [System.Serializable]
    public class SaveData
    {
        /// <summary>
        /// Metadata for this save, including profile ID, last-saved time, and custom fields.
        /// </summary>
        public ISaveMeta Meta { get; set; }

        /// <summary>
        /// Dictionary storing the object data. Each implementation of ISaveData is considered a module.
        /// Must be public for Newtonsoft.Json serialization to work properly.
        /// </summary>
        public Dictionary<string, ISaveData> ObjectData { get; set; } = new();

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
                Meta = Meta,
                ObjectData = new(ObjectData),
                SimpleData = new(SimpleData)
            };

            return clone;
        }
    }
}
