namespace ThanhDV.SaveKeeper.Core
{
    public interface ISavable
    {
        /// <summary>
        /// Gets the unique identifier used to store and retrieve this object's data.
        /// </summary>
        string SaveKey { get; }

        /// <summary>
        /// Captures the current state of the object.
        /// </summary>
        /// <returns>An object implementing ISaveData that contains the captured state.</returns>
        ISaveData CaptureData();

        /// <summary>
        /// Restores the state of the object from the provided save data.
        /// </summary>
        /// <param name="data">The saved data to restore from.</param>
        void RestoreData(ISaveData data);
    }
}
