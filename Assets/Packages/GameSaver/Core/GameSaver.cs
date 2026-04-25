using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ThanhDV.GameSaver.Common;

namespace ThanhDV.GameSaver.Core
{
    public class GameSaver : IDisposable
    {
        private readonly SaveRegistry _registry;
        private readonly IStorageProvider _storageProvider;
        private readonly ISerializer _serializer;
        private readonly IEncryptionProvider _encryptionProvider;
        private readonly SaveSettings _settings;

        private SaveData _curSaveData = new();
        private string _curProfileId; public string CurrentProfileId => _curProfileId;

        /// <summary>
        /// Initializes a new instance of the GameSaver class.
        /// It manages serialization, storage, and persistence operations. Subscribing to registry changes enables 
        /// automatic restoration of newly registered objects and captures state for the ones being unregistered.
        /// </summary>
        public GameSaver(SaveRegistry registry, IStorageProvider storageProvider, ISerializer serializer, IEncryptionProvider encryptionProvider, SaveSettings settings)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _encryptionProvider = encryptionProvider ?? throw new ArgumentNullException(nameof(encryptionProvider));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _registry.OnSavableRegistered += HandleRegistration;
            _registry.OnSavableUnregistered += HandleUnregistration;
        }

        #region Profile Management

        /// <summary>
        /// Retrieves the ID of the most recently modified profile.
        /// </summary>
        /// <returns>The most recent profile ID, or null if no profiles exist.</returns>
        public string GetMostRecentProfileId()
        {
            return _storageProvider.GetMostRecentProfileId();
        }

        /// <summary>
        /// Retrieves a collection of all available profile IDs.
        /// </summary>
        /// <returns>An enumerable collection of profile ID strings.</returns>
        public IEnumerable<string> GetAllProfiles()
        {
            return _storageProvider.GetAllProfileIds();
        }

        /// <summary>
        /// Deletes the specified profile and its associated save data from storage.
        /// If the deleted profile is the currently active one, the current profile state is completely cleared.
        /// </summary>
        /// <param name="profileId">The ID of the profile to delete.</param>
        public void DeleteProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return;

            _storageProvider.DeleteProfile(profileId);

            if (_curProfileId == profileId)
            {
                _curProfileId = null;
                _curSaveData = new();
            }

