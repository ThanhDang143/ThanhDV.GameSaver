using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace ThanhDV.GameSaver
{
    public class FileIO
    {
        private string filePath = "";
        private string fileName = "";

        public FileIO(string _filePath, string _fileName)
        {
            filePath = _filePath;
            fileName = string.IsNullOrEmpty(_fileName) ? "default" : _fileName;
        }

        public async Task<GameData> Load()
        {
            string fullPath = Path.Combine(filePath, fileName);
            GameData loadedData = null;
            if (File.Exists(fullPath))
            {
                try
                {
                    string dataToLoad = "";
                    using FileStream stream = new(fullPath, FileMode.Open);
                    using StreamReader reader = new(stream);
                    dataToLoad = await reader.ReadToEndAsync();

                    loadedData = JsonConvert.DeserializeObject<GameData>(dataToLoad);
                }
                catch (System.Exception)
                {
                    Debug.Log("<color=yellow>[GameSaver] Fail to LOAD data!!!</color>");
                }
            }

            return loadedData;
        }

        public void Save(GameData data)
        {
            string fullPath = Path.Combine(filePath, fileName);
            try
            {
                string directoryName = Path.GetDirectoryName(fullPath);
                Directory.CreateDirectory(directoryName);

                string dataToSave = JsonConvert.SerializeObject(data);

                using FileStream stream = new(fullPath, FileMode.Create);
                using StreamWriter writer = new(stream);
                writer.Write(dataToSave);
            }
            catch (System.Exception)
            {
                Debug.Log("<color=yellow>[GameSaver] Fail to SAVE data!!!</color>");
            }
        }
    }
}
