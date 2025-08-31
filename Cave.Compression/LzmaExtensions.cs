using System.IO;
using Cave.Compression.Lzma;

namespace Cave.Compression;

/// <summary>Provides extensions for lzma compression/decompression.</summary>
public static class LzmaExtensions
{
    #region Public Methods

    /// <summary>Compress the <paramref name="data"/>.</summary>
    /// <param name="data">The uncompressed data.</param>
    /// <param name="level">Block size acts as compression level (1 to 9) with 1 giving the lowest compression and 9 the highest.</param>
    /// <returns>Returns the compressed data.</returns>
    public static byte[] Lzma(this byte[] data, CompressionStrength level = CompressionStrength.Best)
    {
        using var output = new MemoryStream();
        using var input = new MemoryStream(data);
        LzmaStandard.Compress(input, output);
        return output.ToArray();
    }

    /// <summary>Uncompresses the <paramref name="data"/>.</summary>
    /// <param name="data">The compressed data.</param>
    /// <returns>Returns the uncompressed data.</returns>
    public static byte[] Unlzma(this byte[] data)
    {
        using var output = new MemoryStream();
        using var input = new MemoryStream(data);
        LzmaStandard.Decompress(input, output);
        return output.ToArray();
    }

    #endregion Public Methods
}
