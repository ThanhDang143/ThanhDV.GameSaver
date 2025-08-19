using System.Threading.Tasks;

namespace ThanhDV.GameSaver.Saver
{
    public interface ISavable
    {
        void SaveData(ref GameData data);
        void LoadData(GameData data);
    }
}
