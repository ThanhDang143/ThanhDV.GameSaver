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

    public void SaveData(GameData data)
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
