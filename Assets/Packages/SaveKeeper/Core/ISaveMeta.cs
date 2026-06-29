using System;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Minimal metadata for a save profile: id and last-saved timestamp.
    /// </summary>
    public interface ISaveMeta
    {
        /// <summary>Unique identifier of the save profile.</summary>
        string ProfileID { get; set; }

        /// <summary>UTC timestamp of the most recent successful save.</summary>
        DateTime LastTimeSaved { get; set; }
    }
}
