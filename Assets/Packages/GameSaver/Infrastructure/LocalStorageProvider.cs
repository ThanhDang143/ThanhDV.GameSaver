using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ThanhDV.GameSaver.Common;
using ThanhDV.GameSaver.Core;

namespace ThanhDV.GameSaver.Infrastructure
{
    public class LocalStorageProvider : IStorageProvider
    {
        private readonly string _basePath;

        public LocalStorageProvider(string basePath)
        {
            if (string.IsNullOrEmpty(basePath)) throw new ArgumentNullException(nameof(basePath));

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
            string fullPath = GetFullPath(profileId, fileName);
            await WriteToFileAsync(fullPath, data);
        }

        public void WriteImmediate(string profileId, string fileName, string data)
        {
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
            string tempPath = fullPath + Constant.FILE_TEMP_EXTENTION;
            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;

            try
            {
                await File.WriteAllTextAsync(tempPath, data); // Write to a temporary file to prevent corruption of the existing save
                SafeReplace(fullPath, tempPath, backupPath); // Swap the temp file into the main slot (atomic operation handling success/fail without corruption)
            }
            catch (Exception e)
            {
                DebugLog.Error($"Error writing data to file '{fullPath}': {e}");
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
            string tempPath = fullPath + Constant.FILE_TEMP_EXTENTION;
            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;

            try
            {
                File.WriteAllText(tempPath, data); // Write to a temporary file to prevent corruption of the existing save
                SafeReplace(fullPath, tempPath, backupPath); // Swap the temp file into the main slot (atomic operation handling success/fail without corruption)
            }
            catch (Exception e)
            {
                DebugLog.Error($"Error writing data to file '{fullPath}': {e}");
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
            if (string.IsNullOrEmpty(profileId)) return;

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
        /// Identifies the profile that was most recently accessed or modified.
        /// </summary>
        /// <returns>The newest profile ID, or null if no profiles exist.</returns>
        public string GetMostRecentProfileId()
        {
            if (!Directory.Exists(_basePath)) return null;

            try
            {
                DirectoryInfo directory = new DirectoryInfo(_basePath);

                FileInfo mostRecentFile = directory.EnumerateDirectories()
                                            .SelectMany(dir => dir.EnumerateFiles("*.*", SearchOption.AllDirectories))
                                            .OrderByDescending(f => f.LastWriteTimeUtc)
                                            .FirstOrDefault();

                return mostRecentFile?.Directory?.Name;
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
            string fullPath = GetFullPath(profileId, fileName);
            return File.Exists(fullPath) || File.Exists(fullPath + Constant.FILE_BACKUP_EXTENTION);
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
            return Path.Combine(_basePath, profileId, fileName);
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

        #endregion
    }
}
