using System.Collections.Generic;
using UnityEngine;
using ThanhDV.GameSaver.Helper;
using System.Threading.Tasks;
using System.Collections;
using UnityEngine.AddressableAssets;
using System.IO;
using System.Linq;
using ThanhDV.GameSaver.CustomAttribute;

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

        public static void WakeUp()
        {
            if (IsExist) return;

            var saver = Instance;
        }

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

        [UnderlineHeader("Config")]
        [SerializeField] private SaveSettings saveSettings;

        private string curProfileId;
        private SaveData saveData = new();
        private readonly HashSet<ISavable> savableObjs = new();
        private IDataHandler dataHandler;
        private Coroutine autoSaveCoroutine;
        private Task initializationTask;

        private const float TimeToWaitSaveSettings = 3f;

        #region Initialization
        private void Initialize()
        {
            initializationTask = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            bool loadSettingsSuccess = await TryLoadSettings();

            InitializeDataHandler();
            InitializeProfile();
            SaveRegistry.Bind(Register, Unregister);

            await InternalLoadGame();

            StartAutoSave();

            Debug.Log("<color=green>[GameSaver] Initialized done!!!</color>");
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

        private async Task<bool> TryLoadSettings()
        {
            if (saveSettings != null) return true;

            var handle = Addressables.LoadAssetAsync<SaveSettings>(Constant.SAVE_SETTINGS_NAME);
            saveSettings = await handle.Task;

            if (saveSettings == null)
            {
                Debug.LogWarning($"<color=red>[GameSaver] Could not load SaveSettings from Addressables. A default SaveSettings object will be created. Please ensure a '{Constant.SAVE_SETTINGS_NAME}' asset exists and is configured in Addressables!!!</color>");
                saveSettings = ScriptableObject.CreateInstance<SaveSettings>();
            }
            else
            {
                handle.Release();
            }

            return true;
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
        public async Task NewGame(string profileId = null)
        {
            await initializationTask;
            InternalNewGame(profileId);
        }

        #endregion

        #region SaveGame
        public async Task SaveGame()
        {
            await initializationTask;

            if (!TryCreateProfileIfNull()) return;

            if (saveSettings.SaveAsSeparateFiles) SaveAsSeparate();
            else SaveAsAIO();

            void SaveAsAIO()
            {
                foreach (ISavable savable in savableObjs)
                {
                    savable.SaveData(saveData);
                }

                dataHandler.Write(saveData, curProfileId);
                Debug.Log("<color=green>[GameSaver] SaveAIO called!!!</color>");
            }

            void SaveAsSeparate()
            {
                foreach (ISavable savable in savableObjs)
                {
                    string moduleKey = savable.SaveType.Name;
                    savable.SaveData(saveData);

                    SaveModule(moduleKey);
                    Debug.Log($"<color=green>[GameSaver] Save {moduleKey} called!!!</color>");
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
                Task saveTask = SaveGame();
                yield return new WaitUntil(() => saveTask.IsCompleted);
                Debug.Log("<color=green>[GameSaver] AutoSave called!!!</color>");
            }
        }
        #endregion

        #region LoadGame

        public async Task<Dictionary<string, SaveData>> LoadAllProfile()
        {
            await initializationTask;
            Dictionary<string, SaveData> gameDatas = await dataHandler.ReadAllProfile();
            return gameDatas;
        }

        public async Task LoadGame()
        {
            await initializationTask;
            await InternalLoadGame();
        }

        private async Task LoadFromMultiFile()
        {
            if (!TryCreateProfileIfNull()) return;

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
                ISaveData data = await LoadModule(moduleKey);
                if (data != null) savable.LoadData(saveData);
            }
        }

        private async Task LoadFromSingleFile()
        {
            saveData = await dataHandler.Read<SaveData>(curProfileId);

            if (!TryCreateProfileIfNull()) return;

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
        private async void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) await SaveGame();
        }
#elif UNITY_EDITOR
        private async void OnApplicationQuit()
        {
            await SaveGame();
        }
#else
        private async void OnApplicationQuit()
        {
            await SaveGame();
        }
#endif
        #endregion

        #region Internal Method
        /// <summary>
        /// Worker method for NewGame. Doesn't wait for initialization.
        /// </summary>
        private void InternalNewGame(string profileId = null)
        {
            saveData = new SaveData();
            string id = string.IsNullOrEmpty(profileId) ? curProfileId : profileId;
            SetProfileID(id);

            Debug.Log($"<color=green>[GameSaver] Created new game with profile: {id}!!!</color>");
        }

        /// <summary>
        /// Worker method for LoadGame. Doesn't wait for initialization.
        /// </summary>
        private async Task InternalLoadGame()
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
        #endregion

        #region Helper
        public void SetProfileID(string id)
        {
            curProfileId = id;
        }

        public async Task DeleteData(string profileId)
        {
            await initializationTask;
            dataHandler.Delete(profileId);
            InitializeProfile();
            await LoadGame();
        }

        private string GetMostRecentProfile()
        {
            return dataHandler.GetMostRecentProfile();
        }

        /// <summary>
        /// Ensure save data exists; creates default profile if allowed.
        /// </summary>
        /// <returns> true if data already existed or was created; otherwise false.</returns> 
        private bool TryCreateProfileIfNull()
        {
            if (saveSettings.SaveAsSeparateFiles)
            {
                if (HasAnySaveFiles()) return true;
            }
            else
            {
                if (saveData != null) return true;
            }

            if (saveSettings.CreateProfileIfNull)
            {
                Debug.Log($"<color=yellow>[GameSaver] No data. Created new data with profile: {curProfileId}!!!</color>");
                InternalNewGame(curProfileId);
            }
            else
            {
                Debug.Log($"<color=yellow>[GameSaver] No data. Run NewGame() before LoadGame()/SaveGame()!!!</color>");
            }

            return saveSettings.CreateProfileIfNull;
        }

        private bool HasAnySaveFiles()
        {
            string root = Application.persistentDataPath;
            if (!Directory.Exists(root)) return false;

            string ext = string.IsNullOrEmpty(saveSettings.FileExtension) ? Constant.DEFAULT_FILE_SAVE_EXTENSION : saveSettings.FileExtension;
            if (!ext.StartsWith(".")) ext = "." + ext;

            try
            {
                return Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories).Any();
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
    #endregion
}
