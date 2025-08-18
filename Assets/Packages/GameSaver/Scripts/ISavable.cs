namespace ThanhDV.GameSaver
{
    public interface ISavable
    {
        void SaveData(ref GameData data);
        void LoadData(GameData data);
    }
}
