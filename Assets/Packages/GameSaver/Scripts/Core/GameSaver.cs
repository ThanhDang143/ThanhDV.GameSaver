using System.Collections.Generic;
using UnityEngine;
using ThanhDV.GameSaver.DataHandler;
using System.Threading.Tasks;
using System.Collections;

namespace ThanhDV.GameSaver.Core
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
                Initialize();
                DontDestroyOnLoad(_instance);
                return;
            }

            Destroy(gameObject);
        }
        #endregion

        [Header("Config")]
        [SerializeField] private SaveSettings saveSettings;

        private string curProfileId;
        private SaveData gameData;
        private readonly HashSet<ISavable> savableObjs = new();
        private IDataHandler dataHandler;
        private Coroutine autoSaveCoroutine;

        private void Initialize()
        {
            if (saveSettings == null)
            {
                Debug.Log("<color=red>[GameSaver] SaveSettings is not assigned. Please assign a SaveSettings asset!!!</color>");
                return;
            }

            InitializeDataHandler();
            InitializeProfile();
            SaveRegistry.Bind(Register, Unregister);

            _ = LoadGame();

            StartAutoSave();
        }

        private void InitializeDataHandler()
        {
            string fileName = string.IsNullOrEmpty(saveSettings.FileName) ? Constant.DEFAULT_FILE_NAME : saveSettings.FileName;
            dataHandler = new FileHandler(Application.persistentDataPath, fileName, saveSettings.UseEncryption);
        }

        private void InitializeProfile()
        {
            string mostRecentProfile = GetMostRecentProfile();
            curProfileId = string.IsNullOrEmpty(mostRecentProfile) ? Constant.DEFAULT_PROFILE_ID : mostRecentProfile;
        }

        private void Register(ISavable savable)
        {
            savableObjs.Add(savable);
            if (gameData != null) savable.LoadData(gameData);
        }

        private void Unregister(ISavable savable)
        {
            savableObjs.Remove(savable);
            if (gameData != null) savable.SaveData(gameData);
        }

        public void NewGame()
        {
            gameData = new SaveData();
        }

        public void SaveGame()
        {
            if (gameData == null)
            {
                Debug.Log("<color=yellow>[GameSaver] No data. Run NewGame() before SaveGame()!!!</color>");
                return;
            }

            foreach (ISavable savable in savableObjs)
            {
                savable.SaveData(gameData);
            }

            dataHandler.Write(gameData, curProfileId);
        }

        public async Task LoadGame()
        {
            gameData = await dataHandler.Read<SaveData>(curProfileId);

            if (gameData == null)
            {
                Debug.Log("<color=yellow>[GameSaver] No data. Run NewGame() before LoadGame()!!!</color>");
                return;
            }

            foreach (ISavable savable in savableObjs)
            {
                savable.LoadData(gameData);
            }
        }

        public async Task<Dictionary<string, SaveData>> LoadAll()
        {
            Dictionary<string, SaveData> gameDatas = await dataHandler.ReadAll<SaveData>();
            return gameDatas;
        }

        public void SetProfileID(string id)
        {
            curProfileId = id;
        }

        public void DeleteData(string profileId)
        {
            dataHandler.Delete(profileId);
            InitializeProfile();
            _ = LoadGame();
        }

        private string GetMostRecentProfile()
        {
            return dataHandler.GetMostRecentProfile();
        }

        private void StartAutoSave()
        {
            if (autoSaveCoroutine != null) StopCoroutine(autoSaveCoroutine);

            autoSaveCoroutine = StartCoroutine(IEAutoSave());
        }

        private IEnumerator IEAutoSave()
        {
            if (!saveSettings.EnableAutoSave) yield break;

            while (true)
            {
                yield return new WaitForSecondsRealtime(saveSettings.AutoSaveTime);
                SaveGame();
                Debug.Log("<color=green>[GameSaver] AutoSave called!!!</color>");
            }
        }

#if UNITY_ANDROID || UNITY_IOS
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) SaveGame();
        }
#else
        private void OnApplicationQuit()
        {
            SaveGame();
        }
#endif
    }
}
