namespace ThanhDV.GameSaver.Core
{
    public interface ISavable
    {
        void SaveData(SaveData data);
        void LoadData(SaveData data);
    }
}
