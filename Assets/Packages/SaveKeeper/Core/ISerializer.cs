namespace ThanhDV.SaveKeeper.Core
{
    /// <summary>
    /// Defines methods for serializing and deserializing objects.
    /// </summary>
    public interface ISerializer
    {
        /// <summary>
        /// Serializes the specified object to a string representation.
        /// </summary>
        /// <typeparam name="T">The type of the object to serialize.</typeparam>
        /// <param name="obj">The object to serialize.</param>
        /// <returns>A string representing the serialized object.</returns>
        string Serialize<T>(T obj);

        /// <summary>
        /// Deserializes the specified string data back into an object of type T.
        /// </summary>
        /// <typeparam name="T">The type of the object to deserialize.</typeparam>
        /// <param name="data">The string containing the serialized data.</param>
        /// <returns>The deserialized object of type T.</returns>
        T Deserialize<T>(string data);
    }
}
