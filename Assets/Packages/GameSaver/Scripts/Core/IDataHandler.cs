using System.Collections.Generic;
using System.Threading.Tasks;

namespace ThanhDV.GameSaver.Core
{
    public interface IDataHandler
    {
        Task<T> Read<T>(string profileId, bool allowRestoreFromBackup = true) where T : class;
        Task<ISaveData> ReadModule(string profileId, string moduleKey, bool allowRestoreFromBackup = true);
        Task<Dictionary<string, SaveData>> ReadAllProfile();

        Task Write(SaveData data, string profileId);
        Task WriteModule(ISaveData data, string moduleKey, string profileId);

        void Delete(string profileId);

        string GetMostRecentProfile();
    }
}
