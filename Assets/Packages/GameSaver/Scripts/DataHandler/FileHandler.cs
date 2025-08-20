using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace ThanhDV.GameSaver.DataHandler
{
    public class FileHandler : IDataHandler
    {
        private const string BACKUP_EXTENSION = ".bak";
        private const string TEMP_EXTENSION = ".tmp";

        private string filePath = "";
        private string fileName = "";
        private bool useEncryption = true;

        public FileHandler(string _filePath, string _fileName, bool _useEncryption)
        {
            filePath = _filePath;
            fileName = _fileName;
            useEncryption = _useEncryption;
        }

        public async Task<T> Read<T>(string profileId, bool allowRestoreFromBackup = true) where T : class
        {
            string fullPath = Path.Combine(filePath, profileId, fileName);
            T loadedData = null;
            if (File.Exists(fullPath))
            {
                try
                {
                    string dataToLoad = "";
                    using (FileStream stream = new(fullPath, FileMode.Open))
                    using (StreamReader reader = new(stream))
                    {
                        dataToLoad = await reader.ReadToEndAsync();
                    }

                    if (useEncryption)
                    {
                        string secret = AES.GetPassphrase();
                        string decryptedData = AES.Decrypt(dataToLoad, secret);
                        dataToLoad = decryptedData;
                    }

                    loadedData = JsonConvert.DeserializeObject<T>(dataToLoad);
                }
                catch (Exception e)
                {
                    if (allowRestoreFromBackup)
                    {
                        Debug.Log($"<color=yellow>[GameSaver] Fail to LOAD data with ID: {profileId}. Trying to roll back!!!</color>\n{e}");
                        if (TryRollback(fullPath))
                        {
                            loadedData = await Read<T>(profileId, false);
                        }
                        else
                        {
                            Debug.Log($"<color=red>[GameSaver] Fail to LOAD data with ID: {profileId}. Can not roll back!!!</color>\n{e}");
                        }
                    }
                    else
                    {
                        Debug.Log($"<color=red>[GameSaver] Fail to LOAD data with ID: {profileId}. Can not roll back or backup is also corrupt!!!</color>\n{e}");
                    }
                }
            }

            return loadedData;
        }

        public async Task<Dictionary<string, T>> ReadAll<T>() where T : class
        {
            Dictionary<string, T> profiles = new();
            if (!Directory.Exists(filePath))
            {
                Debug.Log("<color=yellow>[GameSaver] Save directory does not exist. No profiles to load!!!</color>");
                return profiles;
            }

            IEnumerable<DirectoryInfo> directoryInfos = new DirectoryInfo(filePath).EnumerateDirectories();
            var loadDataTasks = directoryInfos.Select(async dirInfo =>
            {
                string profileId = dirInfo.Name;
                T data = await Read<T>(profileId);
                return (profileId, data);
            }).ToList();

            var results = await Task.WhenAll(loadDataTasks);

            foreach ((string profileId, T data) in results)
            {
                if (data != null)
                {
                    profiles.Add(profileId, data);
                }
                else
                {
                    Debug.Log($"<color=red>[GameSaver] Fail to LOAD data with ID: {profileId}!!!</color>");
                }
            }

            return profiles;
        }

        public async Task Write<T>(T data, string profileId) where T : class
        {
            string fullPath = Path.Combine(filePath, profileId, fileName);
            string backupPath = fullPath + BACKUP_EXTENSION;
            string tempPath = fullPath + TEMP_EXTENSION;

            try
            {
                string directoryName = Path.GetDirectoryName(fullPath);
                Directory.CreateDirectory(directoryName);

                string dataToSave = JsonConvert.SerializeObject(data, Formatting.Indented, JsonUtilities.UnityJsonSettings);
                if (useEncryption)
                {
                    string secret = AES.GetPassphrase();
                    string encryptedData = AES.Encrypt(dataToSave, secret);
                    dataToSave = encryptedData;
                }

                using (FileStream stream = new(tempPath, FileMode.Create))
                using (StreamWriter writer = new(stream))
                {
                    await writer.WriteAsync(dataToSave);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(tempPath, fullPath, backupPath);
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }
            }
            catch (Exception e)
            {
                Debug.Log("<color=red>[GameSaver] Fail to SAVE data!!! </color>\n" + e);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        public void Delete(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return;

            string fullPath = Path.Combine(filePath, profileId, fileName);

            try
            {
                if (File.Exists(fullPath))
                {
                    string profilePath = Path.GetDirectoryName(fullPath);
                    Directory.Delete(profilePath, true);
                }
                else
                {
                    Debug.Log($"<color=red>[GameSaver] Fail to DELETE data. Data was not found at path: {fullPath}!!!</color>");
                }
            }
            catch (Exception e)
            {
                Debug.Log($"<color=red>[GameSaver] Fail to DELETE data with profile {profileId} at path : {fullPath}!!!</color>\n{e}");
            }
        }

        public string GetMostRecentProfile()
        {
            if (!Directory.Exists(filePath))
            {
                Debug.Log("<color=yellow>[GameSaver] Save directory does not exist. No profiles to load!!!</color>");
                return null;
            }

            try
            {
                DirectoryInfo directory = new(filePath);

                FileInfo mostRecentFile = directory.EnumerateDirectories()
                    .SelectMany(dir => dir.EnumerateFiles("*", SearchOption.AllDirectories))
                    .OrderByDescending(file => file.LastAccessTime)
                    .FirstOrDefault();

                return mostRecentFile?.Directory.Name;
            }
            catch (Exception e)
            {
                Debug.Log($"<color=red>[GameSaver] An error occurred while trying to find the most recent profile!!!</color>\n{e}");
                return null;
            }
        }

        public bool TryRollback(string savePath)
        {
            bool success = false;
            string backupPath = savePath + BACKUP_EXTENSION;
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, savePath, true);
                    success = true;
                }
                else
                {
                    Debug.Log($"<color=red>[GameSaver] Rollback FAIL. Backup file not found at path: {backupPath}!!!</color>");
                }
            }
            catch (Exception e)
            {
                Debug.Log($"<color=red>[GameSaver] Failed to roll back data from backup for path: {savePath}!!!</color>\n{e}");
            }

            return success;
        }
    }
}
