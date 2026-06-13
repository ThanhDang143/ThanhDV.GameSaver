using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;

namespace ThanhDV.SaveKeeper.Infrastructure
{
    public class LocalStorageProvider : IStorageProvider
    {
        private readonly string _basePath;

        public LocalStorageProvider(string basePath)
        {
            if (string.IsNullOrEmpty(basePath)) throw new ArgumentNullException(nameof(basePath));

#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("LocalStorageProvider does not support WebGL. Provide a custom IStorageProvider (e.g. backed by PlayerPrefs or IndexedDB via a JSLib bridge).");
#elif (UNITY_PS4 || UNITY_PS5 || UNITY_XBOXONE || UNITY_GAMECORE || UNITY_SWITCH) && !UNITY_EDITOR
            throw new PlatformNotSupportedException("LocalStorageProvider does not support console platforms (PlayStation, Xbox, Switch). Provide a custom IStorageProvider that wraps the platform's official Save Data API.");
#endif

            _basePath = basePath;
        }

        #region Write

        /// <summary>
        /// Asynchronously writes data to a file for a specific profile.
        /// </summary>
        /// <param name="profileId">The profile ID.</param>
        /// <param name="fileName">The name of the file.</param>
        /// <param name="data">The data to write to the file.</param>
        public async Task WriteAsync(string profileId, string fileName, string data)
        {
            ValidateProfileId(profileId);

            string fullPath = GetFullPath(profileId, fileName);
            await WriteToFileAsync(fullPath, data);
        }

        public void WriteImmediate(string profileId, string fileName, string data)
        {
            ValidateProfileId(profileId);

            string fullPath = GetFullPath(profileId, fileName);
            WriteToFileImmediate(fullPath, data);
        }

