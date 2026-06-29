using System.Collections.Generic;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Root save-file payload: savable objects, SimpleData entries, and metadata.
    /// The embedded metadata is the fallback source when the .meta sidecar is missing or corrupted.
    /// </summary>
    [System.Serializable]
    public class SaveData
    {
        /// <summary>Metadata for this save (profile ID, last-saved time, custom fields).</summary>
        public ISaveMeta Meta { get; set; }

        /// <summary>
        /// Object snapshots keyed by <see cref="ISavable.SaveKey"/>. Public for Newtonsoft.Json serialization.
        /// </summary>
        public Dictionary<string, ISaveData> ObjectData { get; set; } = new();

        /// <summary>
        /// Free-form key/value store for standalone values that don't warrant a dedicated <see cref="ISaveData"/> module.
        /// </summary>
        public Dictionary<string, string> SimpleData { get; set; } = new();

        /// <summary>
        /// Returns a shallow clone: dictionaries are new, but entries and string values are shared by reference.
        /// Used for thread-safe snapshotting during save.
        /// </summary>
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
