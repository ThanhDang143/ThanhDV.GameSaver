using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ThanhDV.SaveKeeper.Core
{
    public interface ISaveKeeper : IDisposable
    {
        event Action<string> OnSaveCompleted;
        bool IsSimpleDataDirty { get; }

        // Profile / metadata
        SaveKeeperOperationHandle<List<T>> GetAllMetadataAsync<T>() where T : class, ISaveMeta;
        string GetMostRecentProfileId();
        IEnumerable<string> GetAllProfiles();
        void DeleteProfile(string profileId);

        // Save
        SaveKeeperOperationHandle SaveAsync(string profileId = null, ISaveMeta metadata = null);
        void SaveImmediate(string profileId = null, ISaveMeta metadata = null);

        // Load / recovery
        SaveKeeperOperationHandle LoadAsync(string profileId, bool discardUnsavedChanges = false);
        SaveKeeperOperationHandle LoadBackupAsync(string profileId, bool discardUnsavedChanges = false);
        SaveKeeperOperationHandle LoadMostRecentAsync(bool discardUnsavedChanges = false);
        void RestoreBackup(string profileId);

        // Lifecycle
        Task WaitForPendingOperationsAsync();

        // SimpleData (Easy-Save style)
        void SetSimple<T>(string key, T value);
        T GetSimple<T>(string key, T defaultValue = default);
        bool HasSimpleKey(string key);
        void DeleteSimple(string key);
    }
}