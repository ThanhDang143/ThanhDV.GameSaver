using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ThanhDV.SaveKeeper.Common;

namespace ThanhDV.SaveKeeper.Core
{
    public class SaveKeeper : ISaveKeeper
    {
        private readonly ISaveRegistry _registry;
        private readonly IStorageProvider _storageProvider;
        private readonly ISerializer _serializer;
        private readonly IEncryptionProvider _encryptionProvider;
        private readonly SaveSettings _settings;

        private SaveData _curSaveData = new();

        /// <summary>Highest observed UTC time — used to detect backward clock jumps.</summary>
        private DateTime _lastObservedTime = DateTime.MinValue;

        /// <summary>Minimum backward clock jump (seconds) that triggers a skew warning.</summary>
        private const double CLOCK_SKEW_THRESHOLD_SECONDS = 1.0;

        /// <summary>
        /// True when SetSimple/DeleteSimple changed in-memory state since the last save/load.
        /// LoadAsync rejects when dirty to prevent data loss — pass <c>discardUnsavedChanges: true</c> to override.
        /// </summary>
        private bool _isSimpleDataDirty; public bool IsSimpleDataDirty => _isSimpleDataDirty;

        /// <summary>Profile active for implicit saves, or null when none. Read under <see cref="_stateLock"/>.</summary>
        private string _curProfileId; public string CurrentProfileId { get { lock (_stateLock) return _curProfileId; } }

        /// <summary>
        /// SynchronizationContext captured at construction — marshals <see cref="OnSaveCompleted"/> back to
        /// the original thread (typically Unity main thread) so callback code can safely touch Unity APIs.
        /// </summary>
        private readonly SynchronizationContext _capturedContext;

        /// <summary>
        /// Raised after every successful save (async or immediate) with the saved profile ID.
        /// Always fires on the thread where this SaveKeeper was constructed.
        /// </summary>
        public event Action<string> OnSaveCompleted;

        private string MetaName => _settings.FileName + _settings.MetaExtension;
        private string SaveName => _settings.FileName + _settings.SaveExtension;

        /// <summary>
        /// Per-profile pipeline state for concurrency control. One entry per profile with an in-flight
        /// save/load or coalesced trailing requests. Access guarded by <see cref="_stateLock"/>.
        /// </summary>
        private readonly Dictionary<string, ProfilePipelineState> _profileStates = new();

        /// <summary>
        /// Guards <see cref="_profileStates"/>, <see cref="_curSaveData"/> (both inner dictionaries), and
        /// <see cref="_curProfileId"/>. Held only for short, await-free critical sections.
        /// </summary>
        private readonly object _stateLock = new();

        /// <summary>
        /// Constructs a SaveKeeper. Subscribes to registry events so newly registered savables auto-restore
        /// from the current state and unregistering ones get their state captured.
        /// </summary>
        public SaveKeeper(ISaveRegistry registry, IStorageProvider storageProvider, ISerializer serializer, IEncryptionProvider encryptionProvider, SaveSettings settings)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _encryptionProvider = encryptionProvider ?? throw new ArgumentNullException(nameof(encryptionProvider));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _capturedContext = SynchronizationContext.Current;

            _registry.OnSavableRegistered += OnSavableRegistered;
            _registry.OnSavableUnregistered += OnSavableUnregistered;
        }

        #region Profile Management

        /// <summary>
        /// Asynchronously fetches metadata for all existing profiles, sorted most-recent first.
        /// Useful for building save-slot selection menus.
        /// </summary>
        /// <typeparam name="T">Concrete metadata type implementing <see cref="ISaveMeta"/>.</typeparam>
        public SaveKeeperOperationHandle<List<T>> GetAllMetadataAsync<T>() where T : class, ISaveMeta
        {
            SaveKeeperOperationInternal<List<T>> internalOp = new();

            _ = ProcessLoadAllMetadataAsync(internalOp);

            return new SaveKeeperOperationHandle<List<T>>(internalOp);
        }

        /// <summary>Returns the most recently saved profile ID, or null if no profiles exist.</summary>
        public string GetMostRecentProfileId()
        {
            return _storageProvider.GetMostRecentProfileId(SaveName);
        }

        /// <summary>Returns every profile ID that currently has saved data on disk.</summary>
        public IEnumerable<string> GetAllProfiles()
        {
            return _storageProvider.GetAllProfileIds();
        }

