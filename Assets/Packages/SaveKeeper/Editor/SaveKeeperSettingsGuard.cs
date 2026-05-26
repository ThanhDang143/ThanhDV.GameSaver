using System.IO;
using ThanhDV.SaveKeeper.Common;
using ThanhDV.SaveKeeper.Core;
using UnityEditor;

namespace ThanhDV.SaveKeeper.Editor
{
    /// <summary>
    /// Editor startup guard that bootstraps default settings on first install and regenerates
    /// <c>SaveKeeperSettings.cs</c> when the JSON source-of-truth exists but the generated
    /// factory file is missing.
    /// </summary>
    /// <remarks>
    /// Runs once per domain reload via <see cref="InitializeOnLoadAttribute"/>. Idempotent — if
    /// both JSON and .cs are present, returns immediately. Generation is deferred via
    /// <see cref="EditorApplication.delayCall"/> to avoid AssetDatabase calls inside the static
    /// constructor (which Unity discourages and can deadlock on cold starts).
    /// <para>
    /// Three scenarios are handled:
    /// <list type="bullet">
    /// <item><b>Both missing (first-time install)</b> — bootstrap with library defaults so
    /// <c>SaveKeeperSettings.Create()</c> is callable immediately after import without user
    /// having to open the settings window.</item>
    /// <item><b>JSON exists, .cs missing</b> — recovery for the case where a teammate pulls
    /// the repo without the generated .cs, or the file is deleted / lost to a Library/ purge.</item>
    /// <item><b>.cs exists, JSON missing</b> — skipped. The .cs is authoritative at runtime,
    /// JSON is only consumed by the settings window. User can re-Apply via the window to
    /// recreate the JSON if they want to edit again.</item>
    /// </list>
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class SaveKeeperSettingsGuard
    {
        private const string GENERATED_CS_PATH = "Assets/Plugins/SaveKeeper/SaveKeeperSettings.cs";

        static SaveKeeperSettingsGuard()
        {
            EditorApplication.delayCall += TryRegenerate;
        }

        private static void TryRegenerate()
        {
            bool jsonExists = SaveSettingsIO.Exists;
            bool csExists = File.Exists(GENERATED_CS_PATH);

            // Fully configured — nothing to do.
            if (jsonExists && csExists) return;

            SaveSettings settings;
            if (!jsonExists && !csExists)
            {
                // First-time install — bootstrap with library defaults.
                DebugLog.Success(
                    "SaveKeeper: First-time setup — generating default settings. " +
                    "Edit values via Tools → ThanhDV → SaveKeeper → Settings.");
                settings = new SaveSettings();
                SaveSettingsIO.Save(settings);
            }
            else if (!csExists)
            {
                // JSON exists but generated factory is missing — recovery path.
                DebugLog.Warning(
                    "SaveKeeperSettings.cs is missing but SaveKeeperSettings.json exists. " +
                    "Regenerating from JSON. Make sure to commit the regenerated file to source control.");
                settings = SaveSettingsIO.Load();
            }
            else
            {
                // .cs exists but JSON missing — user deleted JSON. The .cs is authoritative at
                // runtime, so the missing JSON doesn't cause compile errors. Skip to avoid
                // overwriting the user's possibly-customized .cs with defaults.
                return;
            }

            SaveKeeperSettingsGenerator.Generate(settings);
        }
    }
}
