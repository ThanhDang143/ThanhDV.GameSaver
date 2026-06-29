namespace ThanhDV.SaveKeeper.Core
{
    public interface ISavable
    {
        /// <summary>Unique identifier used to store and retrieve this object's data.</summary>
        string SaveKey { get; }

        /// <summary>
        /// Captures the current state as an immutable snapshot.
        /// </summary>
        /// <remarks>
        /// CONTRACT: the returned object is read on a background thread during <c>SaveAsync</c> — it must be a
        /// self-contained snapshot that you do NOT mutate after returning. Return a fresh object (e.g.
        /// <c>return new PlayerData { Hp = _hp };</c>) rather than a live reference that gameplay keeps changing.
        /// Otherwise the write may corrupt data or throw "Collection was modified" mid-serialization.
        /// </remarks>
        /// <returns>An immutable <see cref="ISaveData"/> snapshot of the object's current state.</returns>
        ISaveData CaptureData();

        /// <summary>Restores state from a previously captured snapshot.</summary>
        /// <param name="data">The saved data to restore from.</param>
        void RestoreData(ISaveData data);
    }
}
