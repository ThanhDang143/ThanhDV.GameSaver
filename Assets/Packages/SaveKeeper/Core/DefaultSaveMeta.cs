using System;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Fallback <see cref="ISaveMeta"/> used when a save call passes null metadata. Stores only ProfileID and
    /// LastTimeSaved — slot UIs cannot display custom info (level name, playtime, etc.).
    /// </summary>
    /// <remarks>
    /// Saving with null metadata OVERWRITES any existing custom metadata on disk. Pass a custom
    /// <see cref="ISaveMeta"/> on every save to preserve rich slot info.
    /// </remarks>
    [SaveDataAlias("DefaultMetadata")]
    public class DefaultSaveMeta : ISaveMeta
    {
        public string ProfileID { get; set; }
        public DateTime LastTimeSaved { get; set; }

        public DefaultSaveMeta() { }

        public DefaultSaveMeta(string profileId, DateTime saveTime)
        {
            ProfileID = profileId;
            LastTimeSaved = saveTime;
        }
    }
}
