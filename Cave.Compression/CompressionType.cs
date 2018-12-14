using System;

namespace Cave.Compression
{
    /// <summary>
    /// Compression type
    /// </summary>
    [Flags]
    public enum CompressionType
    {
        /// <summary>
        /// No compression, no encryption
        /// </summary>
        None = 0x00,

        /// <summary>
        /// Comression using deflate
        /// </summary>
        Deflate = 0x01,

        /// <summary>
        /// Comression using gzip
        /// </summary>
        GZip = 0x02,
    }
}
