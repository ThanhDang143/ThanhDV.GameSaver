using System.Collections.Generic;
using ThanhDV.GameSaver.Core;
using UnityEngine;

public class Tester1 : MonoBehaviour, ISavable
{
    [Space]
    [SerializeField] private int intData;
    [SerializeField] private float floatData;
    [SerializeField] private List<string> dictionaryDataKey;
    [SerializeField] private List<Vector3> dictionaryDataValue;

    [Space]
    [SerializeField] private string profile = "PROFILE A";

    public string ModuleKey => typeof(Tester1Data).Name;

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
            GameSaver.Instance.DeleteData(profile);
        }

        if (Input.GetKeyDown(KeyCode.N))
        {
            GameSaver.Instance.NewGame(profile);
        }
    }

    [ContextMenu("Random Value")]
    private void RandomValue()
    {
        intData = Random.Range(-100, 100);
        floatData = Random.Range(-100f, 100f);

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
        public Dictionary<string, Vector3> DictionaryData { get; set; }

        public Tester1Data()
        {
            IntData = 0;
            FloatData = 0f;
            DictionaryData = new();
        }
    }
}
