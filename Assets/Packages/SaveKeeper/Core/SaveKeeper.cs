using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ThanhDV.SaveKeeper.Common;
using UnityEngine;

namespace ThanhDV.SaveKeeper.Core
{
    public class SaveKeeper : ISaveKeeper
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
        /// Highest observed UTC time, used to detect backward clock changes.
        /// </summary>
        private DateTime _lastObservedTime = DateTime.MinValue;

        /// <summary>
        /// Minimum backward clock jump that triggers a skew warning.
        /// </summary>
        private const double CLOCK_SKEW_THRESHOLD_SECONDS = 1.0;

        /// <summary>
        /// True when unsaved changes exist via SetSimple/DeleteSimple since the last save or load.
        /// Check before profile switches to warn "Save before loading?".
        /// LoadAsync throws when dirty to prevent data loss — use <c>discardUnsavedChanges: true</c> to skip.
        /// </summary>
        private bool _isSimpleDataDirty; public bool IsSimpleDataDirty => _isSimpleDataDirty;

        /// <summary>
        /// Caller's SynchronizationContext captured at construction. Used to marshal OnSaveCompleted
        /// invocations and the auto-save countdown reset back to the original thread (typically Unity main thread).
        /// </summary>
        private readonly SynchronizationContext _capturedContext;

        /// <summary>
        /// Raised after every successful save (async or immediate) with the profile ID that was saved.
        /// Always fires on the thread where this SaveKeeper was constructed — safe for Unity API calls.
        /// </summary>
        public event Action<string> OnSaveCompleted;

        private string MetaName => _settings.FileName + _settings.MetaExtension;
        private string SaveName => _settings.FileName + _settings.SaveExtension;

        /// <summary>
        /// Per-profile pipeline state for concurrency control. Each entry represents a profile that currently has
        /// a save or load pipeline running and/or coalesced trailing save requests waiting. Access must be guarded by _stateLock.
        /// </summary>
        private readonly Dictionary<string, ProfilePipelineState> _profileStates = new();

        /// <summary>
        /// Lock guarding _profileStates, _curSaveData (both inner dictionaries), and _curProfileId.
        /// Held only for short, await-free critical sections to avoid serializing IO.
        /// </summary>
        private readonly object _stateLock = new();

        /// <summary>
        /// Initializes a new instance of the SaveKeeper class.
        /// It manages serialization, storage, and persistence operations. Subscribing to registry changes enables 
        /// automatic restoration of newly registered objects and captures state for the ones being unregistered.
        /// </summary>
        public SaveKeeper(SaveRegistry registry, IStorageProvider storageProvider, ISerializer serializer, IEncryptionProvider encryptionProvider, SaveSettings settings)
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
        public SaveKeeperOperationHandle<List<T>> GetAllMetadataAsync<T>() where T : class, ISaveMeta
        {
            SaveKeeperOperationInternal<List<T>> internalOp = new();

            _ = ProcessLoadAllMetadataAsync(internalOp);

            return new SaveKeeperOperationHandle<List<T>>(internalOp);
        }

