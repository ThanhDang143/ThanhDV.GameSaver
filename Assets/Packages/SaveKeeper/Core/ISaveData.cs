namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// A marker interface used to indicate that a class or struct represents storable data.
    /// Any class containing game state (e.g., health, gold, position) must implement this interface.
    /// </summary>
    /// <remarks>
    /// Implementations should be plain data: own their fields and avoid sharing mutable references with live
    /// gameplay objects. An instance produced by <see cref="ISavable.CaptureData"/> is serialized on a background
    /// thread and must not change after capture — see <see cref="ISavable.CaptureData"/> for the full snapshot contract.
    /// </remarks>
    public interface ISaveData
    {
    }
}