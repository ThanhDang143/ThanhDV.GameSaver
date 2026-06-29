namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>Converts objects to/from string for storage.</summary>
    public interface ISerializer
    {
        /// <summary>Serializes <paramref name="obj"/> to a string.</summary>
        string Serialize<T>(T obj);

        /// <summary>Deserializes <paramref name="data"/> back into an instance of <typeparamref name="T"/>.</summary>
        T Deserialize<T>(string data);
    }
}
