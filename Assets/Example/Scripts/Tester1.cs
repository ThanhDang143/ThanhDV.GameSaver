using System;
using System.Collections.Generic;
using ThanhDV.GameSaver.Core;
using UnityEngine;
using Random = UnityEngine.Random;

public class Tester1 : ISavable, IDisposable
{
    #region Singleton
    private static Tester1 _instance;
    private static readonly object _lock = new object();

    public static Tester1 Instance
    {
        get
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new Tester1();

                    Debug.Log($"<color=yellow>{_instance.GetType().Name} instance is null!!! Auto create new instance!!!</color>");
                }
                return _instance;
            }
        }
    }

    public static bool IsExist => _instance != null;
    #endregion

    private int intData;
    private float floatData;
    private Struct structData;
    private List<string> dictionaryDataKey;
    private List<Vector3> dictionaryDataValue;

    public Type SaveType => typeof(Tester1Data);

    public static void Initialize()
    {
        if (IsExist) return;
        var tester1 = Instance;
        Debug.Log("Tester1 initialized!!!");
    }

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

        Debug.Log("Tester1 loaded!!!");
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

        Debug.Log("Tester1 saved!!!");
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
