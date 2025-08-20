using System.Collections.Generic;
using System.Threading.Tasks;

namespace ThanhDV.GameSaver.DataHandler
{
    public interface IDataHandler
    {
        Task<T> Read<T>(string profileId, bool allowRestoreFromBackup = true) where T : class;
        Task<Dictionary<string, T>> ReadAll<T>() where T : class;

        Task Write<T>(T data, string profileId) where T : class;

        void Delete(string profileId);

        string GetMostRecentProfile();
    }
}
