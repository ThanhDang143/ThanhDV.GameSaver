namespace ThanhDV.GameSaver.Core
{
    public interface ISavable
    {
        void SaveData(ISaveData data);
        void LoadData(ISaveData data);
    }
}
