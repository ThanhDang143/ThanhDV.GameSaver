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

        /// <summary>Asynchronously writes data atomically to the profile's file (temp → swap → backup).</summary>
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

        /// <summary>Atomic async write: writes to a temp file, then swaps it into place (old → .bak).</summary>
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
                SKLogger.Error($"Error writing data to file '{SKLogger.SanitizePath(fullPath)}': {SKLogger.SanitizeException(e)}");
                throw;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        /// <summary>Atomic sync write: writes to a temp file, then swaps it into place (old → .bak).</summary>
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
                SKLogger.Error($"Error writing data to file '{SKLogger.SanitizePath(fullPath)}': {SKLogger.SanitizeException(e)}");
                throw;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        /// <summary>
        /// Replaces the original file with the temp file, demoting the previous version to backup.
        /// Falls back to a plain move if the original doesn't exist yet.
        /// </summary>
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

        /// <summary>Restores from backup by copying .bak over the primary file.</summary>
        /// <exception cref="FileNotFoundException">No backup file exists for this profile/file.</exception>
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
                SKLogger.Success($"Successfully restored file '{fileName}' from backup.");
            }
            catch (Exception e)
            {
                SKLogger.Error($"Error restoring backup for '{fileName}': {e.Message}");
                throw;
            }
        }

        #endregion

        #region Read

        /// <summary>Asynchronously reads the primary save file.</summary>
        /// <exception cref="FileNotFoundException">Primary save file does not exist.</exception>
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

        /// <summary>Asynchronously reads the backup save file (.bak).</summary>
        /// <exception cref="FileNotFoundException">Backup file does not exist.</exception>
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

        /// <summary>Deletes the profile's folder and every save file inside.</summary>
        public void DeleteProfile(string profileId)
        {
            ValidateProfileId(profileId);

            string profilePath = Path.Combine(_basePath, profileId);
            if (Directory.Exists(profilePath))
            {
                try
                {
                    Directory.Delete(profilePath, true);
                    SKLogger.Success($"Successfully deleted profile: {profileId}");
                }
                catch (Exception e)
                {
                    SKLogger.Error($"Failed to delete profile: {profileId}. Reason: {e.Message}");
                    throw;
                }
            }
        }

        /// <summary>Returns every profile ID — one per top-level folder under the base path.</summary>
        public IEnumerable<string> GetAllProfileIds()
        {
            if (!Directory.Exists(_basePath)) return Enumerable.Empty<string>();

            IEnumerable<string> result = new DirectoryInfo(_basePath).EnumerateDirectories().Select(dir => dir.Name);
            return result;
        }

        /// <summary>
        /// Returns the profile whose primary save (or backup, if primary missing) was written most recently.
        /// Ignores .meta sidecars and .tmp leftovers so self-healed writes can't skew the ranking.
        /// </summary>
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
                SKLogger.Error($"Error finding the most recent profile: {e.Message}");
                throw;
            }
        }

        /// <summary>True if the primary save or its backup exists for the profile.</summary>
        public bool Exists(string profileId, string fileName)
        {
            ValidateProfileId(profileId);

            string fullPath = GetFullPath(profileId, fileName);
            return File.Exists(fullPath) || File.Exists(fullPath + Constant.FILE_BACKUP_EXTENTION);
        }

        /// <summary>Returns the file's last-modified UTC time, or null if missing.</summary>
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
        /// Resolves <paramref name="profileId"/>/<paramref name="fileName"/> under the base path.
        /// Throws <see cref="ArgumentException"/> if the resolved path escapes the base directory (path-traversal guard).
        /// </summary>
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

        /// <summary>Creates the parent directory of <paramref name="fullPath"/> if it does not yet exist.</summary>
        private void PrepareDirectory(string fullPath)
        {
            string dir = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(dir) || Directory.Exists(dir)) return;

            Directory.CreateDirectory(dir);
        }

        /// <summary>
        /// Rejects null/empty/whitespace IDs, reserved names ('.'/'..'), and IDs containing path separators
        /// or characters illegal in file names.
        /// </summary>
        /// <exception cref="ArgumentException">profileId violates any of the above.</exception>
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
        /// Returns the save file's UTC last-write time, falling back to the backup when the primary is missing.
        /// Returns null when neither exists.
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
