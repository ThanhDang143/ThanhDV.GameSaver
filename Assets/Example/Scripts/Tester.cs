using System.Collections.Generic;
using ThanhDV.GameSaver.Core;
using UnityEngine;

public class Tester : MonoBehaviour, ISavable
{
    [Space]
    [SerializeField] private int intData;
    [SerializeField] private float floatData;
    [SerializeField] private List<string> dictionaryDataKey;
    [SerializeField] private List<Vector3> dictionaryDataValue;

    [Space]
    [SerializeField] private string profileToDelete = "PROFILE A";

    private void OnEnable()
    {
        SaveRegistry.Register(this);
    }

    private void OnDisable()
    {
        SaveRegistry.Unregister(this);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            GameSaver.Instance.SetProfileID("PROFILE A");
            GameSaver.Instance.SaveGame();
        }

        if (Input.GetKeyDown(KeyCode.B))
        {
            GameSaver.Instance.SetProfileID("PROFILE B");
            GameSaver.Instance.SaveGame();
        }

        if (Input.GetKeyDown(KeyCode.C))
        {
            GameSaver.Instance.SetProfileID("PROFILE C");
            GameSaver.Instance.SaveGame();
        }

        if (Input.GetKeyDown(KeyCode.D))
        {
            GameSaver.Instance.DeleteData(profileToDelete);
        }

        if (Input.GetKeyDown(KeyCode.N))
        {
            GameSaver.Instance.SetProfileID(profileToDelete);
            GameSaver.Instance.NewGame();
        }
    }

    public void LoadData(SaveData data)
    {
        TesterData testerData = data.TryGetData<TesterData>();

        intData = testerData.IntData;
        floatData = testerData.FloatData;

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
        TesterData testerData = data.TryGetData<TesterData>();

        testerData.IntData = intData;
        testerData.FloatData = floatData;

        testerData.DictionaryData = new();
        int dictCount = dictionaryDataKey.Count > dictionaryDataValue.Count ? dictionaryDataValue.Count : dictionaryDataKey.Count;
        for (int i = 0; i < dictCount; i++)
        {
            testerData.DictionaryData.Add(dictionaryDataKey[i], dictionaryDataValue[i]);
        }
    }

    public class TesterData : ISaveData
    {
        public int IntData { get; set; }
        public float FloatData { get; set; }
        public Dictionary<string, Vector3> DictionaryData { get; set; }

        public TesterData()
        {
            IntData = 0;
            FloatData = 0f;
            DictionaryData = new();
        }
    }
}
