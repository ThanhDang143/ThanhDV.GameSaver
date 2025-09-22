using System;
using System.Collections.Generic;
using ThanhDV.GameSaver.Core;
using UnityEngine;
using Random = UnityEngine.Random;

public class Tester1 : ISavable, IDisposable
{
    private int intData;
    private float floatData;
    private Struct structData;
    private List<string> dictionaryDataKey;
    private List<Vector3> dictionaryDataValue;

    public Type SaveType => typeof(Tester1Data);

    public Tester1()
    {
        SaveRegistry.Register(this);
    }

    public void Dispose()
    {
        SaveRegistry.Unregister(this);
    }

    private void RandomValue()
    {
        intData = Random.Range(-100, 100);
        floatData = Random.Range(-100f, 100f);

        structData.i = Random.Range(-100, 100);
        structData.f = Random.Range(-100f, 100f);
        structData.v = Random.insideUnitSphere * floatData;

        dictionaryDataKey = new();
        dictionaryDataValue = new();
        for (int i = 0; i < 5; i++)
        {
            int ranKey = Random.Range(-999, -888);
            Vector3 ranValue = Random.insideUnitSphere * floatData;
            dictionaryDataKey.Add($"K{ranKey}");
            dictionaryDataValue.Add(ranValue);
        }
    }

    public void LoadData(SaveData data)
    {
        data.TryGetData(out Tester1Data testerData);

        intData = testerData.IntData;
        floatData = testerData.FloatData;
        structData = testerData.StructData;

        dictionaryDataKey = new();
        dictionaryDataValue = new();
        foreach (var pair in testerData.DictionaryData)
        {
            dictionaryDataKey.Add(pair.Key);
            dictionaryDataValue.Add(pair.Value);
        }
    }

    public void SaveData(SaveData data)
    {
        data.TryGetData(out Tester1Data testerData);

        testerData.IntData = intData;
        testerData.FloatData = floatData;
        testerData.StructData = structData;

        testerData.DictionaryData = new();
        int dictCount = dictionaryDataKey.Count > dictionaryDataValue.Count ? dictionaryDataValue.Count : dictionaryDataKey.Count;
        for (int i = 0; i < dictCount; i++)
        {
            testerData.DictionaryData.Add(dictionaryDataKey[i], dictionaryDataValue[i]);
        }
    }

    public class Tester1Data : ISaveData
    {
        public int IntData { get; set; }
        public float FloatData { get; set; }
        public Struct StructData { get; set; }
        public Dictionary<string, Vector3> DictionaryData { get; set; }

        public Tester1Data()
        {
            IntData = 0;
            FloatData = 0f;
            StructData = new();
            DictionaryData = new();
        }
    }
}

[Serializable]
public struct Struct
{
    public int i;
    public float f;
    public Vector3 v;
}
