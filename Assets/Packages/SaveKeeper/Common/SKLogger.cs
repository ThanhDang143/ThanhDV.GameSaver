using System;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace ThanhDV.SaveKeeper.Common
{
    public static class SKLogger
    {
        [Conditional("UNITY_EDITOR")]
        public static void Log(string message) => Debug.Log($"<color=white>[SaveKeeper] {message}</color>");

        [Conditional("UNITY_EDITOR")]
        public static void Success(string message) => Debug.Log($"<color=green>[SaveKeeper] {message}</color>");

        public static void Warning(string message) => Debug.LogWarning($"[SaveKeeper] {message}");

        public static void Error(string message) => Debug.LogError($"[SaveKeeper] {message}");

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
