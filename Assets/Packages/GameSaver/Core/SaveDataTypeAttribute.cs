using System;

namespace ThanhDV.GameSaver.Core
{
    /// <summary>
    /// Assigns a stable alias to a type for serialization, protecting saves from breaking when the type is renamed or moved.
    /// The alias is stored in the save file's $type field instead of the full type name.
    /// Apply to ISaveData or ISaveMeta types when you want rename safety.
    /// Without this attribute, Type.FullName is used as the alias.
    /// </summary>
    /// <remarks>
    /// Aliases must be unique per project and cannot be changed after shipping — doing so breaks existing saves.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class SaveDataTypeAttribute : Attribute
    {
        /// <summary>
        /// Stable string identifier written into the serialized $type field for this type.
        /// </summary>
        public string Alias { get; }

        /// <param name="alias">
        /// A stable, unique identifier for this type. Once shipped, treat it as immutable —
        /// changing it will prevent old saves from loading.
        /// </param>
        /// <exception cref="ArgumentException">Thrown when alias is null, empty, or whitespace.</exception>
        public SaveDataTypeAttribute(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                throw new ArgumentException("Alias cannot be null or whitespace.", nameof(alias));
            }

            Alias = alias;
        }
    }
}
