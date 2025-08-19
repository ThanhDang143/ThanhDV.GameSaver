using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ThanhDV.GameSaver.IO;
using System.Threading.Tasks;

namespace ThanhDV.GameSaver.Saver
{
    public class GameSaver : MonoBehaviour
    {
        #region Singleton
        private static GameSaver _instance;
        private static readonly object _lock = new();

        public static GameSaver Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = FindFirstObjectByType(typeof(GameSaver)) as GameSaver;

                        if (_instance == null)
                        {
                            _instance = new GameObject("GameSaver").AddComponent<GameSaver>();

                            Debug.Log($"<color=yellow>{_instance.GetType().Name} instance is null!!! Auto create new instance!!!</color>");
                        }
                        DontDestroyOnLoad(_instance);
                    }
                    return _instance;
                }
            }
        }

        public static bool IsExist => _instance != null;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this as GameSaver;
                DontDestroyOnLoad(_instance);
                return;
            }

            Destroy(gameObject);
        }
        #endregion

        [Header("File Config")]
        [SerializeField, Tooltip("Include extension.")] private string fileName;
        [SerializeField] private bool useEncryption;

        [Header("Debug")]
        [SerializeField] private bool initializeDataIfNull;

        private GameData gameData;
        private List<ISavable> savableObjs;
        private FileIO fileIO;

        private void Start()
        {
            fileIO = new(Application.persistentDataPath, fileName, useEncryption);
            savableObjs = FindSavableObjs();
            _ = LoadGame();
        }

        public void NewGame()
        {
            gameData = new GameData();
        }

        public void SaveGame()
        {
            if (gameData == null)
            {
                Debug.Log("<color=yellow>[GameSaver] No data. Run NewGame() before SaveGame()!!!</color>");
                return;
            }

            for (int i = 0; i < savableObjs.Count; i++)
            {
                savableObjs[i].SaveData(ref gameData);
            }

            fileIO.Save(gameData);
        }

        public async Task LoadGame()
        {
            gameData = await fileIO.Load<GameData>();

            if (gameData == null || initializeDataIfNull)
            {
                NewGame();
            }

            if (gameData == null)
            {
                Debug.Log("<color=yellow>[GameSaver] No data. Run NewGame() before LoadGame()!!!</color>");
                return;
            }

            for (int i = 0; i < savableObjs.Count; i++)
            {
                savableObjs[i].LoadData(gameData);
            }
        }

        private List<ISavable> FindSavableObjs()
        {
            IEnumerable<ISavable> savableObjs = FindObjectsOfType<MonoBehaviour>().OfType<ISavable>();
            return new List<ISavable>(savableObjs);
        }

        private bool HasSaveData()
        {
            return gameData != null;
        }

        private void OnApplicationQuit()
        {
            SaveGame();
        }
    }
}
