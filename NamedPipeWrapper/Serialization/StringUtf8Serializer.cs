namespace NamedPipeWrapper.Serialization
{
    using System.Text;

    /// <inheritdoc />
    /// <summary>
    /// Serializer that serializes strings as UTF8.
    /// </summary>
    public sealed class StringUtf8Serializer : ICustomSerializer<string>
    {
        /// <summary>
        /// Returns a singleton instance of this class
        /// </summary>
        public static readonly StringUtf8Serializer Instance = new StringUtf8Serializer();

        /// <inheritdoc />
        public byte[] Serialize(string obj) => Encoding.UTF8.GetBytes(obj);

        /// <inheritdoc />
        public string Deserialize(byte[] data) => Encoding.UTF8.GetString(data);
    }
}