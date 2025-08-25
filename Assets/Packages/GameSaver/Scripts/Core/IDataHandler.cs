using System.Collections.Generic;
using System.Threading.Tasks;

namespace ThanhDV.GameSaver.Core
{
    public interface IDataHandler
    {
        Task<T> Read<T>(string profileId, bool allowRestoreFromBackup = true) where T : class;
        Task<Dictionary<string, T>> ReadAll<T>() where T : class;

        Task Write(SaveData data, string profileId);

        void Delete(string profileId);

        string GetMostRecentProfile();
    }
}
