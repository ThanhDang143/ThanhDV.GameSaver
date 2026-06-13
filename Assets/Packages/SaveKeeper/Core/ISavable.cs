namespace ThanhDV.SaveKeeper.Core
{
    public interface ISavable
    {
        /// <summary>
        /// Gets the unique identifier used to store and retrieve this object's data.
        /// </summary>
        string SaveKey { get; }

        /// <summary>
        /// Captures the current state of the object into an immutable snapshot.
        /// </summary>
        /// <remarks>
        /// CONTRACT: the returned object is read on a background thread during async serialization
        /// (<c>SaveAsync</c>), so it must be a self-contained snapshot that you do NOT mutate after returning.
        /// Return a fresh object — e.g. <c>return new PlayerData { Hp = _hp };</c> — rather than a live reference
        /// that gameplay keeps changing. Returning a live object whose fields or collections are mutated while the
        /// save is in flight can corrupt the written data or throw "Collection was modified" mid-serialization.
        /// </remarks>
        /// <returns>An immutable <see cref="ISaveData"/> snapshot of the object's current state.</returns>
        ISaveData CaptureData();

        /// <summary>
        /// Restores the state of the object from the provided save data.
        /// </summary>
        /// <param name="data">The saved data to restore from.</param>
        void RestoreData(ISaveData data);
    }
}
