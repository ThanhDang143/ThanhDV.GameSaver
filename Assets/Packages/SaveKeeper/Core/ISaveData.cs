namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Marker interface for storable game-state snapshots (player position, inventory, settings, etc.).
    /// </summary>
    /// <remarks>
    /// Implementations should be plain data and own their fields — instances are serialized on a background thread
    /// after <see cref="ISavable.CaptureData"/> returns and must not change post-capture.
    /// </remarks>
    public interface ISaveData
    {
    }
}