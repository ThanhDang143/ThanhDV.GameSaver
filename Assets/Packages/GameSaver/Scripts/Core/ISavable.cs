using System.Threading.Tasks;

namespace ThanhDV.GameSaver.Core
{
    public interface ISavable
    {
        void SaveData(GameData data);
        void LoadData(GameData data);
    }
}
