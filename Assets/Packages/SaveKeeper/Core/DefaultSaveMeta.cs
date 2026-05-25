using System;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Default <see cref="ISaveMeta"/> used when saving without custom metadata.
    /// </summary>
    /// <remarks>
    /// Stores only library-managed fields: ProfileID and LastTimeSaved, so slot UI cannot show custom info such as level name or playtime.
    /// <para>
    /// Saving with null metadata replaces any existing custom metadata on disk. Pass a custom <see cref="ISaveMeta"/> on every save to keep rich slot info.
    /// </para>
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
