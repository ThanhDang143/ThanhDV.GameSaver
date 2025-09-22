using System;

namespace ThanhDV.GameSaver.Core
{
    public interface ISavable
    {
        Type SaveType { get; }
        void SaveData(SaveData data);
        void LoadData(SaveData data);
    }
}
