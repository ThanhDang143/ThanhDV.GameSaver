using System;

namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Pins a stable alias to an <see cref="ISaveData"/>/<see cref="ISaveMeta"/> type so saves survive renames or moves.
    /// The alias is written to the $type field instead of <c>Type.FullName</c>.
    /// </summary>
    /// <remarks>Aliases must be unique per project and immutable after shipping — changing one breaks existing saves.</remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class SaveDataAliasAttribute : Attribute
    {
        /// <summary>Stable identifier written to the serialized $type field for this type.</summary>
        public string Alias { get; }

        /// <param name="alias">
        /// A stable, unique identifier for this type. Once shipped, treat it as immutable —
        /// changing it will prevent old saves from loading.
        /// </param>
        /// <exception cref="ArgumentException">Thrown when alias is null, empty, or whitespace.</exception>
        public SaveDataAliasAttribute(string alias)
        {
            if (string.IsNullOrWhiteSpace(alias))
            {
                throw new ArgumentException("Alias cannot be null or whitespace.", nameof(alias));
            }

            Alias = alias;
        }
    }
}
