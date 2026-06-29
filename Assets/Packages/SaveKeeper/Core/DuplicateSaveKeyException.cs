using System;

namespace ThanhDV.SaveKeeper.Core
{
    public class DuplicateSaveKeyException : InvalidOperationException
    {
        /// <summary>The conflicting SaveKey that triggered the exception.</summary>
        public string SaveKey { get; }

        /// <summary>
        /// Runtime type of the savable already registered under <see cref="SaveKey"/>.
        /// Null only if the existing entry was unexpectedly collected (should not happen in normal flow).
        /// </summary>
        public Type ExistingType { get; }

        /// <summary>Runtime type of the savable that attempted to register and triggered the conflict.</summary>
        public Type IncomingType { get; }

        /// <param name="saveKey">The SaveKey shared by both savables.</param>
        /// <param name="existingType">The type currently registered under the key.</param>
        /// <param name="incomingType">The type that attempted to register.</param>
        public DuplicateSaveKeyException(string saveKey, Type existingType, Type incomingType) : base(BuildMessage(saveKey, existingType, incomingType))
        {
            SaveKey = saveKey;
            ExistingType = existingType;
            IncomingType = incomingType;
        }

        private static string BuildMessage(string saveKey, Type existingType, Type incomingType)
        {
            string existing = existingType?.FullName ?? "<unknown>";
            string incoming = incomingType?.FullName ?? "<unknown>";

            return $"SaveKey '{saveKey}' is already registered by '{existing}'. " +
                   $"Cannot register '{incoming}'. SaveKey must be unique across the project." +
                   $"Use it for service-level savables (PlayerStats, Inventory, ...), not for runtime-spawned instances. See README for details.";
        }
    }
}
