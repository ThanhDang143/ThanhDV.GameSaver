using System.Collections.Generic;
using UnityEngine;
using ThanhDV.GameSaver.Helper;
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
        private SaveData saveData;
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

            Debug.Log("<color=green>[GameSaver] Initialized successfully.</color>");

        }

        private void InitializeDataHandler()
        {
            string fileName = string.IsNullOrEmpty(saveSettings.FileName) ? Constant.DEFAULT_FILE_NAME : saveSettings.FileName;
            fileName += string.IsNullOrEmpty(saveSettings.FileExtension) ? Constant.DEFAULT_FILE_SAVE_EXTENSION : saveSettings.FileExtension;
            dataHandler = new FileHandler(Application.persistentDataPath, fileName, saveSettings.UseEncryption, saveSettings.SaveAsSeparateFiles);
        }

        private void InitializeProfile()
        {
            string mostRecentProfile = GetMostRecentProfile();
            curProfileId = string.IsNullOrEmpty(mostRecentProfile) ? Constant.DEFAULT_PROFILE_ID : mostRecentProfile;
        }

        private void Register(ISavable savable)
        {
            savableObjs.Add(savable);
            if (saveData != null) savable.LoadData(saveData);
        }

        private void Unregister(ISavable savable)
        {
            savableObjs.Remove(savable);
            if (saveData != null) savable.SaveData(saveData);
        }

        public void NewGame(string profileId = null)
        {
            saveData = new SaveData();
            string id = string.IsNullOrEmpty(profileId) ? curProfileId : profileId;
            SetProfileID(id);

            Debug.Log($"<color=green>[GameSaver] Created new game with profile: {id}!!!</color>");
        }

        public void SaveGame()
        {
            if (saveData == null)
            {
                Debug.Log("<color=yellow>[GameSaver] No data. Run NewGame() before SaveGame()!!!</color>");
                return;
            }

            foreach (ISavable savable in savableObjs)
            {
                savable.SaveData(saveData);
            }

            dataHandler.Write(saveData, curProfileId);
        }

        public async Task<T> LoadModule<T>() where T : class, ISaveData, new()
        {
            string moduleKey = typeof(T).Name;
            saveData ??= new();

            if (saveData.DataModules.TryGetValue(moduleKey, out ISaveData cachedData))
            {
                return cachedData as T;
            }

            T loadedData = await dataHandler.ReadModule<T>(curProfileId, moduleKey);
            if (loadedData != null)
            {
                saveData.DataModules.TryAdd(moduleKey, loadedData);
                return loadedData;
            }

            T newData = new();
            saveData.DataModules.TryAdd(moduleKey, newData);
            return newData;
        }

        public async Task LoadGame()
        {
            if (saveSettings.SaveAsSeparateFiles)
            {
                await LoadFromMultiFile();
            }
            else
            {
                await LoadFromSingleFile();
            }
        }

        private async Task LoadFromMultiFile()
        {
            if (saveData == null)
            {
                if (saveSettings.CreateProfileOnFirstRun)
                {
                    NewGame(Constant.DEFAULT_PROFILE_ID);
                    Debug.Log($"<color=yellow>[GameSaver] No data. Created new data with profile: {curProfileId}!!!</color>");
                }
                else
                {
                    Debug.Log("<color=yellow>[GameSaver] No data. Run NewGame() before LoadGame()!!!</color>");
                    return;
                }
            }

            foreach (ISavable savable in savableObjs)
            {
                var savableType = saveData.GetType();
                ISaveData data = await LoadModule<savableType>();
                savable.LoadData(data);
            }

            Debug.Log($"<color=green>[GameSaver] Data loaded (Profile: {curProfileId})</color>");
        }

        private async Task LoadFromSingleFile()
        {
            saveData = await dataHandler.Read<SaveData>(curProfileId);

            if (saveData == null)
            {
                if (saveSettings.CreateProfileOnFirstRun)
                {
                    NewGame(Constant.DEFAULT_PROFILE_ID);
                    Debug.Log($"<color=yellow>[GameSaver] No data. Created new data with profile: {curProfileId}!!!</color>");
                }
                else
                {
                    Debug.Log("<color=yellow>[GameSaver] No data. Run NewGame() before LoadGame()!!!</color>");
                    return;
                }
            }

            foreach (ISavable savable in savableObjs)
            {
                string savableType = saveData.GetType().FullName;
                if (saveData.DataModules.TryGetValue(savableType, out ISaveData data))
                {
                    savable.LoadData(data);
                }
            }

            Debug.Log($"<color=green>[GameSaver] Data loaded (Profile: {curProfileId})</color>");
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
