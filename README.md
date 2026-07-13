# SaveKeeper

A lightweight JSON save system for Unity.

## Installation

### Unity Package Manager

    https://github.com/ThanhDang143/ThanhDV.SaveKeeper.git?path=/Assets/Packages/SaveKeeper

### Scoped Registry

- Name: ThanhDV
- URL: https://upm.thanhdv.com
- Scope: thanhdv
- Package: thanhdv.savekeeper

## Public API

Namespace: ThanhDV.SaveKeeper.Core

### SaveKeeper
```
    public event Action<string> OnSaveCompleted;

    public bool IsSimpleDataDirty { get; }
    public string CurrentProfileId { get; }

    public SaveKeeper(
        ISaveRegistry registry,
        IStorageProvider storageProvider,
        ISerializer serializer,
        IEncryptionProvider encryptionProvider,
        SaveSettings settings);

    public SaveKeeperOperationHandle<List<T>> GetAllMetadataAsync<T>()
        where T : class, ISaveMeta;

    public string GetMostRecentProfileId();
    public IEnumerable<string> GetAllProfiles();
    public void DeleteProfile(string profileId);

    public SaveKeeperOperationHandle SaveAsync(
        string profileId = null,
        ISaveMeta metadata = null);

    public void SaveImmediate(
        string profileId = null,
        ISaveMeta metadata = null);

    public void RestoreBackup(string profileId);

    public SaveKeeperOperationHandle LoadAsync(
        string profileId,
        bool discardUnsavedChanges = false);

    public SaveKeeperOperationHandle LoadBackupAsync(
        string profileId,
        bool discardUnsavedChanges = false);

    public SaveKeeperOperationHandle LoadMostRecentAsync(
        bool discardUnsavedChanges = false);

    public Task WaitForPendingOperationsAsync();

    public void SetSimple<T>(string key, T value);
    public T GetSimple<T>(string key, T defaultValue = default);
    public bool HasSimpleKey(string key);
    public void DeleteSimple(string key);

    public void Dispose();
```