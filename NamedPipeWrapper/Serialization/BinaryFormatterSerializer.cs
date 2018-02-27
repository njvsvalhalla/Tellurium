namespace NamedPipeWrapper.Serialization
{
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;

    /// <inheritdoc />
    /// <summary>
    /// Serializer that uses an instance of System.Runtime.Serialization.Formatters.Binary.BinaryFormatter
    /// </summary>
    /// <typeparam name="T">Reference type to serialize or deserialize</typeparam>
    public class BinaryFormatterSerializer<T> : ICustomSerializer<T> where T : class
    {
        private readonly BinaryFormatter _binaryFormatter = new BinaryFormatter();

        /// <inheritdoc />
        public byte[] Serialize(T obj)
        {
            using (var memoryStream = new MemoryStream())
            {
                _binaryFormatter.Serialize(memoryStream, obj);
                return memoryStream.ToArray();
            }
        }

        /// <inheritdoc />
        public T Deserialize(byte[] data)
        {
            using (var memoryStream = new MemoryStream(data))
                return (T)_binaryFormatter.Deserialize(memoryStream);
        }
    }
}
