using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ThanhDV.GameSaver.Core
{
    public interface IStorageProvider
    {
        /// <summary>
        /// Asynchronously writes save data for the specified profile and file.
        /// </summary>
        /// <param name="profileId">The profile identifier that owns the save file.</param>
        /// <param name="fileName">The target save file name.</param>
        /// <param name="data">The serialized save data to write.</param>
        /// <returns>A task that completes when the write operation finishes.</returns>
        Task WriteAsync(string profileId, string fileName, string data);

        /// <summary>
        /// Immediately writes save data for the specified profile and file.
        /// </summary>
        /// <param name="profileId">The profile identifier that owns the save file.</param>
        /// <param name="fileName">The target save file name.</param>
        /// <param name="data">The serialized save data to write.</param>
        void WriteImmediate(string profileId, string fileName, string data);

        /// <summary>
        /// Restores the save data from its backup file, if available.
        /// </summary>
        /// <param name="profileId">The profile identifier that owns the save file.</param>
        /// <param name="fileName">The target save file name to restore.</param>
        void RestoreBackup(string profileId, string fileName);

        /// <summary>
        /// Asynchronously reads save data from the specified profile and file.
        /// </summary>
        /// <param name="profileId">The profile identifier that owns the save file.</param>
        /// <param name="fileName">The save file name to read.</param>
        /// <returns>A task that resolves to the serialized save data.</returns>
        Task<string> ReadAsync(string profileId, string fileName);

        /// <summary>
        /// Asynchronously reads save data from the backup file.
        /// </summary>
        /// <param name="profileId">The profile identifier that owns the save file.</param>
        /// <param name="fileName">The save file name to read.</param>
        /// <returns>A task that resolves to the serialized save data.</returns>
        Task<string> ReadBackupAsync(string profileId, string fileName);

        /// <summary>
        /// Deletes all save data associated with the specified profile.
        /// </summary>
        /// <param name="profileId">The profile identifier to remove.</param>
        void DeleteProfile(string profileId);

        /// <summary>
        /// Determines whether the specified save file exists for a profile.
        /// </summary>
        /// <param name="profileId">The profile identifier to check.</param>
        /// <param name="fileName">The save file name to look for.</param>
        /// <returns><see langword="true"/> if the save file exists; otherwise, <see langword="false"/>.</returns>
        bool Exists(string profileId, string fileName);

        /// <summary>
        /// Gets the file's last modified time in UTC, or null if it does not exist.
        /// Used to detect stale metadata when a save succeeds but the related .meta write is interrupted.
        /// </summary>
        /// <param name="profileId">The profile identifier that owns the save file.</param>
        /// <param name="fileName">The target save file name to inspect.</param>
        /// <returns>The file's last modified UTC time, or <see langword="null"/> if missing.</returns>
        DateTime? GetLastWriteTimeUtc(string profileId, string fileName);

        /// <summary>
        /// Gets all profile identifiers that currently have stored save data.
        /// </summary>
        /// <returns>An enumerable collection of available profile identifiers.</returns>
        IEnumerable<string> GetAllProfileIds();

        /// <summary>
        /// Gets the most recently used profile identifier.
        /// </summary>
        /// <returns>The most recent profile identifier, or <see langword="null"/> if none exists.</returns>
        string GetMostRecentProfileId();
    }
}
