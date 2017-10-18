using System;
using System.Text;

namespace NamedPipeWrapper.Serialization
{
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
        public byte[] Serialize(string obj)
        {
            return Encoding.UTF8.GetBytes(obj);
        }

        /// <inheritdoc />
        public string Deserialize(byte[] data)
        {
            return Encoding.UTF8.GetString(data);
        }
    }
}