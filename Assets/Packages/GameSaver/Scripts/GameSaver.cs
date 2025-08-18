using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ThanhDV.GameSaver
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
        [SerializeField] private string fileName;

        private GameData gameData;
        private List<ISavable> savableObjs;
        private FileIO fileIO;

        private void Start()
        {
            fileIO = new(Application.persistentDataPath, fileName);
            savableObjs = FindSavableObjs();
            LoadGame();
        }

        public void NewGame()
        {
            gameData = new GameData();
        }

        public void SaveGame()
        {
            for (int i = 0; i < savableObjs.Count; i++)
            {
                savableObjs[i].SaveData(ref gameData);
            }

            fileIO.Save(gameData);
        }

        public void LoadGame()
        {
            if (gameData == null)
            {
                Debug.Log("<color=yellow>[GameSaver] No data was found. Create new default data!!!</color>");
                NewGame();
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

        private void OnApplicationQuit()
        {
            SaveGame();
        }
    }
}
