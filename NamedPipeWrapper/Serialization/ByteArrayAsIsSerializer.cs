namespace NamedPipeWrapper.Serialization
{
    /// <summary>
    /// Serializer that pseudo-serializes byte arrays by passing them as-is.
    /// </summary>
    public sealed class ByteArrayAsIsSerializer : ICustomSerializer<byte[]>
    {

        /// <inheritdoc />
        public byte[] Serialize(byte[] obj)
        {
            return obj;
        }

        /// <inheritdoc />
        public byte[] Deserialize(byte[] data)
        {
            return data;
        }
    }
}