using System.Collections.Generic;
using UnityEngine;
using ThanhDV.GameSaver.Helper;
using System.Threading.Tasks;
using System.Collections;
using UnityEngine.AddressableAssets;
using System.IO;
using System.Linq;
using ThanhDV.GameSaver.CustomAttribute;
using System;
using System.Threading;

namespace ThanhDV.GameSaver.Core
{
    public class GameSaver : MonoBehaviour
    {
#if !INJECTION_ENABLED
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
#else
        private void Awake()
        {
            Initialize();
            DontDestroyOnLoad(gameObject);
        }
#endif

        [UnderlineHeader("Config")]
        [SerializeField] private SaveSettings saveSettings;

        private string curProfileId;
        private SaveData saveData = new();
        private readonly HashSet<ISavable> savableObjs = new();
        private IDataHandler dataHandler;
        private Coroutine autoSaveCoroutine;

        #region Events

        private TaskCompletionSource<bool> initializationTCS;
        public Task WhenInitialized => initializationTCS?.Task ?? Task.CompletedTask;

        public static event Action<LoadStartArgs> OnLoadDataStarted;
        public static event Action<LoadCompleteArgs> OnLoadDataCompleted;
        public static event Action<SaveStartArgs> OnSaveDataStarted;
        public static event Action<SaveCompleteArgs> OnSaveDataCompleted;

        public sealed class LoadStartArgs : EventArgs
        {
            public string ProfileId { get; }

            public LoadStartArgs(string profileId)
            {
                ProfileId = profileId;
            }
        }

        public sealed class LoadCompleteArgs : EventArgs
        {
            public string ProfileId { get; }
            public SaveData Data { get; }

            public LoadCompleteArgs(string profileId, SaveData data)
            {
                ProfileId = profileId;
                Data = data;
            }
        }

        public sealed class SaveStartArgs : EventArgs
        {
            public string ProfileId { get; }
            public SaveData Data { get; }

            public SaveStartArgs(string profileId, SaveData data)
            {
                ProfileId = profileId;
                Data = data;
            }
        }

        public sealed class SaveCompleteArgs : EventArgs
        {
            public string ProfileId { get; }
            public bool IsSuccess { get; }

            public SaveCompleteArgs(string profileId, bool isSuccess)
            {
                ProfileId = profileId;
                IsSuccess = isSuccess;
            }
        }

        private void RaiseLoadStarted()
        {
            LoadStartArgs args = new(curProfileId);
            OnLoadDataStarted?.Invoke(args);
        }

        private void RaiseLoadCompleted()
        {
            LoadCompleteArgs args = new(curProfileId, saveData);
            OnLoadDataCompleted?.Invoke(args);
        }

        private void RaiseSaveStarted()
        {
            SaveStartArgs args = new(curProfileId, saveData);
            OnSaveDataStarted?.Invoke(args);
        }

        private void RaiseSaveCompleted(bool isSuccess)
        {
            SaveCompleteArgs args = new(curProfileId, isSuccess);
            OnSaveDataCompleted?.Invoke(args);
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            initializationTCS = new TaskCompletionSource<bool>();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            bool loadSettingsSuccess = await TryLoadSettings();

            InitializeDataHandler();
            InitializeProfile();

            await InternalLoadGame();

            SaveRegistry.Bind(Register, Unregister);

            StartAutoSave();

            Debug.Log("<color=green>[GameSaver] Initialized done!!!</color>");

            initializationTCS.TrySetResult(true);
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
                savableLoaded.OnLoadCompleted(saveData);
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
            await WhenInitialized;
            InternalNewGame(profileId);
        }

        #endregion

        #region SaveGame
        private enum SaveMode
        {
            Async,
            Immediate
        }

        private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

        // Coalesce state
        private readonly object _saveCoalesceLock = new();
        private bool _isSavingSequence;
        private bool _saveRequestedDuringSave;
        private SaveMode _nextMode = SaveMode.Async;
        private TaskCompletionSource<bool> _coalescedSaveTcs;

        public async Task SaveGameAsync()
        {
            await WhenInitialized;
            await RequestCoalescedSave(SaveMode.Async);
        }

        public async Task SaveGameImmediate()
        {
            await WhenInitialized;
            await RequestCoalescedSave(SaveMode.Immediate);
        }

        private Task RequestCoalescedSave(SaveMode mode)
        {
            lock (_saveCoalesceLock)
            {
                if (mode == SaveMode.Immediate)
                    _nextMode = SaveMode.Immediate;

                if (_isSavingSequence)
                {
                    _saveRequestedDuringSave = true;
                    return _coalescedSaveTcs.Task;
                }

                _isSavingSequence = true;
                _saveRequestedDuringSave = false;
                _coalescedSaveTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = RunSaveSequence();
                return _coalescedSaveTcs.Task;
            }
        }

