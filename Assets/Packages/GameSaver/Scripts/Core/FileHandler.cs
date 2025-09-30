using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ThanhDV.GameSaver.Helper;
using UnityEngine;

namespace ThanhDV.GameSaver.Core
{
    public class FileHandler : IDataHandler
    {
        private const string TEMP_EXTENSION = ".tmp";

        private readonly string filePath = "";
        private readonly string fileName = "";
        private readonly bool useEncryption = true;
        private readonly bool saveAsSeparateFiles = false;
        private readonly string fileExtention;

        public FileHandler(string _filePath, string _fileName, bool _useEncryption, bool _saveAsSeparateFiles)
        {
            filePath = _filePath;
            fileName = _fileName;
            useEncryption = _useEncryption;
            saveAsSeparateFiles = _saveAsSeparateFiles;

            fileExtention = fileName.Split('.')[^1];
        }

        #region Read
        public async Task<T> Read<T>(string profileId, bool allowRestoreFromBackup = true) where T : class
        {
            string profilePath = Path.Combine(filePath, profileId);

            if (!Directory.Exists(profilePath))
            {
                Debug.Log("<color=yellow>[GameSaver] Save directory does not exist. No profiles to load!!!</color>");
                return null;
            }

            if (saveAsSeparateFiles)
            {
                SaveData saveData = new();
                string[] files = Directory.GetFiles(profilePath, $"*.{fileExtention}");

                foreach (string file in files)
                {
                    string moduleKey = Path.GetFileNameWithoutExtension(file);
                    ISaveData moduleData = await ReadObject<ISaveData>(file, allowRestoreFromBackup);
                    if (moduleData != null) saveData.TryAddData(moduleKey, moduleData);
                }

                return saveData as T;
            }
            else
            {
                string fullPath = Path.Combine(profilePath, fileName);
                return await ReadObject<T>(fullPath, allowRestoreFromBackup);
            }
        }

        public async Task<ISaveData> ReadModule(string profileId, string moduleKey, bool allowRestoreFromBackup = true)
        {
            if (!saveAsSeparateFiles)
            {
                Debug.Log("<color=yellow>[GameSaver] ReadModule is only supported when 'Save As Separate Files' mode is enabled!!!</color>");
                return null;
            }

            string fileExtention = this.fileName.Split('.')[^1];
            string moduleFileName = $"{moduleKey}.{fileExtention}";
            string fullPath = Path.Combine(filePath, profileId, moduleFileName);

            if (!File.Exists(fullPath))
            {
                Debug.Log($"<color=yellow>[GameSaver] Module '{moduleKey}' file not found for profile '{profileId}'.</color>");
                return null;
            }

            return await ReadObject<ISaveData>(fullPath, allowRestoreFromBackup);
        }

