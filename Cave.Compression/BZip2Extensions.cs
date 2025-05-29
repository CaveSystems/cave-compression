using System.IO;

namespace Cave.Compression;

/// <summary>Provides extensions for gzip compression/decompression.</summary>
public static class BZip2Extensions
{
    #region Public Methods

    /// <summary>Uncompresses the <paramref name="data"/>.</summary>
    /// <param name="data">The compressed data.</param>
    /// <returns>Returns the uncompressed data.</returns>
    public static byte[] Bunzip2(this byte[] data)
    {
        using var ms = new MemoryStream();
        GZip.GZip.Decompress(new MemoryStream(data), ms, true);
        return ms.ToArray();
    }

    /// <summary>Compress the <paramref name="data"/>.</summary>
    /// <param name="data">The uncompressed data.</param>
    /// <param name="level">Block size acts as compression level (1 to 9) with 1 giving the lowest compression and 9 the highest.</param>
    /// <returns>Returns the compressed data.</returns>
    public static byte[] BZip2(this byte[] data, CompressionStrength level = CompressionStrength.Best)
    {
        using var ms = new MemoryStream();
        GZip.GZip.Compress(new MemoryStream(data), ms, true, level);
        return ms.ToArray();
    }

    #endregion Public Methods
}