        /// <summary>
        /// Asynchronously writes data to a file using a secure temporary-to-main replacement flow.
        /// </summary>
        /// <param name="fullPath">The complete file path describing where to write the data.</param>
        /// <param name="data">The string content to write.</param>
        private async Task WriteToFileAsync(string fullPath, string data)
        {
            PrepareDirectory(fullPath);
            string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + Constant.FILE_TEMP_EXTENTION;
            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;

            try
            {
                await File.WriteAllTextAsync(tempPath, data); // Write to a temporary file to prevent corruption of the existing save
                SafeReplace(fullPath, tempPath, backupPath); // Swap the temp file into the main slot (atomic operation handling success/fail without corruption)
            }
            catch (Exception e)
            {
                DebugLog.Error($"Error writing data to file '{DebugLog.SanitizePath(fullPath)}': {DebugLog.SanitizeException(e)}");
                throw;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        /// <summary>
        /// Synchronously writes data to a file using a secure temporary-to-main replacement flow.
        /// </summary>
        /// <param name="fullPath">The complete file path describing where to write the data.</param>
        /// <param name="data">The string content to write.</param>
        private void WriteToFileImmediate(string fullPath, string data)
        {
            PrepareDirectory(fullPath);
            string tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + Constant.FILE_TEMP_EXTENTION;
            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;

            try
            {
                File.WriteAllText(tempPath, data); // Write to a temporary file to prevent corruption of the existing save
                SafeReplace(fullPath, tempPath, backupPath); // Swap the temp file into the main slot (atomic operation handling success/fail without corruption)
            }
            catch (Exception e)
            {
                DebugLog.Error($"Error writing data to file '{DebugLog.SanitizePath(fullPath)}': {DebugLog.SanitizeException(e)}");
                throw;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        /// <summary>
        /// If the main save file exists, replace it with the temp file (aka lastest file) and keep the previous version as a backup.
        /// </summary>
        /// <param name="originalPath">The primary save file.</param>
        /// <param name="tempPath">The temp file (aka lastest file).</param>
        /// <param name="backupPath">The backup file.</param>
        private void SafeReplace(string originalPath, string tempPath, string backupPath)
        {
            if (File.Exists(originalPath))
            {
                File.Replace(tempPath, originalPath, backupPath);
            }
            else
            {
                File.Move(tempPath, originalPath);
            }
        }

        /// <summary>
        /// Restores the save data from its backup file by overwriting the primary save file.
        /// </summary>
        /// <param name="profileId">The profile ID.</param>
        /// <param name="fileName">The target save file name to restore.</param>
        public void RestoreBackup(string profileId, string fileName)
        {
            ValidateProfileId(profileId);

            string fullPath = GetFullPath(profileId, fileName);
            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;

            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Backup file does not exist: {backupPath}");
            }

            try
            {
                File.Copy(backupPath, fullPath, true);
                DebugLog.Success($"Successfully restored file '{fileName}' from backup.");
            }
            catch (Exception e)
            {
                DebugLog.Error($"Error restoring backup for '{fileName}': {e.Message}");
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>
        /// Asynchronously reads the content of the primary save file.
        /// </summary>
        /// <param name="profileId">The profile ID.</param>
        /// <param name="fileName">The target save file name to read.</param>
        /// <returns>A task that resolves to the file content string.</returns>
        public async Task<string> ReadAsync(string profileId, string fileName)
        {
            ValidateProfileId(profileId);

            string fullPath = GetFullPath(profileId, fileName);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Primary save file not found at: {fullPath}");
            }

            string result = await File.ReadAllTextAsync(fullPath);
            return result;
        }

        /// <summary>
        /// Asynchronously reads the content of the backup save file.
        /// </summary>
        /// <param name="profileId">The profile ID.</param>
        /// <param name="fileName">The target save file name associated with the backup.</param>
        /// <returns>A task that resolves to the backup file content string.</returns>
        public async Task<string> ReadBackupAsync(string profileId, string fileName)
        {
            ValidateProfileId(profileId);

            string fullPath = GetFullPath(profileId, fileName);
            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;

            if (!File.Exists(backupPath))
            {
                throw new FileNotFoundException($"Backup save file not found at: {backupPath}");
            }

            string result = await File.ReadAllTextAsync(backupPath);
            return result;
        }

        #endregion

        #region Profile Management

        /// <summary>
        /// Deletes an entire profile and all its associated save files.
        /// </summary>
        /// <param name="profileId">The target profile ID to delete.</param>
        public void DeleteProfile(string profileId)
        {
            ValidateProfileId(profileId);

            string profilePath = Path.Combine(_basePath, profileId);
            if (Directory.Exists(profilePath))
            {
                try
                {
                    Directory.Delete(profilePath, true);
                    DebugLog.Success($"Successfully deleted profile: {profileId}");
                }
                catch (Exception e)
                {
                    DebugLog.Error($"Failed to delete profile: {profileId}. Reason: {e.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Gets a list of all existing profile IDs by identifying the folders within the base path.
        /// </summary>
        /// <returns>An enumerable collection of profile IDs.</returns>
        public IEnumerable<string> GetAllProfileIds()
        {
            if (!Directory.Exists(_basePath)) return Enumerable.Empty<string>();

            IEnumerable<string> result = new DirectoryInfo(_basePath).EnumerateDirectories().Select(dir => dir.Name);
            return result;
        }

        /// <summary>
        /// Identifies the profile whose primary save file was written most recently, falling back to the profile's backup (.bak) when the primary is missing. 
        /// Other files (.meta sidecar, leftover .tmp) are ignored so a self-healed metadata write or a crashed temp file cannot make the wrong profile look most recent.
        /// </summary>
        /// <param name="fileName">The primary save file name to rank profiles by.</param>
        /// <returns>The newest profile ID, or null if no profile has a save or backup file.</returns>
        public string GetMostRecentProfileId(string fileName)
        {
            if (!Directory.Exists(_basePath)) return null;

            try
            {
                string backupName = fileName + Constant.FILE_BACKUP_EXTENTION;
                DirectoryInfo directory = new DirectoryInfo(_basePath);

                string mostRecentFile = directory.EnumerateDirectories()
                                            .Select(dir => new { ProfileId = dir.Name, Mtime = GetSaveMtime(dir, fileName, backupName) })
                                            .Where(x => x.Mtime.HasValue)
                                            .OrderByDescending(f => f.Mtime.Value)
                                            .Select(f => f.ProfileId)
                                            .FirstOrDefault();

                return mostRecentFile;
            }
            catch (Exception e)
            {
                DebugLog.Error($"Error finding the most recent profile: {e.Message}");
                throw;
            }
        }

        /// <summary>
        /// Checks if a designated save file or its backup exists.
        /// </summary>
        /// <param name="profileId">The profile ID.</param>
        /// <param name="fileName">The target save file name to check.</param>
        /// <returns>True if either the primary save or its backup exists, false otherwise.</returns>
        public bool Exists(string profileId, string fileName)
        {
            ValidateProfileId(profileId);

            string fullPath = GetFullPath(profileId, fileName);
            return File.Exists(fullPath) || File.Exists(fullPath + Constant.FILE_BACKUP_EXTENTION);
        }

        /// <summary>
        /// Returns the UTC modification time of the file specified by <paramref name="fileName"/>, or null if the file does not exist.
        /// </summary>
        /// <param name="profileId">The profile ID.</param>
        /// <param name="fileName">The target save file name to inspect.</param>
        /// <returns>UTC <see cref="DateTime"/> of last content modification, or null when missing.</returns>
        public DateTime? GetLastWriteTimeUtc(string profileId, string fileName)
        {
            ValidateProfileId(profileId);

            string fullPath = GetFullPath(profileId, fileName);
            if (!File.Exists(fullPath)) return null;

            return File.GetLastWriteTimeUtc(fullPath);
        }

        #endregion

        #region Helper

        /// <summary>
        /// Combines the base path, profile ID, and file name to get the full file path.
        /// </summary>
        /// <param name="profileId">The profile ID.</param>
        /// <param name="fileName">The name of the file.</param>
        /// <returns>The combined full path.</returns>
        private string GetFullPath(string profileId, string fileName)
        {
            string combined = Path.Combine(_basePath, profileId, fileName);
            string resolved = Path.GetFullPath(combined);

            string baseWithSeparator = Path.GetFullPath(_basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!resolved.StartsWith(baseWithSeparator, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Resolved path '{resolved}' escapes base directory '{baseWithSeparator}'. profileId='{profileId}', fileName='{fileName}'.", nameof(fileName));
            }

            return resolved;
        }

        /// <summary>
        /// Ensures the directory for the specified file path exists by creating it if needed.
        /// </summary>
        /// <param name="fullPath">The full path of the file.</param>
        private void PrepareDirectory(string fullPath)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(dir) || Directory.Exists(dir)) return;

            Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// Validates that the profile ID is not null, empty, or whitespace, is not a reserved name,
        /// and does not contain invalid characters or path separators.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when profileId violates any of these constraints.</exception>
        private void ValidateProfileId(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                throw new ArgumentException("ProfileId cannot be null, empty, or whitespace.", nameof(profileId));
            }

            if (profileId == "." || profileId == "..")
            {
                throw new ArgumentException($"ProfileId '{profileId}' is reserved — cannot use '.' or '..'.", nameof(profileId));
            }

            // Check for characters prohibited by the OS and directory separator slashes (/, \)
            if (profileId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || profileId.Contains(Path.DirectorySeparatorChar) || profileId.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException($"ProfileId '{profileId}' is invalid. Nested directories and special characters are not allowed.", nameof(profileId));
            }
        }

        /// <summary>
        /// Returns the save file's last-write time (UTC) for a profile, falling back to the backup file's time when the primary save is missing. 
        /// Returns null when neither exists (the profile has no loadable save).
        /// </summary>
        private DateTime? GetSaveMtime(DirectoryInfo profileDir, string fileName, string backupName)
        {
            FileInfo save = new(Path.Combine(profileDir.FullName, fileName));
            if (save.Exists) return save.LastWriteTimeUtc;

            FileInfo backup = new(Path.Combine(profileDir.FullName, backupName));
            if (backup.Exists) return backup.LastWriteTimeUtc;

            return null;
        }

        #endregion
    }
}