        /// <summary>
        /// Retrieves the ID of the most recently modified profile.
        /// </summary>
        /// <returns>The most recent profile ID, or null if no profiles exist.</returns>
        public string GetMostRecentProfileId()
        {
            return _storageProvider.GetMostRecentProfileId(SaveName);
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
        /// Thrown when a save or load operation is currently in flight for the same profile.
        /// Wait for the operation to complete (or for trailing saves to drain) before deleting.
        /// </exception>
        public void DeleteProfile(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) return;

            lock (_stateLock)
            {
                // Reject if any operation (save async/immediate or load) is in flight for this profile.
                if (_profileStates.TryGetValue(profileId, out ProfilePipelineState saveState) && saveState.Activity != PipelineActivity.None)
                {
                    throw new InvalidOperationException($"Cannot delete profile '{profileId}' while a save or load operation is in flight for the same profile.");
                }

                _storageProvider.DeleteProfile(profileId);

                if (_curProfileId == profileId)
                {
                    _curProfileId = null;
                    _curSaveData = new();
                    ClearSimpleDataDirtyFlag();
                }

                _profileStates.Remove(profileId);
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
        public SaveKeeperOperationHandle SaveAsync(string profileId = null, ISaveMeta metadata = null)
        {
            SaveKeeperOperationInternal internalOp = new();

            bool isImplicit = profileId == null;
            string targetProfile;
            lock (_stateLock) targetProfile = profileId ?? _curProfileId;

            if (string.IsNullOrEmpty(targetProfile))
            {
                InvalidOperationException error = new("Cannot save: No valid ProfileId was provided or found.");
                internalOp.Complete(error);
                return new SaveKeeperOperationHandle(internalOp);
            }

            Dictionary<string, ISaveData> captured;
            try
            {
                captured = CaptureAllSavables();
            }
            catch (Exception e)
            {
                internalOp.Complete(e);
                return new SaveKeeperOperationHandle(internalOp);
            }

            bool rejectedBySaveImmediate = false;
            bool rejectedByLoad = false;
            lock (_stateLock)
            {
                if (!_profileStates.TryGetValue(targetProfile, out ProfilePipelineState saveState))
                {
                    saveState = new();
                    _profileStates[targetProfile] = saveState;
                }

                switch (saveState.Activity)
                {
                    case PipelineActivity.Load:
                        // A load is in flight — cannot save over a profile being restored.
                        rejectedByLoad = true;
                        break;
                    case PipelineActivity.ImmediateSave:
                        // A SaveImmediate is in flight — cannot coalesce into a synchronous run.
                        rejectedBySaveImmediate = true;
                        break;
                    case PipelineActivity.AsyncSave:
                        // Normal coalesce: another SaveAsync is running.
                        saveState.IsDirty = true;
                        if (metadata != null) saveState.PendingMetadata = metadata;
                        saveState.PendingCaptured = captured;
                        saveState.PendingHandles.Add(internalOp);
                        break;
                    default:
                        // None — claim the slot and start the leading pipeline.
                        saveState.Activity = PipelineActivity.AsyncSave;
                        saveState.RunningTask = ProcessSaveAsync(targetProfile, isImplicit, metadata, captured, internalOp);
                        break;
                }
            }

            if (rejectedBySaveImmediate)
            {
                InvalidOperationException error = new($"Cannot SaveAsync for profile '{targetProfile}' while a SaveImmediate is in flight.");
                internalOp.Complete(error);
            }
            else if (rejectedByLoad)
            {
                InvalidOperationException error = new($"Cannot SaveAsync for profile '{targetProfile}' while a load operation is in flight.");
                internalOp.Complete(error);
            }

            return new SaveKeeperOperationHandle(internalOp);
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
            bool isImplicit = profileId == null;
            string targetProfile;
            lock (_stateLock) targetProfile = profileId ?? _curProfileId;

            if (string.IsNullOrEmpty(targetProfile)) throw new InvalidOperationException("Cannot save: No valid ProfileId was provided or found.");

            DateTime now = ReadClockAndCheckSkew($"save '{targetProfile}'");
            ISaveMeta resolvedMeta = metadata ?? new DefaultSaveMeta(targetProfile, now);
            lock (_stateLock)
            {
                if (_profileStates.TryGetValue(targetProfile, out ProfilePipelineState existingSaveState) && existingSaveState.Activity != PipelineActivity.None)
                {
                    throw new InvalidOperationException($"Cannot SaveImmediate for profile '{targetProfile}' while another save operation is in flight.");
                }

                ProfilePipelineState saveState = existingSaveState ?? new();
                saveState.Activity = PipelineActivity.ImmediateSave;
                _profileStates[targetProfile] = saveState;
            }

            try
            {
                Dictionary<string, ISaveData> captured = CaptureAllSavables();
                SaveData snapshot;

                lock (_stateLock)
                {
                    _curSaveData.Meta = resolvedMeta;
                    foreach (var kvp in captured) _curSaveData.ObjectData[kvp.Key] = kvp.Value;
                    snapshot = _curSaveData.Clone();
                    ClearSimpleDataDirtyFlag();
                }

                string data = _serializer.Serialize(snapshot);
                string finalData = _settings.UseEncryption ? _encryptionProvider.Encrypt(data) : data;

                _storageProvider.WriteImmediate(targetProfile, SaveName, finalData);

                string metaJson = _serializer.Serialize(resolvedMeta);
                _storageProvider.WriteImmediate(targetProfile, MetaName, metaJson);

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
                    if (_profileStates.TryGetValue(targetProfile, out ProfilePipelineState saveState))
                    {
                        saveState.Activity = PipelineActivity.None;

                        if (!saveState.IsDirty && saveState.PendingHandles.Count == 0)
                        {
                            _profileStates.Remove(targetProfile);
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
        /// Loads game data from the specified profile and restores it to registered ISavable instances.
        /// </summary>
        /// <param name="profileId">The ID of the profile to load.</param>
        /// <param name="discardUnsavedChanges">
        /// Defaults to false. If <see cref="IsSimpleDataDirty"/> is true, the operation completes with
        /// <see cref="InvalidOperationException"/> to prevent data loss. Pass true to discard unsaved
        /// changes and continue loading.
        /// </param>
        public SaveKeeperOperationHandle LoadAsync(string profileId, bool discardUnsavedChanges = false)
        {
            SaveKeeperOperationInternal internalOp = new();

            if (!TryAuthorizeLoad(profileId, discardUnsavedChanges, internalOp))
            {
                return new SaveKeeperOperationHandle(internalOp);
            }

            _ = ProcessLoadAsync(profileId, false, internalOp);
            return new SaveKeeperOperationHandle(internalOp);
        }

        /// <summary>
        /// Loads data from the backup file instead of the main file. Use this when the main save is corrupted.
        /// </summary>
        /// <param name="profileId">The ID of the profile whose backup should be loaded.</param>
        /// <param name="discardUnsavedChanges">
        /// Defaults to false. If <see cref="IsSimpleDataDirty"/> is true, the operation completes with
        /// <see cref="InvalidOperationException"/> to prevent data loss. Pass true to discard unsaved
        /// changes and continue loading.
        /// </param>
        public SaveKeeperOperationHandle LoadBackupAsync(string profileId, bool discardUnsavedChanges = false)
        {
            SaveKeeperOperationInternal internalOp = new();

            if (!TryAuthorizeLoad(profileId, discardUnsavedChanges, internalOp))
            {
                return new SaveKeeperOperationHandle(internalOp);
            }

            _ = ProcessLoadAsync(profileId, true, internalOp);
            return new SaveKeeperOperationHandle(internalOp);
        }

        /// <summary>
        /// Attempts to lazily resolve the most recently saved profile and loads it asynchronously. 
        /// </summary>
        /// <param name="discardUnsavedChanges">
        /// Defaults to false. If <see cref="IsSimpleDataDirty"/> is true, the operation completes with
        /// <see cref="InvalidOperationException"/> to prevent data loss. Pass true to discard unsaved
        /// changes and continue loading.
        /// </param>
        public SaveKeeperOperationHandle LoadMostRecentAsync(bool discardUnsavedChanges = false)
        {
            SaveKeeperOperationInternal internalOp = new();

            try
            {
                string mostRecentProfile = _storageProvider.GetMostRecentProfileId(SaveName);

                if (string.IsNullOrEmpty(mostRecentProfile))
                {
                    FileNotFoundException e = new("No previously saved Profile could be found.");
                    internalOp.Complete(e);
                    return new SaveKeeperOperationHandle(internalOp);
                }

                if (!TryAuthorizeLoad(mostRecentProfile, discardUnsavedChanges, internalOp))
                {
                    return new SaveKeeperOperationHandle(internalOp);
                }

                _ = ProcessLoadAsync(mostRecentProfile, false, internalOp);
            }
            catch (Exception e)
            {
                internalOp.Complete(e);
            }

            return new SaveKeeperOperationHandle(internalOp);
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
        private async Task ProcessSaveAsync(string targetProfile, bool isImplicit, ISaveMeta metadata, Dictionary<string, ISaveData> captured, SaveKeeperOperationInternal internalOp)
        {
            await ProcessSingleSaveAsync(targetProfile, isImplicit, metadata, captured, internalOp).ConfigureAwait(false);

            // Trailing loop: drain coalesced SaveAsync calls.
            int iterations = 0;
            while (true)
            {
                ISaveMeta trailingMetatdata;
                Dictionary<string, ISaveData> trailingCaptured;
                SaveKeeperOperationInternal[] pendingOps;

                lock (_stateLock)
                {
                    if (!_profileStates.TryGetValue(targetProfile, out ProfilePipelineState saveState)) return;

                    if (!saveState.IsDirty)
                    {
                        saveState.Activity = PipelineActivity.None;
                        saveState.RunningTask = null;
                        _profileStates.Remove(targetProfile);
                        return;
                    }

                    trailingMetatdata = saveState.PendingMetadata;
                    trailingCaptured = saveState.PendingCaptured;
                    pendingOps = saveState.PendingHandles.ToArray();
                    saveState.PendingHandles.Clear();
                    saveState.PendingMetadata = null;
                    saveState.PendingCaptured = null;
                    saveState.IsDirty = false;
                }

                iterations++;
                if (iterations > 2)
                {
                    DebugLog.Warning($"Save trailing loop running iteration #{iterations} for profile '{targetProfile}' — possible reentrancy.");
                }

                // Trailing pipeline is never "implicit" — it always targets the resolved profile explicitly.
                // The leading pipeline already updated _curProfileId if needed.
                SaveKeeperOperationInternal trailingOp = new();
                await ProcessSingleSaveAsync(targetProfile, false, trailingMetatdata, trailingCaptured, trailingOp).ConfigureAwait(false);

                for (int i = 0; i < pendingOps.Length; i++)
                {
                    SaveKeeperOperationInternal op = pendingOps[i];
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
        private async Task ProcessSingleSaveAsync(string targetProfile, bool isImplicit, ISaveMeta metadata, Dictionary<string, ISaveData> captured, SaveKeeperOperationInternal internalOp)
        {
            try
            {
                internalOp.PercentComplete = 0.1f;

                DateTime now = ReadClockAndCheckSkew($"save '{targetProfile}'");
                ISaveMeta resolvedMeta = metadata ?? new DefaultSaveMeta(targetProfile, now);

                // Snapshot state under lock; serialization on background threads avoids shared-state corruption.
                SaveData snapshot;
                lock (_stateLock)
                {
                    _curSaveData.Meta = resolvedMeta;
                    if (captured != null) foreach (var kvp in captured) _curSaveData.ObjectData[kvp.Key] = kvp.Value;
                    snapshot = _curSaveData.Clone();
                    ClearSimpleDataDirtyFlag();
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

                string metaJson = await Task.Run(() => _serializer.Serialize(resolvedMeta)).ConfigureAwait(false);
                await _storageProvider.WriteAsync(targetProfile, MetaName, metaJson).ConfigureAwait(false);

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
        private async Task ProcessLoadAsync(string profileId, bool isBackup, SaveKeeperOperationInternal internalOp)
        {
            bool claimedLoad = false;
            try
            {
                if (string.IsNullOrEmpty(profileId)) throw new InvalidOperationException("Cannot load: No valid ProfileId was provided or found.");

                lock (_stateLock)
                {
                    if (_profileStates.TryGetValue(profileId, out ProfilePipelineState state))
                    {
                        if (state.Activity == PipelineActivity.AsyncSave || state.Activity == PipelineActivity.ImmediateSave)
                        {
                            throw new InvalidOperationException($"Cannot load profile '{profileId}' while a save operation is in flight for the same profile.");
                        }

                        if (state.Activity == PipelineActivity.Load)
                        {
                            throw new InvalidOperationException($"Cannot load profile '{profileId}' while another load operation is in flight for the same profile.");
                        }

                        state.Activity = PipelineActivity.Load;
                    }
                    else
                    {
                        state = new() { Activity = PipelineActivity.Load };
                        _profileStates[profileId] = state;
                    }

                    claimedLoad = true;
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
                IReadOnlyList<ISavable> savableSnapshots;
                SaveData restoreSnapshot;
                lock (_stateLock)
                {
                    _curSaveData = loadedData ?? new();
                    _curProfileId = profileId;
                    ClearSimpleDataDirtyFlag();
                    savableSnapshots = _registry.Savables;
                    restoreSnapshot = _curSaveData.Clone();
                }

                ObserveExternalTimestamp(loadedData?.Meta?.LastTimeSaved ?? default);

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
            finally
            {
                // Release the load slot.
                // Guard with claimedLoad so a rejected attempt (save/load already in flight) does not clear the slot owned by the operation that actually holds it.
                if (claimedLoad)
                {
                    lock (_stateLock)
                    {
                        if (_profileStates.TryGetValue(profileId, out ProfilePipelineState state))
                        {
                            state.Activity = PipelineActivity.None;
                            if (!state.IsDirty && state.PendingHandles.Count <= 0)
                            {
                                _profileStates.Remove(profileId);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Asynchronously loads metadata for every available profile, sorted by most recent save time.
        /// Uses the meta sidecar cache for fast reads; falls back to rebuilding metadata from the save source of truth when the sidecar is missing, stale (post-crash), or corrupted.
        /// </summary>
        /// <typeparam name="T">The concrete metadata type, which must implement <see cref="ISaveMeta"/>.</typeparam>
        /// <param name="internalOp">Operation context to report status, completion, and progress scaling.</param>
        private async Task ProcessLoadAllMetadataAsync<T>(SaveKeeperOperationInternal<List<T>> internalOp) where T : class, ISaveMeta
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

                    try
                    {
                        if (!_storageProvider.Exists(profile, SaveName)) continue;

                        T metadata = await TryLoadMetaAsync<T>(profile).ConfigureAwait(false);
                        if (metadata != null)
                        {
                            metadatas.Add(metadata);
                            ObserveExternalTimestamp(metadata.LastTimeSaved);
                        }
                    }
                    finally
                    {
                        internalOp.PercentComplete = (float)(i + 1) / profileCount;
                    }
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
                    runningTasks = _profileStates.Values.Select(s => s.RunningTask).Where(t => t != null).ToArray();
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

            // Null is valid — bypass serializer, store as sentinel. GetSimple returns default(T).
            string serializedValue = value is null ? null : _serializer.Serialize(value);

            lock (_stateLock)
            {
                _curSaveData.SimpleData[key] = serializedValue;
                MarkSimpleDataDirtyFlag();
            }
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

            // Null = explicitly stored. Returns default(T), not the supplied defaultValue.
            if (serializedValue == null) return default;

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

            lock (_stateLock)
            {
                if (_curSaveData.SimpleData.Remove(key)) MarkSimpleDataDirtyFlag();
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
            ISaveData saveData;
            bool found;
            lock (_stateLock) found = _curSaveData.ObjectData.TryGetValue(savable.SaveKey, out saveData);

            if (found) savable.RestoreData(saveData);
        }

        /// <summary>
        /// Handled roughly before an ISavable unregisters itself (e.g. gets destroyed).
        /// Automatically forces a capture of its context back into the global save data cache beforehand so state isn't lost.
        /// </summary>
        /// <param name="savable">The object preparing to detach.</param>
        private void HandleUnregistration(ISavable savable)
        {
            ISaveData saveData = savable.CaptureData();
            if (saveData == null) return;

            lock (_stateLock) _curSaveData.ObjectData[savable.SaveKey] = saveData;
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
                tasks = _profileStates.Values.Select(s => s.RunningTask).Where(t => t != null).ToArray();
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
        /// Restores a savable from the provided save-data snapshot.
        /// Skips invocation entirely when no entry exists for the savable's key — the object keeps its
        /// default state, freeing user code from having to null-check inside RestoreData.
        /// </summary>
        /// <param name="savable">The savable instance to restore.</param>
        /// <param name="sourceSaveData">The save-data snapshot used for lookup.</param>
        private void RestoreSavable(ISavable savable, SaveData sourceSaveData)
        {
            if (!sourceSaveData.ObjectData.TryGetValue(savable.SaveKey, out ISaveData saveData)) return;
            savable.RestoreData(saveData);
        }

        /// <summary>
        /// Calls CaptureData() on every registered savable and collects non-null results into a new dictionary.
        /// Runs WITHOUT holding _stateLock — call this off-lock (it executes user CaptureData() code). 
        /// Callers merge the returned dictionary into _curSaveData.ObjectData under _stateLock.
        /// </summary>
        private Dictionary<string, ISaveData> CaptureAllSavables()
        {
            IReadOnlyList<ISavable> savables = _registry.Savables;
            Dictionary<string, ISaveData> captured = new(savables.Count);

            foreach (ISavable savable in savables)
            {
                ISaveData saveData = savable.CaptureData();
                if (saveData == null) continue;

                captured[savable.SaveKey] = saveData;
            }

            return captured;
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
            bool matchesCurrent;
            lock (_stateLock) matchesCurrent = profileId == _curProfileId;
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
        /// Marks unsaved changes. Called by SetSimple/DeleteSimple after mutating _curSaveData.SimpleData.
        /// </summary>
        private void MarkSimpleDataDirtyFlag()
        {
            lock (_stateLock) _isSimpleDataDirty = true;
        }

        /// <summary>
        /// Clears the dirty flag. Called after a successful save or load.
        /// </summary>
        private void ClearSimpleDataDirtyFlag()
        {
            lock (_stateLock) _isSimpleDataDirty = false;
        }

        /// <summary>
        /// Returns true if load can proceed. Returns false and completes the handle with an error
        /// if unsaved changes exist and discardUnsavedChanges is false.
        /// </summary>
        private bool TryAuthorizeLoad(string profileId, bool discardUnsavedChanges, SaveKeeperOperationInternal intenalOp)
        {
            if (_isSimpleDataDirty && !discardUnsavedChanges)
            {
                InvalidOperationException error = new($"Cannot load profile '{profileId}' — unsaved changes exist. Save first or pass discardUnsavedChanges: true.");

                intenalOp.Complete(error);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads save (or its backup), decrypts, deserializes, and extracts the embedded <see cref="ISaveMeta"/>. 
        /// Returns null when the file is missing, corrupted, or its meta is not castable to <typeparamref name="T"/>.
        /// </summary>
        private async Task<T> TryRebuildMetaAsync<T>(string profile, bool useBackup) where T : class, ISaveMeta
        {
            try
            {
                string rawData = useBackup ? await _storageProvider.ReadBackupAsync(profile, SaveName).ConfigureAwait(false) : await _storageProvider.ReadAsync(profile, SaveName).ConfigureAwait(false);

                SaveData saveData = await Task.Run(() =>
                {
                    string plain = _settings.UseEncryption ? _encryptionProvider.Decrypt(rawData) : rawData;
                    return _serializer.Deserialize<SaveData>(plain);
                }).ConfigureAwait(false);

                return saveData?.Meta as T;
            }
            catch (Exception e)
            {
                string label = useBackup ? "Backup" : "Primary";
                DebugLog.Warning($"{label} save for profile '{profile}' could not be read for metadata rebuild ({e.Message}).");
                return null;
            }
        }

        /// <summary>
        /// Resolves metadata for a single profile. Tries read meta when the sidecar is fresh per mtime; otherwise rebuilds from save and self-heals the sidecar.
        /// </summary>
        private async Task<T> TryLoadMetaAsync<T>(string profile) where T : class, ISaveMeta
        {
            DateTime? metaMtime = _storageProvider.GetLastWriteTimeUtc(profile, MetaName);
            DateTime? saveMtime = _storageProvider.GetLastWriteTimeUtc(profile, SaveName);
            bool isMetaValid = metaMtime.HasValue && saveMtime.HasValue && metaMtime.Value >= saveMtime.Value;

            if (isMetaValid)
            {
                try
                {
                    string metaJson = await _storageProvider.ReadAsync(profile, MetaName).ConfigureAwait(false);
                    return await Task.Run(() => _serializer.Deserialize<T>(metaJson)).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    DebugLog.Warning($"Metadata of profile '{profile}' is corrupted ({e.Message}). Try rebuilding from save.");
                }
            }

            T metadata = await TryRebuildMetaAsync<T>(profile, false).ConfigureAwait(false);
            bool isFromBackup = false;
            if (metadata == null)
            {
                metadata = await TryRebuildMetaAsync<T>(profile, true).ConfigureAwait(false);
                isFromBackup = true;
            }

            if (metadata != null)
            {
                try
                {
                    string metaJson = await Task.Run(() => _serializer.Serialize(metadata)).ConfigureAwait(false);
                    await _storageProvider.WriteAsync(profile, MetaName, metaJson).ConfigureAwait(false);

                    if (isFromBackup) DebugLog.Success($"Rebuilt metadata from Backup save of profile '{profile}'.");
                }
                catch (Exception healEx)
                {
                    DebugLog.Warning($"Failed to write self-healed metadata for profile '{profile}': {healEx.Message}");
                }
            }
            else
            {
                DebugLog.Error($"Both Primary and Backup save of profile '{profile}' are corrupted. Skipping this Slot.");
            }

            return metadata;
        }

        /// <summary>
        /// Returns the current UTC time and warns if the clock moved backward too far.
        /// </summary>
        /// <param name="context">Label included in the warning for debugging.</param>
        /// <returns>The current UTC time.</returns>
        private DateTime ReadClockAndCheckSkew(string context)
        {
            DateTime now = DateTime.UtcNow;
            DateTime prev = DateTime.MinValue;
            bool warn = false;
            TimeSpan delta;

            lock (_stateLock)
            {
                prev = _lastObservedTime;
                if (prev == DateTime.MinValue)
                {
                    warn = false;
                    delta = TimeSpan.Zero;
                }
                else
                {
                    delta = prev - now;
                    warn = delta.TotalSeconds > CLOCK_SKEW_THRESHOLD_SECONDS;
                }

                if (now > _lastObservedTime) _lastObservedTime = now;
            }

            if (warn)
            {
                DebugLog.Warning($"Clock moved backward ({context}) by {delta.TotalSeconds:0.0}s (from {prev:O} to {now:O}). Save ordering may be inconsistent.");
            }

            return now;
        }

        /// <summary>
        /// Observes a timestamp from an external source (loaded save metadata) to update the clock baseline.
        /// </summary>
        private void ObserveExternalTimestamp(DateTime time)
        {
            if (time == default) return;

            lock (_stateLock)
            {
                if (time > _lastObservedTime) _lastObservedTime = time;
            }
        }

        /// <summary>
        /// Tracks per-profile pipeline state for concurrency control. Each profile with an in-flight save or load
        /// (or pending coalesced save requests) has one instance of this class. Access must be guarded by SaveKeeper._stateLock.
        /// </summary>
        private class ProfilePipelineState
        {
            /// <summary>
            /// The operation currently in flight for this profile. None = idle.
            /// SaveAsync coalesces only when this is AsyncSave; ImmediateSave/Load reject incoming saves.
            /// </summary>
            public PipelineActivity Activity;

            /// <summary>
            /// Task from the ProcessSaveAsync pipeline. Set only when Activity == AsyncSave; used to await in-flight saves.
            /// </summary>
            public Task RunningTask;

            /// <summary>
            /// True if at least one additional SaveAsync arrived while an async save was running.
            /// The trailing save runs after the current pipeline completes.
            /// </summary>
            public bool IsDirty;

            /// <summary>
            /// Last-wins metadata supplied by coalesced SaveAsync calls. Applied by the trailing save.
            /// </summary>
            public ISaveMeta PendingMetadata;

            /// <summary>
            /// Last-wins captured object state from coalesced SaveAsync calls (captured at call time, off-lock,
            /// on the main thread). The trailing save merges this instead of re-capturing.
            /// </summary>
            public Dictionary<string, ISaveData> PendingCaptured;

            /// <summary>
            /// Handles of coalesced SaveAsync calls. All complete together when the trailing save finishes.
            /// </summary>
            public readonly List<SaveKeeperOperationInternal> PendingHandles = new();
        }

        /// <summary>
        /// The single in-flight operation kind for a profile. Save and Load are mutually exclusive per profile.
        /// </summary>
        private enum PipelineActivity
        {
            None,
            AsyncSave,
            ImmediateSave,
            Load
        }


        #endregion
    }
}
