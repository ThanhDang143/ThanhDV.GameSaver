namespace ThanhDV.GameSaver.Core
{
    public interface ISavable
    {
        string ModuleKey { get; }
        void SaveData(SaveData data);
        void LoadData(SaveData data);
    }
}