        private async Task RunSaveSequence()
        {
            while (true)
            {
                SaveMode modeToRun;
                lock (_saveCoalesceLock)
                {
                    modeToRun = _nextMode;
                }

                await SaveGameEnqueued(modeToRun);

                lock (_saveCoalesceLock)
                {
                    if (_saveRequestedDuringSave)
                    {
                        _saveRequestedDuringSave = false;
                        continue;
                    }

                    _isSavingSequence = false;
                    _coalescedSaveTcs.TrySetResult(true);

                    _nextMode = SaveMode.Async;
                    return;
                }
            }
        }

        private async Task SaveGameEnqueued(SaveMode mode)
        {
            await _saveSemaphore.WaitAsync();
            try
            {
                await SaveGameInternal(mode);
            }
            finally
            {
                _saveSemaphore.Release();
            }
        }

        private async Task SaveGameInternal(SaveMode mode)
        {
            if (!TryCreateProfileIfNull()) return;

            RaiseSaveStarted();

            bool success = true;
            try
            {
                if (saveSettings.SaveAsSeparateFiles)
                    await SaveAllModules(mode);
                else
                    await SaveAllInOne(mode);
            }
            catch (Exception e)
            {
                Debug.Log($"<color=red>[GameSaver] Save failed for profile {curProfileId}!!!</color>\n{e}");
                success = false;
            }
            finally
            {
                RaiseSaveCompleted(success);
            }
        }

        private async Task SaveAllInOne(SaveMode mode)
        {
            foreach (ISavable savable in savableObjs)
            {
                savable.SaveData(saveData);
            }

            if (mode == SaveMode.Immediate)
                dataHandler.WriteImmediate(saveData, curProfileId);
            else
                await dataHandler.WriteAsync(saveData, curProfileId);

            Debug.Log("<color=green>[GameSaver] SaveAIO called!!!</color>");
        }

        private async Task SaveAllModules(SaveMode mode)
        {
            foreach (ISavable savable in savableObjs)
            {
                string moduleKey = savable.SaveType.Name;
                savable.SaveData(saveData);
                await SaveModuleInternal(moduleKey, mode);
                Debug.Log($"<color=green>[GameSaver] Save {moduleKey} called!!!</color>");
            }
        }

        private Task SaveModuleInternal(string moduleKey, SaveMode mode)
        {
            saveData ??= new();
            if (!saveData.TryGetData(moduleKey, out ISaveData data) || data == null)
            {
                Debug.Log($"<color=red>[GameSaver] SaveModule({moduleKey}) called with null data!!!</color>");
                return Task.CompletedTask;
            }

            if (mode == SaveMode.Immediate)
            {
                dataHandler.WriteModuleImmediate(data, moduleKey, curProfileId);
                return Task.CompletedTask;
            }

            return dataHandler.WriteModuleAsync(data, moduleKey, curProfileId);
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
                Task saveTask = SaveGameAsync();
                yield return new WaitUntil(() => saveTask.IsCompleted);
                Debug.Log("<color=green>[GameSaver] AutoSave called!!!</color>");
            }
        }
        #endregion

        #region LoadGame

        public async Task<Dictionary<string, SaveData>> LoadAllProfile()
        {
            await WhenInitialized;
            Dictionary<string, SaveData> gameDatas = await dataHandler.ReadAllProfile();
            return gameDatas;
        }

        public async Task LoadGame()
        {
            await WhenInitialized;
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

                savableLoaded.OnLoadCompleted(saveData);
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
                savable.LoadData(saveData);
            }

            foreach (ISavable savable in savableObjs)
            {
                if (savable is not ISavableLoaded savableLoaded) continue;

                savableLoaded.OnLoadCompleted(saveData);
            }

            Debug.Log($"<color=green>[GameSaver] Data loaded (Profile: {curProfileId})</color>");
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

#if UNITY_EDITOR
        private async void OnApplicationQuit()
        {
            await SaveGameImmediate();
        }
#elif UNITY_ANDROID || UNITY_IOS
        private async void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) await SaveGameImmediate();
        }
#else
        private async void OnApplicationQuit()
        {
            await SaveGameImmediate();
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
            RaiseLoadStarted();

            if (saveSettings.SaveAsSeparateFiles)
            {
                await LoadFromMultiFile();
            }
            else
            {
                await LoadFromSingleFile();
            }

            RaiseLoadCompleted();
        }
        #endregion

        #region Helper
        public void SetProfileID(string id)
        {
            curProfileId = id;
        }

        public async Task DeleteData(string profileId)
        {
            await WhenInitialized;
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
