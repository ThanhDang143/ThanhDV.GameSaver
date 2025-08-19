using System.Collections.Generic;
using ThanhDV.GameSaver.Saver;
using UnityEngine;

public class Tester : MonoBehaviour, ISavable
{
    [SerializeField] private int intData;
    [SerializeField] private float floatData;
    [SerializeField] private List<string> dictionaryDataKey;
    [SerializeField] private List<Vector3> dictionaryDataValue;

    public void LoadData(GameData data)
    {
        intData = data.IntData;
        floatData = data.FloatData;

        dictionaryDataKey = new();
        dictionaryDataValue = new();
        foreach (var pair in data.DictionaryData)
        {
            dictionaryDataKey.Add(pair.Key);
            dictionaryDataValue.Add(pair.Value);
        }
    }

    public void SaveData(ref GameData data)
    {
        data.IntData = intData;
        data.FloatData = floatData;

        data.DictionaryData = new();
        int dictCount = dictionaryDataKey.Count > dictionaryDataValue.Count ? dictionaryDataValue.Count : dictionaryDataKey.Count;
        for (int i = 0; i < dictCount; i++)
        {
            data.DictionaryData.Add(dictionaryDataKey[i], dictionaryDataValue[i]);
        }
    }
}
