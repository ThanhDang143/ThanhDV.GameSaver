using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ThanhDV.GameSaver.Common;
using UnityEngine;

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
        private string _curProfileId;
        private float _autoSaveCountdown;

        /// <summary>
        /// Caller's SynchronizationContext captured at construction. Used to marshal OnSaveCompleted
        /// invocations and the auto-save countdown reset back to the original thread (typically Unity main thread).
        /// </summary>
        private readonly SynchronizationContext _capturedContext;

        /// <summary>
        /// Raised after every successful save (async or immediate) with the profile ID that was saved.
        /// Always fires on the thread where this GameSaver was constructed — safe for Unity API calls.
        /// </summary>
        public event Action<string> OnSaveCompleted;

        private string MetaName => _settings.FileName + _settings.MetaExtension;
        private string SaveName => _settings.FileName + _settings.SaveExtension;

        /// <summary>
        /// Per-profile save state for concurrency control. Each entry represents a profile that currently has
        /// a save pipeline running and/or coalesced trailing requests waiting. Access must be guarded by _stateLock.
        /// </summary>
        private readonly Dictionary<string, ProfileSaveState> _saveStates = new();

        /// <summary>
        /// Lock guarding _saveStates, _curSaveData (both inner dictionaries), and _curProfileId.
        /// Held only for short, await-free critical sections to avoid serializing IO.
        /// </summary>
        private readonly object _stateLock = new();

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
            _capturedContext = SynchronizationContext.Current;

            _registry.OnSavableRegistered += HandleRegistration;
            _registry.OnSavableUnregistered += HandleUnregistration;

            Application.quitting += HandleApplicationQuitting;
            Application.focusChanged += HandleFocusChanged;

            ResetAutoSaveCountdown();
            AutoSaveTicker.Subscribe(this);
        }

        #region Profile Management

        /// <summary>
        /// Asynchronously fetches the metadata for all existing profiles.
        /// This is particularly useful for constructing save-slot selection menus.
        /// </summary>
        /// <typeparam name="T">The type of metadata model, implementing <see cref="ISaveMeta"/>.</typeparam>
        /// <returns>A tracking handle providing a list of all successfully parsed metadata files.</returns>
        public GameSaverOperationHandle<List<T>> GetAllMetadataAsync<T>() where T : class, ISaveMeta
        {
            GameSaverOperationInternal<List<T>> internalOp = new();

            _ = ProcessLoadAllMetadataAsync(internalOp);

            return new GameSaverOperationHandle<List<T>>(internalOp);
        }

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
        /// <exception cref="InvalidOperationException">
        /// Thrown when a save operation is currently in flight for the same profile.
        /// Wait for the save to complete (or for trailing saves to drain) before deleting.
        /// </exception>
        public void DeleteProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return;

            lock (_stateLock)
            {
                // Reject if a save (async or immediate) is in flight for this profile.
                if (_saveStates.TryGetValue(profileId, out ProfileSaveState saveState) && saveState.IsRunning)
                {
                    throw new InvalidOperationException($"Cannot delete profile '{profileId}' while a save operation is in flight for the same profile.");
                }

                _storageProvider.DeleteProfile(profileId);

                if (_curProfileId == profileId)
                {
                    _curProfileId = null;
                    _curSaveData = new();
                }

                _saveStates.Remove(profileId);
            }

            DebugLog.Success($"Successfully deleted profile: {profileId}");
        }

        #endregion

        #region Save/Load

        /// <summary>
        /// Asynchronously saves the current game state to the specified profile.
        /// </summary>
        /// <param name="profileId">The target profile ID. If null, the currently active profile is used.</param>
        /// <param name="metadata">UI display metadata (implements ISaveMeta).</param>
        /// <returns>A handle to track the progress and completion of the save operation.</returns>
        public GameSaverOperationHandle SaveAsync(string profileId = null, ISaveMeta metadata = null)
        {
            GameSaverOperationInternal internalOp = new();

            string targetProfile = profileId ?? _curProfileId;
            bool isImplicit = profileId == null;

            if (string.IsNullOrEmpty(targetProfile))
            {
                InvalidOperationException error = new("Cannot save: No valid ProfileId was provided or found.");
                internalOp.Complete(error);
                return new GameSaverOperationHandle(internalOp);
            }

            bool rejectedBySaveImmediate = false;
            lock (_stateLock)
            {
                if (!_saveStates.TryGetValue(targetProfile, out ProfileSaveState saveState))
                {
                    saveState = new();
                    _saveStates[targetProfile] = saveState;
                }

                if (saveState.IsRunning)
                {
                    if (saveState.IsSaveImmediate)
                    {
                        // A SaveImmediate is in flight — cannot coalesce into a synchronous run.
                        rejectedBySaveImmediate = true;
                    }
                    else
                    {
                        // Normal coalesce: another SaveAsync is running.
                        saveState.IsDirty = true;
                        if (metadata != null) saveState.PendingMetadata = metadata;
                        saveState.PendingHandles.Add(internalOp);
                    }
                }
                else
                {
                    // No save in flight — claim the slot and start the leading pipeline.
                    saveState.RunningTask = ProcessSaveAsync(targetProfile, isImplicit, metadata, internalOp);
                }
            }

            if (rejectedBySaveImmediate)
            {
                InvalidOperationException error = new($"Cannot SaveAsync for profile '{targetProfile}' while a SaveImmediate is in flight.");
                internalOp.Complete(error);
            }

            return new GameSaverOperationHandle(internalOp);
        }

        /// <summary>
        /// Synchronously captures and saves the current game state to the specified profile.
        /// This is a blocking operation and should be used with caution to avoid frame drops.
        /// </summary>
        /// <param name="profileId">The target profile ID. If null, the currently active profile is used.</param>
        /// <param name="metadata">UI display metadata (implements ISaveMeta).</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no valid profile ID can be determined, or when another save operation is already in flight for the target profile.
        /// </exception>
        public void SaveImmediate(string profileId = null, ISaveMeta metadata = null)
        {
            string targetProfile = profileId ?? _curProfileId;
            bool isImplicit = profileId == null;

            if (string.IsNullOrEmpty(targetProfile)) throw new InvalidOperationException("Cannot save: No valid ProfileId was provided or found.");

            SaveData snapshot;
            lock (_stateLock)
            {
                if (_saveStates.TryGetValue(targetProfile, out ProfileSaveState existingSaveState) && existingSaveState.IsRunning)
                {
                    throw new InvalidOperationException($"Cannot SaveImmediate for profile '{targetProfile}' while another save operation is in flight.");
                }

                ProfileSaveState saveState = existingSaveState ?? new();
                saveState.IsSaveImmediate = true;
                _saveStates[targetProfile] = saveState;

                CaptureAllSavables();
                snapshot = _curSaveData.Clone();
            }

            try
            {
                string data = _serializer.Serialize(snapshot);
                string finalData = _settings.UseEncryption ? _encryptionProvider.Encrypt(data) : data;

                _storageProvider.WriteImmediate(targetProfile, SaveName, finalData);

                if (metadata != null)
                {
                    metadata.ProfileID = targetProfile;
                    metadata.LastTimeSaved = DateTime.UtcNow;

                    string metaJson = _serializer.Serialize(metadata);
                    _storageProvider.WriteImmediate(targetProfile, MetaName, metaJson);
                }

                if (isImplicit) lock (_stateLock) _curProfileId = targetProfile;

                NotifySaveCompleted(targetProfile);

                DebugLog.Success("Immediate save operation successful!");
            }
            catch (Exception e)
            {
                DebugLog.Error($"Immediate save failed: {e.Message}");
                throw;
            }
            finally
            {
                // Release the slot. Because IsImmediate=true rejected all SaveAsync coalesce attempts,
                // there should be no pending handles or dirty flag here.
                lock (_stateLock)
                {
                    if (_saveStates.TryGetValue(targetProfile, out ProfileSaveState saveState))
                    {
                        saveState.IsSaveImmediate = false;

                        if (!saveState.IsDirty && saveState.PendingHandles.Count == 0)
                        {
                            _saveStates.Remove(targetProfile);
                        }
                    }
                }
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

            _storageProvider.RestoreBackup(profileId, SaveName);
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
        /// Orchestrates the save workflow for a single profile, including the trailing loop that drains
        /// coalesced SaveAsync calls that arrived while the leading pipeline was running.
        /// </summary>
        /// <param name="targetProfile">The resolved profile ID to save to. Must be non-empty.</param>
        /// <param name="isImplicit">True if the original SaveAsync call passed null (use current profile). Controls _curProfileId update.</param>
        /// <param name="metadata">UI display metadata (implements ISaveMeta).</param>
        /// <param name="internalOp">Operation context to report completion and progress scaling.</param>
        private async Task ProcessSaveAsync(string targetProfile, bool isImplicit, ISaveMeta metadata, GameSaverOperationInternal internalOp)
        {
            await ProcessSingleSaveAsync(targetProfile, isImplicit, metadata, internalOp).ConfigureAwait(false);

            // Trailing loop: drain coalesced SaveAsync calls.
            int iterations = 0;
            while (true)
            {
                ISaveMeta trailingMetatdata;
                GameSaverOperationInternal[] pendingOps;

                lock (_stateLock)
                {
                    if (!_saveStates.TryGetValue(targetProfile, out ProfileSaveState saveState)) return;

                    if (!saveState.IsDirty)
                    {
                        saveState.RunningTask = null;
                        _saveStates.Remove(targetProfile);
                        return;
                    }

                    trailingMetatdata = saveState.PendingMetadata;
                    pendingOps = saveState.PendingHandles.ToArray();
                    saveState.PendingHandles.Clear();
                    saveState.PendingMetadata = null;
                    saveState.IsDirty = false;
                }

                iterations++;
                if (iterations > 2)
                {
                    DebugLog.Warning($"Save trailing loop running iteration #{iterations} for profile '{targetProfile}' — possible reentrancy.");
                }

                // Trailing pipeline is never "implicit" — it always targets the resolved profile explicitly.
                // The leading pipeline already updated _curProfileId if needed.
                GameSaverOperationInternal trailingOp = new();
                await ProcessSingleSaveAsync(targetProfile, false, trailingMetatdata, trailingOp).ConfigureAwait(false);

                for (int i = 0; i < pendingOps.Length; i++)
                {
                    GameSaverOperationInternal op = pendingOps[i];
                    op.Complete(trailingOp.Error);
                }
            }
        }

        /// <summary>
        /// Executes a single save pipeline pass:
        /// capturing dynamic state, serializing, optionally encrypting, and persistently writing out.
        /// </summary>
        /// <param name="targetProfile">The target profile ID, which defaults to current if omitted.</param>
        /// <param name="isImplicit">True if the original SaveAsync call passed null (use current profile). Controls _curProfileId update.</param>
        /// <param name="internalOp">Operation context to report completion and progress scaling.</param>
        /// <param name="metadata">UI display metadata (implements ISaveMeta).</param>
        private async Task ProcessSingleSaveAsync(string targetProfile, bool isImplicit, ISaveMeta metadata, GameSaverOperationInternal internalOp)
        {
            try
            {
                internalOp.PercentComplete = 0.1f;

                // Snapshot state under lock; serialization on background threads avoids shared-state corruption.
                SaveData snapshot;
                lock (_stateLock)
                {
                    CaptureAllSavables();
                    snapshot = _curSaveData.Clone();
                }

                internalOp.PercentComplete = 0.2f;
                string finalData = await Task.Run(() =>
                {
                    string data = _serializer.Serialize(snapshot);
                    return _settings.UseEncryption ? _encryptionProvider.Encrypt(data) : data;
                }).ConfigureAwait(false);
                internalOp.PercentComplete = 0.5f;

                await _storageProvider.WriteAsync(targetProfile, SaveName, finalData).ConfigureAwait(false);

                internalOp.PercentComplete = 0.8f;
                if (metadata != null)
                {
                    metadata.ProfileID = targetProfile;
                    metadata.LastTimeSaved = DateTime.UtcNow;

                    string metaJson = await Task.Run(() => _serializer.Serialize(metadata)).ConfigureAwait(false);
                    await _storageProvider.WriteAsync(targetProfile, MetaName, metaJson).ConfigureAwait(false);
                }

                if (isImplicit) lock (_stateLock) _curProfileId = targetProfile;

                NotifySaveCompleted(targetProfile);

                internalOp.PercentComplete = 1f;
                internalOp.Complete();
            }
            catch (Exception e)
            {
                internalOp.Complete(e);
            }
        }

        /// <summary>
        /// Orchestrates the asynchronous, multi-step workflow required for loading: 
        /// rejecting the request if a save is in flight for the same profile, reading from primary or backup storage,
        /// decrypting and parsing on a background thread, and progressively restoring registered objects.
        /// </summary>
        /// <param name="profileId">The ID to strictly load from.</param>
        /// <param name="isBackup">Indicator denoting whether the raw storage layer reads from standard file or backup.</param>
        /// <param name="internalOp">Operation context to report completion and progress scaling.</param>
        private async Task ProcessLoadAsync(string profileId, bool isBackup, GameSaverOperationInternal internalOp)
        {
            try
            {
                if (string.IsNullOrEmpty(profileId)) throw new InvalidOperationException("Cannot load: No valid ProfileId was provided or found.");

                lock (_stateLock)
                {
                    if (_saveStates.TryGetValue(profileId, out ProfileSaveState saveState) && saveState.IsRunning)
                    {
                        throw new InvalidOperationException($"Cannot load profile '{profileId}' while a save operation is in flight for the same profile.");
                    }
                }

                string rawData;

                internalOp.PercentComplete = 0.1f;
                // Fetch basic structured data, parsing depends on whether reading standard save or backup
                if (isBackup)
                {
                    rawData = await _storageProvider.ReadBackupAsync(profileId, SaveName);
                }
                else
                {
                    rawData = await _storageProvider.ReadAsync(profileId, SaveName);
                }

                internalOp.PercentComplete = 0.3f;
                // Execute decryption and deserialization on a background thread for performance
                SaveData loadedData = await Task.Run(() =>
                {
                    string data = _settings.UseEncryption ? _encryptionProvider.Decrypt(rawData) : rawData;
                    return _serializer.Deserialize<SaveData>(data);
                });

                // Capture snapshots to isolate restore from concurrent SetSimple mutations.
                List<ISavable> savableSnapshots;
                SaveData restoreSnapshot;
                lock (_stateLock)
                {
                    _curSaveData = loadedData ?? new();
                    _curProfileId = profileId;
                    savableSnapshots = _registry.Savables.ToList();
                    restoreSnapshot = _curSaveData.Clone();
                }

                int objectRegisteredCount = savableSnapshots.Count;

                if (objectRegisteredCount > 0)
                {
                    float startProgress = 0.4f;
                    float progressRange = 0.6f;

                    // Progressively inject loaded state back into each registered object
                    // Reads from restoreSnapshot (local) — unaffected by concurrent SetSimple or parallel capture.
                    for (int i = 0; i < objectRegisteredCount; i++)
                    {
                        RestoreSavable(savableSnapshots[i], restoreSnapshot);

                        internalOp.PercentComplete = startProgress + (float)(i + 1) / objectRegisteredCount * progressRange;
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
                DebugLog.Error($"Failed to load data: {e.Message}");

                internalOp.Complete(e);
            }
        }

        /// <summary>
        /// Asynchronously loads and deserializes metadata from all available profiles.
        /// The resulting collection is sorted by the most recent save time.
        /// Corrupted or missing metadata files are safely skipped without halting the overall process.
        /// </summary>
        /// <typeparam name="T">The concrete metadata type, which must implement <see cref="ISaveMeta"/>.</typeparam>
        /// <param name="internalOp">Operation context to report status, completion, and progress scaling.</param>
        private async Task ProcessLoadAllMetadataAsync<T>(GameSaverOperationInternal<List<T>> internalOp) where T : class, ISaveMeta
        {
            try
            {
                List<string> profiles = _storageProvider.GetAllProfileIds().ToList();
                List<T> metadatas = new();
                int profileCount = profiles.Count;

                if (profileCount == 0)
                {
                    internalOp.Complete(metadatas);
                    return;
                }

                for (int i = 0; i < profileCount; i++)
                {
                    string profile = profiles[i];

                    if (_storageProvider.Exists(profile, MetaName))
                    {
                        T metadata = null;

                        try
                        {
                            string metaJson = await _storageProvider.ReadAsync(profile, MetaName);
                            metadata = await Task.Run(() => _serializer.Deserialize<T>(metaJson));
                        }
                        catch (Exception primaryEx)
                        {
                            // Intentionally do not throw and try to load backup
                            DebugLog.Warning($"Primary Metadata file for Profile '{profile}' is corrupted ({primaryEx.Message}). Attempting to load from Backup...");

                            try
                            {
                                string backupJson = await _storageProvider.ReadBackupAsync(profile, MetaName);
                                metadata = await Task.Run(() => _serializer.Deserialize<T>(backupJson));

                                DebugLog.Success($"Successfully loaded Metadata from Backup file for Profile '{profile}'.");
                            }
                            catch (Exception backupEx)
                            {
                                DebugLog.Error($"Both primary and Backup files for Profile '{profile}' are corrupted ({backupEx.Message}). Skipping this Slot.");
                            }
                        }

                        if (metadata != null) metadatas.Add(metadata);
                    }

                    internalOp.PercentComplete = (float)(i + 1) / profileCount;
                }

                // Sort by most recent first
                metadatas.Sort((a, b) => b.LastTimeSaved.CompareTo(a.LastTimeSaved));
                internalOp.Complete(metadatas);
            }
            catch (Exception e)
            {
                DebugLog.Error($"Failed to load metadata: {e.Message}");
                internalOp.Complete(null, e);
            }
        }

        /// <summary>
        /// Awaits all pending async save pipelines, looping to catch new SaveAsync calls.
        /// Use before scene transitions or before SaveImmediate.
        /// Does not interact with sync saves or rethrow errors (routed to individual handles).
        /// </summary>
        public async Task WaitForPendingOperationsAsync()
        {
            while (true)
            {
                Task[] runningTasks;
                lock (_stateLock)
                {
                    runningTasks = _saveStates.Values.Select(s => s.RunningTask).Where(t => t != null).ToArray();
                }

                if (runningTasks.Length <= 0) return;

                try
                {
                    await Task.WhenAll(runningTasks).ConfigureAwait(false);
                }
                catch
                {
                    // Errors already routed to individual operation handles.
                    // This method only awaits completion, not success/failure of each save.
                }
            }
        }

        #endregion

        #region Direct Access

        /// <summary>
        /// Directly stores a primitive variable (int, float, string, etc.) or a small struct in memory.
        /// A convenience API similar to Easy Save.
        /// Note: You still need to call SaveAsync() or SaveImmediate() to persist the data to disk.
        /// </summary>
        /// <typeparam name="T">The type of the value being stored.</typeparam>
        /// <param name="key">The unique identifier for the value.</param>
        /// <param name="value">The value to store.</param>
        public void SetSimple<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
            {
                DebugLog.Error("Cannot set data: Key is null or empty.");
                return;
            }

            string serializedValue = _serializer.Serialize(value);

            lock (_stateLock) _curSaveData.SimpleData[key] = serializedValue;
        }

        /// <summary>
        /// Retrieves a value directly from memory by its key. Returns the provided defaultValue if it was never saved.
        /// </summary>
        /// <typeparam name="T">The expected type of the value.</typeparam>
        /// <param name="key">The unique identifier for the value.</param>
        /// <param name="defaultValue">The fallback value to return if the key doesn't exist or parsing fails. Defaults to default(T).</param>
        /// <returns>The deserialized value of type T, or the defaultValue if not found.</returns>
        public T GetSimple<T>(string key, T defaultValue = default)
        {
            if (string.IsNullOrEmpty(key))
            {
                DebugLog.Error("Cannot get data: Key is null or empty.");
                return defaultValue;
            }

            string serializedValue;
            bool found;
            lock (_stateLock) found = _curSaveData.SimpleData.TryGetValue(key, out serializedValue);

            if (!found) return defaultValue;

            try
            {
                return _serializer.Deserialize<T>(serializedValue);
            }
            catch (Exception e)
            {
                DebugLog.Warning($"Failed to parse data for key '{key}': {e.Message}. Returning default value.");
                return defaultValue;
            }
        }

        /// <summary>
        /// Checks if a specific key has been saved in memory.
        /// </summary>
        /// <param name="key">The unique identifier to check.</param>
        /// <returns>True if the key exists, otherwise false.</returns>
        public bool HasSimpleKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                DebugLog.Error("Cannot check key: Key is null or empty.");
                return false;
            }

            lock (_stateLock) return _curSaveData.SimpleData.ContainsKey(key);
        }

        /// <summary>
        /// Deletes a specific key and its associated value from memory.
        /// </summary>
        /// <param name="key">The unique identifier of the data to remove.</param>
        public void DeleteSimple(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                DebugLog.Error("Cannot delete data: Key is null or empty.");
                return;
            }

            lock (_stateLock) _curSaveData.SimpleData.Remove(key);
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
            lock (_stateLock) RestoreSavable(savable);
        }

        /// <summary>
        /// Handled roughly before an ISavable unregisters itself (e.g. gets destroyed).
        /// Automatically forces a capture of its context back into the global save data cache beforehand so state isn't lost.
        /// </summary>
        /// <param name="savable">The object preparing to detach.</param>
        private void HandleUnregistration(ISavable savable)
        {
            lock (_stateLock) CaptureSavable(savable);
        }

        /// <summary>
        /// Tears down references to the registry. Highly recommended to explicitly call this to prevent lingering objects.
        /// </summary>
        public void Dispose()
        {
            _registry.OnSavableRegistered -= HandleRegistration;
            _registry.OnSavableUnregistered -= HandleUnregistration;

            Application.quitting -= HandleApplicationQuitting;
            Application.focusChanged -= HandleFocusChanged;

            AutoSaveTicker.Unsubscribe(this);
        }

        #endregion

        #region Helper

        /// <summary>
        /// Handles the application's quitting event by performing a best-effort save flush.
        /// </summary>
        private void HandleApplicationQuitting()
        {
            if (!_settings.AutoSaveOnQuit) return;

            ProcessAutoSave();
        }

        /// <summary>
        /// On mobile, flushes pending saves when focus is lost (app going to background may be OS-killed).
        /// Skipped on desktop where focus changes are transient (alt-tab, click outside).
        /// </summary>
        /// <param name="focused">True on focus gain; false on focus loss.</param>
        private void HandleFocusChanged(bool focused)
        {
            if (focused) return;
            if (!Application.isMobilePlatform) return;
            if (!_settings.AutoSaveOnQuit) return;

            ProcessAutoSave();
        }

        /// <summary>
        /// Drains in-flight async saves (bounded by AutoSaveOnQuitTimeout), then forces a final SaveImmediate.
        /// Shared between quit and mobile-focus-loss handlers.
        /// </summary>
        /// <remarks>
        /// Handle callbacks may fire after this returns (during shutdown/pause) — harmless since data
        /// is already persisted. For guaranteed save-before-quit, call <see cref="WaitForPendingOperationsAsync"/> proactively.
        /// </remarks>
        private void ProcessAutoSave()
        {
            int timeout = _settings.AutoSaveOnQuitTimeout;

            Task[] tasks;
            lock (_stateLock)
            {
                tasks = _saveStates.Values.Select(s => s.RunningTask).Where(t => t != null).ToArray();
            }

            if (tasks.Length > 0)
            {
                try
                {
                    Task.WaitAll(tasks, timeout);
                }
                catch
                {
                    // per-task errors already routed to each handle
                }
            }

            try
            {
                SaveImmediate();
            }
            catch (InvalidOperationException)
            {
                DebugLog.Warning("AutoSaveOnQuit has been skipped (no current profile, or async drain timed out).");
            }
            catch (Exception e)
            {
                DebugLog.Error($"AutoSaveOnQuit has failed: {e.Message}");
            }
        }

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

        /// <summary>
        /// Countdown timer called each frame by <see cref="AutoSaveTicker"/>.
        /// Uses unscaled time, so it continues even when the game is paused (Time.timeScale == 0).
        /// Does nothing if auto-save is disabled or no profile is active.
        /// </summary>
        /// <param name="deltaTime">Unscaled frame time delta.</param>
        internal void AutoSaveTick(float deltaTime)
        {
            if (!_settings.EnableAutoSave) return;
            if (string.IsNullOrEmpty(_curProfileId)) return;

            _autoSaveCountdown -= deltaTime;
            if (_autoSaveCountdown > 0) return;

            _ = SaveAsync();
            ResetAutoSaveCountdown();
        }

        private void ResetAutoSaveCountdown()
        {
            _autoSaveCountdown = _settings.AutoSaveTime;
        }

        /// <summary>
        /// Invoked at the end of a successful save. Resets the auto-save countdown when the saved profile
        /// matches the current implicit profile, then notifies external subscribers via <see cref="OnSaveCompleted"/>.
        /// Both actions are marshaled to the captured SynchronizationContext.
        /// </summary>
        /// <param name="profileId">The profile that was just persisted to disk.</param>
        private void NotifySaveCompleted(string profileId)
        {
            bool matchesCurrent = profileId == _curProfileId;
            Action<string> handler = OnSaveCompleted;

            if (_capturedContext != null && _capturedContext != SynchronizationContext.Current)
            {
                _capturedContext.Post(_ =>
                {
                    if (matchesCurrent) ResetAutoSaveCountdown();
                    handler?.Invoke(profileId);
                }, null);
            }
            else
            {
                if (matchesCurrent) ResetAutoSaveCountdown();
                handler?.Invoke(profileId);
            }
        }

        /// <summary>
        /// Tracks per-profile save state for concurrency control. Each profile being actively saved (or with pending save requests)
        /// has one instance of this class. Access must be guarded by GameSaver._stateLock.
        /// </summary>
        private class ProfileSaveState
        {
            /// <summary>
            /// True iff any save (async or immediate) is in flight for this profile.
            /// </summary>
            public bool IsRunning => RunningTask != null || IsSaveImmediate;

            /// <summary>
            /// Task from ProcessSaveAsync pipeline. Used to await all in-flight saves.
            /// Null when idle or started via SaveImmediate (sync).
            /// </summary>
            public Task RunningTask;

            /// <summary>
            /// True if the in-flight save was started by SaveImmediate (synchronous).
            /// SaveAsync calls arriving while this is true are rejected (cannot coalesce into a sync run).
            /// </summary>
            public bool IsSaveImmediate;

            /// <summary>
            /// True if at least one additional SaveAsync call arrived while IsRunning was true.
            /// The trailing save will run after the current pipeline completes.
            /// </summary>
            public bool IsDirty;

            /// <summary>
            /// Last-wins metadata supplied by coalesced SaveAsync calls. Applied by the trailing save.
            /// </summary>
            public ISaveMeta PendingMetadata;

            /// <summary>
            /// Handles of coalesced SaveAsync calls. All complete together when the trailing save finishes.
            /// </summary>
            public readonly List<GameSaverOperationInternal> PendingHandles = new();
        }

        #endregion
    }
}
