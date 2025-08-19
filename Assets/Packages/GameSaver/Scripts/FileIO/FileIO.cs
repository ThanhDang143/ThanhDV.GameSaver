using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace ThanhDV.GameSaver.IO
{
    public class FileIO
    {
        private string filePath = "";
        private string fileName = "";
        private bool useEncryption = true;

        public FileIO(string _filePath, string _fileName, bool _useEncryption)
        {
            filePath = _filePath;
            fileName = string.IsNullOrEmpty(_fileName) ? "default" : _fileName;
            useEncryption = _useEncryption;
        }

        public async Task<T> Load<T>() where T : class
        {
            string fullPath = Path.Combine(filePath, fileName);
            T loadedData = null;
            if (File.Exists(fullPath))
            {
                try
                {
                    string dataToLoad = "";
                    using FileStream stream = new(fullPath, FileMode.Open);
                    using StreamReader reader = new(stream);
                    dataToLoad = await reader.ReadToEndAsync();

                    if (useEncryption)
                    {
                        string secret = AES.GetPassphrase();
                        string decryptedData = AES.Decrypt(dataToLoad, secret);
                        dataToLoad = decryptedData;
                    }

                    loadedData = JsonConvert.DeserializeObject<T>(dataToLoad);
                }
                catch (System.Exception)
                {
                    Debug.Log("<color=yellow>[GameSaver] Fail to LOAD data!!!</color>");
                }
            }

            return loadedData;
        }

        public void Save<T>(T data)
        {
            string fullPath = Path.Combine(filePath, fileName);
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

                using FileStream stream = new(fullPath, FileMode.Create);
                using StreamWriter writer = new(stream);
                writer.Write(dataToSave);
            }
            catch (System.Exception e)
            {
                Debug.Log("<color=yellow>[GameSaver] Fail to SAVE data!!! </color>" + e);
            }
        }
    }
}
