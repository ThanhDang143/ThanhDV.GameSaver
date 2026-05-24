using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace ThanhDV.GameSaver.Common
{
    public static class DebugLog
    {
        [Conditional("UNITY_EDITOR")]
        public static void Log(string message) => Debug.Log($"<color=white>[GameSaver] {message}</color>");

        [Conditional("UNITY_EDITOR")]
        public static void Success(string message) => Debug.Log($"<color=green>[GameSaver] {message}</color>");

        public static void Warning(string message) => Debug.Log($"<color=yellow>[GameSaver] {message}</color>");

        public static void Error(string message) => Debug.Log($"<color=red>[GameSaver] {message}</color>");

        public static string SanitizePath(string fullPath)
        {
#if UNITY_EDITOR
            return fullPath;
#else
            return string.IsNullOrEmpty(fullPath) ? fullPath : System.IO.Path.GetFileName(fullPath);
#endif
        }
        
        public static string SanitizeException(Exception e)
        {
#if UNITY_EDITOR
            return e?.ToString() ?? "<null>";
#else
            return e?.Message ?? "<null>";
#endif
        }
    }
}
