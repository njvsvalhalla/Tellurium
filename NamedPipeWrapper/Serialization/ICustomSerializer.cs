namespace NamedPipeWrapper.Serialization
{
    using System.Runtime.Serialization;

    /// <summary>
    /// An object that will serialize and deserialize the data passing through the pipe.
    /// </summary>
    /// <typeparam name="T">Reference type to serialize or deserialize</typeparam>
    public interface ICustomSerializer<T> where T : class
    {
        /// <summary>
        /// Serializes an object as byte arrays.
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <returns>Serialized bytes</returns>
        /// <exception cref="SerializationException">A problem has happened in the serialization process.</exception>
        byte[] Serialize(T obj);

        /// <summary>
        /// Deserializes a byte array as an object.
        /// </summary>
        /// <param name="data">Bytes to deserialize</param>
        /// <returns>Deserialized object</returns>
        /// <exception cref="SerializationException">A problem has happened in the deserialization process.</exception>
        T Deserialize(byte[] data);
    }
}