            DebugLog.Success($"Successfully deleted profile: {profileId}");
        }

        #endregion

        #region Save/Load

        /// <summary>
        /// Asynchronously saves the current game state to the specified profile.
        /// </summary>
        /// <param name="profileId">The target profile ID. If null, the currently active profile is used.</param>
        /// <returns>A handle to track the progress and completion of the save operation.</returns>
        public GameSaverOperationHandle SaveAsync(string profileId = null)
        {
            GameSaverOperationInternal internalOp = new();

            _ = ProcessSaveAsync(profileId, internalOp);

            return new GameSaverOperationHandle(internalOp);
        }

        /// <summary>
        /// Synchronously captures and saves the current game state to the specified profile.
        /// This is a blocking operation and should be used with caution to avoid frame drops.
        /// </summary>
        /// <param name="profileId">The target profile ID. If null, the currently active profile is used.</param>
        /// <exception cref="InvalidOperationException">Thrown when no valid profile ID can be determined.</exception>
        public void SaveImmediate(string profileId = null)
        {
            string targetProfile = profileId ?? _curProfileId;

            if (string.IsNullOrEmpty(targetProfile)) throw new InvalidOperationException("Cannot save: No valid ProfileId was provided or found.");

            try
            {
                CaptureAllSavables();

                string data = _serializer.Serialize(_curSaveData);
                string finalData = _settings.UseEncryption ? _encryptionProvider.Encrypt(data) : data;
                string fileName = _settings.FileName + _settings.FileExtension;

                _storageProvider.WriteImmediate(targetProfile, fileName, finalData);

                _curProfileId = targetProfile;

                DebugLog.Success("Immediate save operation successful!");
            }
            catch (Exception e)
            {
                DebugLog.Error($"Immediate save failed: {e.Message}");
                throw;
            }
        }

        /// <summary>
        /// Restores the backup save file for the specified profile, replacing any broken primary save data with a healthy backup.
        /// </summary>
        /// <param name="profileId">The ID of the profile to restore from its corresponding backup.</param>
        /// <exception cref="ArgumentNullException">Thrown when the provided profileId is null or empty.</exception>
        public void RestoreBackup(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId), "ProfileId cannot be null or empty.");

            string fileName = _settings.FileName + _settings.FileExtension;

            _storageProvider.RestoreBackup(profileId, fileName);
        }

        /// <summary>
        /// Asynchronously loads the game state from the specified profile and restores it to all registered savables.
        /// </summary>
        /// <param name="profileId">The ID of the profile to load.</param>
        /// <returns>A handle to track the progress and completion of the load operation.</returns>
        public GameSaverOperationHandle LoadAsync(string profileId)
        {
            GameSaverOperationInternal internalOp = new();

            _ = ProcessLoadAsync(profileId, false, internalOp);

            return new GameSaverOperationHandle(internalOp);
        }

        /// <summary>
        /// Asynchronously loads the game state from the backup file rather than the primary file. 
        /// Use this when the main save is corrupted or missing.
        /// </summary>
        /// <param name="profileId">The ID of the profile to load backup data from.</param>
        /// <returns>A handle to track the progress and completion of the load operation.</returns>
        public GameSaverOperationHandle LoadBackupAsync(string profileId)
        {
            GameSaverOperationInternal internalOp = new();

            _ = ProcessLoadAsync(profileId, true, internalOp);

            return new GameSaverOperationHandle(internalOp);
        }

        /// <summary>
        /// Attempts to lazily resolve the most recently saved profile and loads it asynchronously. 
        /// </summary>
        /// <returns>A handle tracking completion. It will fault if no previous profiles are found.</returns>
        public GameSaverOperationHandle LoadMostRecentAsync()
        {
            GameSaverOperationInternal internalOp = new();

            try
            {
                string mostRecentProfile = _storageProvider.GetMostRecentProfileId();

                if (string.IsNullOrEmpty(mostRecentProfile))
                {
                    FileNotFoundException e = new("No previously saved Profile could be found.");
                    internalOp.Complete(e);
                    return new GameSaverOperationHandle(internalOp);
                }

                _ = ProcessLoadAsync(mostRecentProfile, false, internalOp);
            }
            catch (Exception e)
            {
                internalOp.Complete(e);
            }

            return new GameSaverOperationHandle(internalOp);
        }

        #endregion

        #region Process Save/Load

        /// <summary>
        /// Orchestrates the asynchronous, multi-step workflow required for saving: 
        /// capturing dynamic state, serializing, optionally encrypting, and persistently writing out.
        /// </summary>
        /// <param name="profileId">The target profile ID, which defaults to current if omitted.</param>
        /// <param name="internalOp">Operation context to report completion and progress scaling.</param>
        private async Task ProcessSaveAsync(string profileId, GameSaverOperationInternal internalOp)
        {
            try
            {
                string targetProfile = profileId ?? _curProfileId;

                if (string.IsNullOrEmpty(targetProfile)) throw new InvalidOperationException("Cannot save: No valid ProfileId was provided or found.");

                internalOp.PercentComplete = 0.1f;
                // Capture the current states of all tracked savables before persisting
                CaptureAllSavables();

                internalOp.PercentComplete = 0.2f;
                string finalData = await Task.Run(() =>
                {
                    string data = _serializer.Serialize(_curSaveData);
                    return _settings.UseEncryption ? _encryptionProvider.Encrypt(data) : data;
                });
                internalOp.PercentComplete = 0.5f;

                string fileName = _settings.FileName + _settings.FileExtension;
                await _storageProvider.WriteAsync(targetProfile, fileName, finalData);

                _curProfileId = targetProfile;
                internalOp.PercentComplete = 0.8f;

                internalOp.Complete();
            }
            catch (Exception e)
            {
                internalOp.Complete(e);
            }
        }

        /// <summary>
        /// Orchestrates the asynchronous, multi-step workflow required for loading: 
        /// reading from primary or backup storage, optionally decrypting on a separate thread, parsing string data, and progressively restoring registered objects.
        /// </summary>
        /// <param name="profileId">The ID to strictly load from.</param>
        /// <param name="isBackup">Indicator denoting whether the raw storage layer reads from standard file or backup.</param>
        /// <param name="internalOp">Operation context to report completion and progress scaling.</param>
        private async Task ProcessLoadAsync(string profileId, bool isBackup, GameSaverOperationInternal internalOp)
        {
            try
            {
                if (string.IsNullOrEmpty(profileId)) throw new InvalidOperationException("Cannot load: No valid ProfileId was provided or found.");

                string fileName = _settings.FileName + _settings.FileExtension;
                string rawData;

                internalOp.PercentComplete = 0.1f;
                // Fetch basic structured data, parsing depends on whether reading standard save or backup
                if (isBackup)
                {
                    rawData = await _storageProvider.ReadBackupAsync(profileId, fileName);
                }
                else
                {
                    rawData = await _storageProvider.ReadAsync(profileId, fileName);
                }

                internalOp.PercentComplete = 0.3f;
                // Execute decryption and deserialization on a background thread for performance
                SaveData loadedData = await Task.Run(() =>
                {
                    string data = _settings.UseEncryption ? _encryptionProvider.Decrypt(rawData) : rawData;
                    return _serializer.Deserialize<SaveData>(data);
                });

                _curSaveData = loadedData ?? new();
                _curProfileId = profileId;

                List<ISavable> savables = _registry.Savables.ToList();
                int objectRegisteredCount = savables.Count;

                if (objectRegisteredCount > 0)
                {
                    float startProgress = 0.4f;
                    float progressRange = 0.6f;

                    // Progressively inject loaded state back into each registered object
                    for (int i = 0; i < objectRegisteredCount; i++)
                    {
                        RestoreSavable(savables[i], _curSaveData);

                        internalOp.PercentComplete = startProgress + ((float)(i + 1) / objectRegisteredCount) * progressRange;
                    }
                }
                else
                {
                    internalOp.PercentComplete = 1f;
                }

                DebugLog.Success($"Successfully loaded {(isBackup ? "Backup" : "Primary")} data for Profile: {profileId}");

                internalOp.Complete();
            }
            catch (Exception e)
            {
                DebugLog.Error($"[GameSaver] Failed to load data: {e.Message}");

                internalOp.Complete(e);
            }
        }

        #endregion

        #region The Safety Net

        /// <summary>
        /// Handled whenever a new ISavable registers itself. 
        /// Automatically forces a restoration into its context based on the current parsed save data.
        /// </summary>
        /// <param name="savable">The newly observed object.</param>
        private void HandleRegistration(ISavable savable)
        {
            RestoreSavable(savable);
        }

        /// <summary>
        /// Handled roughly before an ISavable unregisters itself (e.g. gets destroyed).
        /// Automatically forces a capture of its context back into the global save data cache beforehand so state isn't lost.
        /// </summary>
        /// <param name="savable">The object preparing to detach.</param>
        private void HandleUnregistration(ISavable savable)
        {
            CaptureSavable(savable);
        }

        /// <summary>
        /// Tears down references to the registry. Highly recommended to explicitly call this to prevent lingering objects.
        /// </summary>
        public void Dispose()
        {
            _registry.OnSavableRegistered -= HandleRegistration;
            _registry.OnSavableUnregistered -= HandleUnregistration;
        }

        #endregion

        #region Helper

        /// <summary>
        /// Reads isolated stored configuration blocks based on a savable's Key, and forces states appropriately.
        /// Does not throw if data has never been persisted for the specific key context; simply pushes null.
        /// </summary>
        /// <param name="savable">The ISavable requiring deserialized data injection.</param>
        private void RestoreSavable(ISavable savable)
        {
            RestoreSavable(savable, _curSaveData);
        }

        /// <summary>
        /// Restores a savable from the provided save-data snapshot instead of the current in-memory profile.
        /// This is used during load so state can be applied before the new profile is committed.
        /// </summary>
        /// <param name="savable">The savable instance to restore.</param>
        /// <param name="sourceSaveData">The save-data snapshot used for lookup.</param>
        private void RestoreSavable(ISavable savable, SaveData sourceSaveData)
        {
            sourceSaveData.DataModules.TryGetValue(savable.SaveKey, out ISaveData saveData);
            savable.RestoreData(saveData);
        }

        /// <summary>
        /// Queries an established ISavable for an ISaveData bundle representing entirely mutable variables.
        /// Safely ignores instances that respond with completely empty data definitions to avoid footprint overhead.
        /// </summary>
        /// <param name="savable">The targeted ISavable expected to serialize its context variables locally.</param>
        private void CaptureSavable(ISavable savable)
        {
            ISaveData saveData = savable.CaptureData();

            if (saveData == null) return;

            _curSaveData.DataModules[savable.SaveKey] = saveData;
        }

        /// <summary>
        /// Sweeps through the registry to unconditionally process all tracked valid ISavables at once, requesting state captures asynchronously or synchronously.
        /// </summary>
        private void CaptureAllSavables()
        {
            foreach (ISavable savable in _registry.Savables)
            {
                CaptureSavable(savable);
            }
        }

        #endregion
    }
}
