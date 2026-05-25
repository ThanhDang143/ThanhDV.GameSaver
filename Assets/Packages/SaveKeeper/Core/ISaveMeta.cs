using System;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Defines the minimal metadata required to identify a save profile and record when it was last saved.
    /// </summary>
    public interface ISaveMeta
    {
        /// <summary>
        /// Gets or sets the unique identifier of the save profile.
        /// </summary>
        string ProfileID { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the most recent successful save operation.
        /// </summary>
        DateTime LastTimeSaved { get; set; }
    }
}
