using System.IO;
using UnityEditor;
using UnityEngine;

namespace ThanhDV.SaveKeeper.Editor
{
    public static class OpenDefaultDataFolder
    {
        [MenuItem("Tools/ThanhDV/SaveKeeper/Open Default Data Folder")]
        private static void OpenSaveFolder()
        {
            // Default save location used by LocalStorageProvider when constructed via SKSingleton.
            // Projects passing a custom basePath should add their own menu item.
            string path = Application.persistentDataPath;

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            EditorUtility.RevealInFinder(path);
        }
    }
}