        public async Task<Dictionary<string, SaveData>> ReadAllProfile()
        {
            Dictionary<string, SaveData> profiles = new();
            if (!Directory.Exists(filePath))
            {
                Debug.Log("<color=yellow>[GameSaver] Save directory does not exist. No profiles to load!!!</color>");
                return profiles;
            }

            IEnumerable<DirectoryInfo> directoryInfos = new DirectoryInfo(filePath).EnumerateDirectories();
            var loadDataTasks = directoryInfos.Select(async dirInfo =>
            {
                string profileId = dirInfo.Name;
                SaveData data = await Read<SaveData>(profileId);
                return (profileId, data);
            }).ToList();

            var results = await Task.WhenAll(loadDataTasks);

            foreach ((string profileId, SaveData data) in results)
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

        private async Task<T> ReadObject<T>(string fullPath, bool allowRestoreFromBackup = true) where T : class
        {
            string profilePath = Path.GetDirectoryName(fullPath);
            string profileId = new DirectoryInfo(profilePath).Name;

            T loadedData = null;

            if (File.Exists(fullPath))
            {
                try
                {
                    string dataToLoad = await File.ReadAllTextAsync(fullPath);

                    if (useEncryption)
                    {
                        string secret = AES.GetPassphrase();
                        string decryptedData = AES.Decrypt(dataToLoad, secret);
                        dataToLoad = decryptedData;
                    }

                    loadedData = JsonConvert.DeserializeObject<T>(dataToLoad, JsonUtilities.UnityJsonSettings);
                }
                catch (Exception e)
                {
                    if (allowRestoreFromBackup)
                    {
                        Debug.Log($"<color=yellow>[GameSaver] Fail to LOAD data with ID: {profileId}. Trying to roll back!!!</color>\n{e}");
                        if (TryRollback(fullPath))
                        {
                            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;
                            loadedData = await ReadObject<T>(backupPath, false);
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
        #endregion

        #region Write Async
        public async Task WriteAsync(SaveData data, string profileId)
        {
            if (saveAsSeparateFiles)
            {
                var writeTasks = data.GetDataModules().Select(kvp => WriteModuleAsync(kvp.Value, kvp.Key, profileId));
                await Task.WhenAll(writeTasks);
            }
            else
            {
                string profilePath = Path.Combine(filePath, profileId);
                await WriteObjectAsync(data, fileName, profilePath);
            }
        }

        public async Task WriteModuleAsync(ISaveData data, string moduleKey, string profileId)
        {
            if (!saveAsSeparateFiles)
            {
                Debug.Log("<color=yellow>[GameSaver] WriteModule is only supported when 'Save As Separate Files' mode is enabled!!!</color>");
                return;
            }

            string profilePath = Path.Combine(filePath, profileId);
            string fileExtention = this.fileName.Split('.')[^1];
            string fileName = $"{moduleKey}.{fileExtention}";

            await WriteObjectAsync(data, fileName, profilePath);
        }

        private async Task WriteObjectAsync(object data, string fileName, string profilePath)
        {
            if (data == null)
            {
                Debug.Log("<color=red>[GameSaver] Cannot save null data!!!</color>");
                return;
            }

            Directory.CreateDirectory(profilePath);

            string fullPath = Path.Combine(profilePath, fileName);
            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;
            string tempPath = fullPath + TEMP_EXTENSION;

            try
            {
                string dataToSave = JsonConvert.SerializeObject(data, Formatting.Indented, JsonUtilities.UnityJsonSettings);
                if (useEncryption)
                {
                    string secret = AES.GetPassphrase();
                    string encryptedData = AES.Encrypt(dataToSave, secret);
                    dataToSave = encryptedData;
                }

                await File.WriteAllTextAsync(tempPath, dataToSave);

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
        #endregion

        #region Write Immediate
        public void WriteImmediate(SaveData data, string profileId)
        {
            if (saveAsSeparateFiles)
            {
                Dictionary<string, ISaveData> modules = data.GetDataModules();
                foreach (var module in modules)
                {
                    WriteModuleImmediate(module.Value, module.Key, profileId);
                }
            }
            else
            {
                string profilePath = Path.Combine(filePath, profileId);
                WriteObjectImmediate(data, fileName, profilePath);
            }
        }

        public void WriteModuleImmediate(ISaveData data, string moduleKey, string profileId)
        {
            if (!saveAsSeparateFiles)
            {
                Debug.Log("<color=yellow>[GameSaver] WriteModule is only supported when 'Save As Separate Files' mode is enabled!!!</color>");
                return;
            }

            string profilePath = Path.Combine(filePath, profileId);
            string fileExtention = this.fileName.Split('.')[^1];
            string fileName = $"{moduleKey}.{fileExtention}";

            WriteObjectImmediate(data, fileName, profilePath);
        }

        private void WriteObjectImmediate(object data, string fileName, string profilePath)
        {
            if (data == null)
            {
                Debug.Log("<color=red>[GameSaver] Cannot save null data!!!</color>");
                return;
            }

            Directory.CreateDirectory(profilePath);

            string fullPath = Path.Combine(profilePath, fileName);
            string backupPath = fullPath + Constant.FILE_BACKUP_EXTENTION;
            string tempPath = fullPath + TEMP_EXTENSION;

            try
            {
                string dataToSave = JsonConvert.SerializeObject(data, Formatting.Indented, JsonUtilities.UnityJsonSettings);
                if (useEncryption)
                {
                    string secret = AES.GetPassphrase();
                    string encryptedData = AES.Encrypt(dataToSave, secret);
                    dataToSave = encryptedData;
                }

                File.WriteAllText(tempPath, dataToSave);

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
        #endregion

        #region Helper
        public void Delete(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return;

            string profilePath = Path.Combine(filePath, profileId);

            try
            {
                if (Directory.Exists(profilePath))
                {
                    Directory.Delete(profilePath, true);
                    Debug.Log($"<color=red>[GameSaver] Deleted profile: {profileId}</color>");
                }
                else
                {
                    Debug.Log($"<color=red>[GameSaver] Fail to DELETE data. Data was not found at path: {profilePath}!!!</color>");
                }
            }
            catch (Exception e)
            {
                Debug.Log($"<color=red>[GameSaver] Fail to DELETE data with profile {profileId} at path : {profilePath}!!!</color>\n{e}");
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

                string[] patterns = { $"*.{fileExtention}", $"*{Constant.FILE_BACKUP_EXTENTION}" };

                FileInfo mostRecentFile = directory.EnumerateDirectories()
                    .SelectMany(dir => patterns.SelectMany(p => dir.EnumerateFiles(p, SearchOption.AllDirectories)))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
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
            string backupPath = savePath + Constant.FILE_BACKUP_EXTENTION;
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
        #endregion
    }
}
