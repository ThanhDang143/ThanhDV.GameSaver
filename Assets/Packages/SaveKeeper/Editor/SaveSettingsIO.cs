using System;
using System.IO;
using Newtonsoft.Json;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;

namespace ThanhDV.SaveKeeper.Editor
{
    /// <summary>
    /// Reads and writes the SaveKeeper settings JSON file in ProjectSettings/.
    /// Path is relative to the Unity project root.
    /// </summary>
    internal static class SaveSettingsIO
    {
        private const string FILE_PATH = "ProjectSettings/SaveKeeperSettings.json";

        /// <summary>
        /// Returns true if the settings JSON file exists on disk.
        /// </summary>
        public static bool Exists => File.Exists(FILE_PATH);

        /// <summary>
        /// Loads settings from disk. Returns a fresh defaults instance when the file is missing
        /// or fails to parse. Logs a warning on parse failure but never throws.
        /// </summary>
        public static SaveSettings Load()
        {
            if (!Exists) return new SaveSettings();

            try
            {
                string json = File.ReadAllText(FILE_PATH);
                SaveSettings parsed = JsonConvert.DeserializeObject<SaveSettings>(json);
                return parsed ?? new SaveSettings();
            }
            catch (System.Exception e)
            {
                DebugLog.Warning($"Failed to parse '{FILE_PATH}': {e.Message}. Falling back to defaults.");
                return new SaveSettings();
            }
        }

        /// <summary>
        /// Serializes the given settings to indented JSON and writes them to disk.
        /// </summary>
        public static void Save(SaveSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);

            string dir = Path.GetDirectoryName(FILE_PATH);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(FILE_PATH, json);
        }
    }
}
