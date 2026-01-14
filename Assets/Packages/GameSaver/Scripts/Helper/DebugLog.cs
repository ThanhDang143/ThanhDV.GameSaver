using UnityEngine;

namespace ThanhDV.GameSaver.Helper
{
    public static class DebugLog
    {
        private static void Log(string message, string color = "white") => Debug.Log($"<color={color}>[GameSaver] {message}</color>");
        public static void Warning(string message) => Log(message, "yellow");
        public static void Error(string message) => Log(message, "red");
        public static void Success(string message) => Log(message, "green");
    }
}