        /// <summary>Returns true if a save file exists on disk for the given profile (false for null/empty).</summary>
        public bool ProfileExists(string profileId)
        {
            return !string.IsNullOrEmpty(profileId) && _storageProvider.Exists(profileId, SaveName);
        }

        /// <summary>
        /// Creates and activates a new save profile with an initial save; in-memory state from a prior load/save is cleared first.
        /// </summary>
        public void CreateProfile(string profileId, ISaveMeta metadata = null, bool overwrite = false)
        {
            if (string.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId));

            if (_storageProvider.Exists(profileId, SaveName))
            {
                if (!overwrite)
                {
                    throw new InvalidOperationException($"Profile '{profileId}' already exists. Pass overwrite: true to replace it.");
                }
                DeleteProfile(profileId);   // also throws if pipeline busy for this profile
            }

            lock (_stateLock)
            {
                _curSaveData = new();
                _curProfileId = profileId;
                ClearSimpleDataDirtyFlag();
            }

            SaveImmediate(profileId, metadata);
        }

        /// <summary>
        /// Deletes a profile and all its save data. If the profile is currently active, in-memory state is cleared too.
        /// </summary>
        /// <exception cref="InvalidOperationException">A save or load is in flight for this profile — wait for it to drain.</exception>
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

            SKLogger.Success($"Successfully deleted profile: {profileId}");
        }

        #endregion

        #region Save/Load

        /// <summary>
        /// Asynchronously saves the current state. Coalesces with any in-flight save for the same profile.
        /// </summary>
        /// <param name="profileId">Target profile; null uses <see cref="CurrentProfileId"/>.</param>
        /// <param name="metadata">UI display metadata; null uses <see cref="DefaultSaveMeta"/>.</param>
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
        /// Synchronously captures and saves. Blocks the calling thread — use sparingly to avoid frame drops.
        /// </summary>
        /// <param name="profileId">Target profile; null uses <see cref="CurrentProfileId"/>.</param>
        /// <param name="metadata">UI display metadata; null uses <see cref="DefaultSaveMeta"/>.</param>
        /// <exception cref="InvalidOperationException">No valid profile ID, or another save is in flight for this profile.</exception>
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

                SKLogger.Success("Immediate save operation successful!");
            }
            catch (InvalidOperationException)
            {
                // Expected rejection (e.g. no valid profile, concurrent save). Caller sees it via the thrown
                // exception — don't surface as Error to avoid polluting crash reporters with caller misuse.
                throw;
            }
            catch (Exception e)
            {
                SKLogger.Error($"Immediate save failed: {e.Message}");
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

        /// <summary>Restores a profile's backup file, replacing a broken primary save.</summary>
        /// <exception cref="ArgumentNullException">profileId is null or empty.</exception>
        public void RestoreBackup(string profileId)
        {
            if (string.IsNullOrEmpty(profileId)) throw new ArgumentNullException(nameof(profileId), "ProfileId cannot be null or empty.");

            _storageProvider.RestoreBackup(profileId, SaveName);
        }

        /// <summary>
        /// Loads a profile and restores state into every registered <see cref="ISavable"/>.
        /// </summary>
        /// <param name="discardUnsavedChanges">
        /// Default false. Operation fails if <see cref="IsSimpleDataDirty"/> is true (prevents data loss);
        /// pass true to discard pending changes and load anyway.
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

        /// <summary>Loads from the backup file instead of the main file — use when the primary save is corrupted.</summary>
        /// <param name="discardUnsavedChanges">See <see cref="LoadAsync"/>.</param>
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

        /// <summary>Resolves the most recently saved profile and loads it. Fails if none exists.</summary>
        /// <param name="discardUnsavedChanges">See <see cref="LoadAsync"/>.</param>
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
        /// Runs the leading save pass plus a trailing loop that drains coalesced SaveAsync calls that
        /// arrived while the leading pipeline was running.
        /// </summary>
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
                    SKLogger.Warning($"Save trailing loop running iteration #{iterations} for profile '{targetProfile}' — possible reentrancy.");
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
        /// One save pipeline pass: merge captured state, serialize, optionally encrypt, then persist to disk.
        /// </summary>
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
        /// Load pipeline: claim the load slot, read+decrypt+deserialize off-thread, then progressively restore
        /// each registered savable. Rejects if a save/load is already in flight for this profile.
        /// </summary>
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

                SKLogger.Success($"Successfully loaded {(isBackup ? "Backup" : "Primary")} data for Profile: {profileId}");

                internalOp.Complete();
            }
            catch (InvalidOperationException e)
            {
                // Expected rejection (e.g. no profile id, load/save already in flight). Caller observes it via
                // the operation handle — don't surface as Error to avoid polluting crash reporters.
                internalOp.Complete(e);
            }
            catch (Exception e)
            {
                SKLogger.Error($"Failed to load data: {e.Message}");

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
        /// Loads metadata for every profile, sorted most-recent first. Uses the .meta sidecar for fast reads;
        /// falls back to rebuilding from the .sav source of truth when the sidecar is missing/stale/corrupted.
        /// </summary>
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
                SKLogger.Error($"Failed to load metadata: {e.Message}");
                internalOp.Complete(null, e);
            }
        }

        /// <summary>
        /// Awaits every in-flight async save (looping to catch new SaveAsync calls). Use before scene
        /// transitions or before SaveImmediate. Errors are routed to individual handles, not rethrown.
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
        /// Stores a primitive or small struct in memory (Easy-Save-style). Call <see cref="SaveAsync"/>
        /// or <see cref="SaveImmediate"/> afterwards to persist.
        /// </summary>
        public void SetSimple<T>(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
            {
                SKLogger.Warning("Cannot set data: Key is null or empty.");
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
        /// Reads a value by key. Returns <paramref name="defaultValue"/> if the key is missing or parsing fails.
        /// </summary>
        public T GetSimple<T>(string key, T defaultValue = default)
        {
            if (string.IsNullOrEmpty(key))
            {
                SKLogger.Warning("Cannot get data: Key is null or empty.");
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
                SKLogger.Warning($"Failed to parse data for key '{key}': {e.Message}. Returning default value.");
                return defaultValue;
            }
        }

        /// <summary>True if the given key currently has a value in the SimpleData store.</summary>
        public bool HasSimpleKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                SKLogger.Warning("Cannot check key: Key is null or empty.");
                return false;
            }

            lock (_stateLock) return _curSaveData.SimpleData.ContainsKey(key);
        }

        /// <summary>Removes a key (and its value) from the SimpleData store.</summary>
        public void DeleteSimple(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                SKLogger.Warning("Cannot delete data: Key is null or empty.");
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
        /// Fires when a savable registers. Auto-restores its state from <see cref="_curSaveData"/> if a
        /// matching key exists — so late-arriving objects pick up the loaded profile.
        /// </summary>
        private void OnSavableRegistered(ISavable savable)
        {
            ISaveData saveData;
            bool found;
            lock (_stateLock) found = _curSaveData.ObjectData.TryGetValue(savable.SaveKey, out saveData);

            if (found) savable.RestoreData(saveData);
        }

        /// <summary>
        /// Fires when a savable is about to unregister (typically OnDestroy). Captures its state into
        /// <see cref="_curSaveData"/> so it survives the next save.
        /// </summary>
        private void OnSavableUnregistered(ISavable savable)
        {
            ISaveData saveData = savable.CaptureData();
            if (saveData == null) return;

            lock (_stateLock) _curSaveData.ObjectData[savable.SaveKey] = saveData;
        }

        /// <summary>Unsubscribes from registry events. Call to prevent lingering references after teardown.</summary>
        public void Dispose()
        {
            _registry.OnSavableRegistered -= OnSavableRegistered;
            _registry.OnSavableUnregistered -= OnSavableUnregistered;
        }

        #endregion

        #region Helper

        /// <summary>
        /// Restores a savable from a snapshot. Skips invocation if no entry exists for the savable's key —
        /// the object keeps its default state, so user RestoreData code doesn't need null checks.
        /// </summary>
        private void RestoreSavable(ISavable savable, SaveData sourceSaveData)
        {
            if (!sourceSaveData.ObjectData.TryGetValue(savable.SaveKey, out ISaveData saveData)) return;
            savable.RestoreData(saveData);
        }

        /// <summary>
        /// Calls <see cref="ISavable.CaptureData"/> on every registered savable and collects non-null results.
        /// Runs OFF-lock because it executes user code; callers merge the result under <see cref="_stateLock"/>.
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
        /// Fires <see cref="OnSaveCompleted"/> after a successful save, marshalled to the captured
        /// <see cref="_capturedContext"/> so subscribers run on the original (Unity main) thread.
        /// </summary>
        private void NotifySaveCompleted(string profileId)
        {
            Action<string> handler = OnSaveCompleted;

            if (handler == null) return;

            if (_capturedContext != null && _capturedContext != SynchronizationContext.Current)
            {
                _capturedContext.Post(_ => { handler?.Invoke(profileId); }, null);
            }
            else
            {
                handler?.Invoke(profileId);
            }
        }

        /// <summary>Marks unsaved changes (called after SimpleData mutations).</summary>
        private void MarkSimpleDataDirtyFlag()
        {
            lock (_stateLock) _isSimpleDataDirty = true;
        }

        /// <summary>Clears the dirty flag (after a successful save or load).</summary>
        private void ClearSimpleDataDirtyFlag()
        {
            lock (_stateLock) _isSimpleDataDirty = false;
        }

        /// <summary>
        /// Returns true if a load may proceed; otherwise completes the handle with an error
        /// when <see cref="IsSimpleDataDirty"/> and <paramref name="discardUnsavedChanges"/> is false.
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
        /// Reads the save (or backup), decrypts/deserializes, and returns the embedded <see cref="ISaveMeta"/>.
        /// Returns null when the file is missing, corrupted, or the meta isn't castable to <typeparamref name="T"/>.
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
                SKLogger.Warning($"{label} save for profile '{profile}' could not be read for metadata rebuild ({e.Message}).");
                return null;
            }
        }

        /// <summary>
        /// Resolves metadata for one profile. Fast-path: read the .meta sidecar if its mtime is fresh.
        /// Slow-path: rebuild from .sav (then .sav.bak), then self-heal the sidecar.
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
                    SKLogger.Warning($"Metadata of profile '{profile}' is corrupted ({e.Message}). Try rebuilding from save.");
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

                    if (isFromBackup) SKLogger.Success($"Rebuilt metadata from Backup save of profile '{profile}'.");
                }
                catch (Exception healEx)
                {
                    SKLogger.Warning($"Failed to write self-healed metadata for profile '{profile}': {healEx.Message}");
                }
            }
            else
            {
                SKLogger.Error($"Both Primary and Backup save of profile '{profile}' are corrupted. Skipping this Slot.");
            }

            return metadata;
        }

        /// <summary>Returns current UTC time and warns if the clock jumped backward beyond the threshold.</summary>
        /// <param name="context">Label included in the warning for debugging.</param>
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
                SKLogger.Warning($"Clock moved backward ({context}) by {delta.TotalSeconds:0.0}s (from {prev:O} to {now:O}). Save ordering may be inconsistent.");
            }

            return now;
        }

        /// <summary>Bumps the clock baseline from an external timestamp (e.g. loaded save metadata).</summary>
        private void ObserveExternalTimestamp(DateTime time)
        {
            if (time == default) return;

            lock (_stateLock)
            {
                if (time > _lastObservedTime) _lastObservedTime = time;
            }
        }

        /// <summary>
        /// Per-profile concurrency-control state. One instance per profile with an in-flight save/load or
        /// coalesced trailing requests. Access guarded by <see cref="SaveKeeper._stateLock"/>.
        /// </summary>
        private class ProfilePipelineState
        {
            /// <summary>
            /// Current in-flight kind. None = idle. SaveAsync coalesces only when AsyncSave;
            /// ImmediateSave/Load reject incoming saves.
            /// </summary>
            public PipelineActivity Activity;

            /// <summary>Task from the ProcessSaveAsync pipeline (set only when Activity == AsyncSave).</summary>
            public Task RunningTask;

            /// <summary>True if a SaveAsync arrived while another was running — trailing save will drain it.</summary>
            public bool IsDirty;

            /// <summary>Last-wins metadata from coalesced calls, applied by the trailing save.</summary>
            public ISaveMeta PendingMetadata;

            /// <summary>
            /// Last-wins captured object state from coalesced calls (captured off-lock on the main thread).
            /// Trailing save merges this instead of re-capturing.
            /// </summary>
            public Dictionary<string, ISaveData> PendingCaptured;

            /// <summary>Handles of coalesced SaveAsync calls; all complete together when the trailing save finishes.</summary>
            public readonly List<SaveKeeperOperationInternal> PendingHandles = new();
        }

        /// <summary>The in-flight operation kind for a profile. Save and Load are mutually exclusive per profile.</summary>
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
