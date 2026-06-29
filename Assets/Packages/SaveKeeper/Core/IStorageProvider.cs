using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ThanhDV.SaveKeeper.Core
{
    public interface IStorageProvider
    {
        /// <summary>Asynchronously writes save data for the given profile/file.</summary>
        Task WriteAsync(string profileId, string fileName, string data);

        /// <summary>Synchronously writes save data for the given profile/file.</summary>
        void WriteImmediate(string profileId, string fileName, string data);

        /// <summary>Restores the save from its backup file, if available.</summary>
        void RestoreBackup(string profileId, string fileName);

        /// <summary>Asynchronously reads save data for the given profile/file.</summary>
        Task<string> ReadAsync(string profileId, string fileName);

        /// <summary>Asynchronously reads save data from the backup file.</summary>
        Task<string> ReadBackupAsync(string profileId, string fileName);

        /// <summary>Deletes all data associated with the given profile.</summary>
        void DeleteProfile(string profileId);

        /// <summary>Returns true if the file exists for the given profile.</summary>
        bool Exists(string profileId, string fileName);

        /// <summary>
        /// Returns the file's last-modified UTC time, or null if missing. Used to detect stale metadata
        /// when a save succeeds but the .meta write is interrupted.
        /// </summary>
        DateTime? GetLastWriteTimeUtc(string profileId, string fileName);

        /// <summary>Returns all profile identifiers that currently have stored data.</summary>
        IEnumerable<string> GetAllProfileIds();

        /// <summary>
        /// Returns the profile whose save file (or backup, when primary is missing) was written most recently.
        /// Sidecar files (.meta) and transient files (.tmp) must NOT influence the result.
        /// </summary>
        /// <param name="fileName">The primary save file name to rank profiles by (e.g. "Default.sav").</param>
        string GetMostRecentProfileId(string fileName);
    }
}
