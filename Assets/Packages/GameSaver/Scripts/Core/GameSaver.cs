using System.Collections.Generic;
using UnityEngine;
using ThanhDV.GameSaver.Helper;
using System.Threading.Tasks;
using System.Collections;
using System.Reflection;
using System.Linq;

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

        #region Initialization
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

        private async void Register(ISavable savable)
        {
            savableObjs.Add(savable);
            if (saveData == null) return;

            string moduleKey = savable.SaveType.Name;
            if (saveSettings.SaveAsSeparateFiles)
            {
                await LoadModule(moduleKey);
                savable.LoadData(saveData);
            }
            else
            {
                savable.LoadData(saveData);
            }

            if (savable is ISavableLoaded savableLoaded)
            {
                savableLoaded.OnLoadCompleted();
            }
        }

        private async void Unregister(ISavable savable)
        {
            if (saveData == null) return;

            string moduleKey = savable.SaveType.Name;
            if (saveSettings.SaveAsSeparateFiles)
            {
                await LoadModule(moduleKey);
                savable.SaveData(saveData);
            }
            else
            {
                savable.SaveData(saveData);
            }

            savableObjs.Remove(savable);
        }
        #endregion

        #region NewGame
        public void NewGame(string profileId = null)
        {
            saveData = new SaveData();
            string id = string.IsNullOrEmpty(profileId) ? curProfileId : profileId;
            SetProfileID(id);

            Debug.Log($"<color=green>[GameSaver] Created new game with profile: {id}!!!</color>");
        }

        #endregion

        #region SaveGame
        public void SaveGame()
        {
            if (!TryCreateProfileOnFirstRun()) return;

            if (saveSettings.SaveAsSeparateFiles) SaveAsSeparate();
            else SaveAsAIO();

            void SaveAsAIO()
            {
                foreach (ISavable savable in savableObjs)
                {
                    savable.SaveData(saveData);
                }

                dataHandler.Write(saveData, curProfileId);
            }

            void SaveAsSeparate()
            {
                foreach (ISavable savable in savableObjs)
                {
                    string moduleKey = savable.SaveType.Name;
                    savable.SaveData(saveData);

                    SaveModule(moduleKey);
                }
            }
        }

        private void SaveModule(string moduleKey)
        {
            saveData ??= new();
            saveData.TryGetData(moduleKey, out ISaveData data);
            if (data == null)
            {
                Debug.Log($"<color=red>[GameSaver] SaveModule({moduleKey}) called with null data!!!</color>");
                return;
            }

            dataHandler.WriteModule(data, moduleKey, curProfileId);
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
        #endregion

        #region LoadGame

        public async Task<Dictionary<string, SaveData>> LoadAllProfile()
        {
            Dictionary<string, SaveData> gameDatas = await dataHandler.ReadAllProfile();
            return gameDatas;
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
            if (!TryCreateProfileOnFirstRun()) return;

            List<Task> loadTasks = new();
            foreach (ISavable savable in savableObjs)
            {
                loadTasks.Add(LoadAndApplyData(savable));
            }
            await Task.WhenAll(loadTasks);

            foreach (ISavable savable in savableObjs)
            {
                if (savable is not ISavableLoaded savableLoaded) continue;

                savableLoaded.OnLoadCompleted();
            }

            Debug.Log($"<color=green>[GameSaver] Multi-file data loaded for profile: {curProfileId}</color>");

            async Task LoadAndApplyData(ISavable savable)
            {
                string moduleKey = savable.SaveType.Name;
                await LoadModule(moduleKey);
                savable.LoadData(saveData);
            }
        }

        private async Task LoadFromSingleFile()
        {
            saveData = await dataHandler.Read<SaveData>(curProfileId);

            if (!TryCreateProfileOnFirstRun()) return;

            foreach (ISavable savable in savableObjs)
            {
                ApplyData(savable);
            }

            foreach (ISavable savable in savableObjs)
            {
                if (savable is not ISavableLoaded savableLoaded) continue;

                savableLoaded.OnLoadCompleted();
            }

            Debug.Log($"<color=green>[GameSaver] Data loaded (Profile: {curProfileId})</color>");

            void ApplyData(ISavable savable)
            {
                savable.LoadData(saveData);
            }
        }

        private async Task<ISaveData> LoadModule(string moduleKey)
        {
            saveData ??= new();

            if (saveData.TryGetData(moduleKey, out ISaveData cachedData))
            {
                return cachedData;
            }

            ISaveData loadedData = await dataHandler.ReadModule(curProfileId, moduleKey);
            if (loadedData != null)
            {
                saveData.TryAddData(moduleKey, loadedData);
                return loadedData;
            }

            return null;
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
        #endregion

        #region Helper
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

        /// <summary>
        /// Ensure save data exists; creates default profile if allowed.
        /// </summary>
        /// <returns> true if data already existed or was created; otherwise false.</returns> 
        private bool TryCreateProfileOnFirstRun()
        {
            if (saveData != null) return true;

            if (saveSettings.CreateProfileOnFirstRun)
            {
                NewGame(Constant.DEFAULT_PROFILE_ID);
                Debug.Log($"<color=yellow>[GameSaver] No data. Created new data with profile: {curProfileId}!!!</color>");
            }
            else
            {
                Debug.Log("<color=yellow>[GameSaver] No data. Run NewGame() before LoadGame()/SaveGame()!!!</color>");
            }

            return saveSettings.CreateProfileOnFirstRun;
        }
    }
    #endregion
}
