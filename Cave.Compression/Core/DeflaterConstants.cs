using System;

#pragma warning disable SA1401 // Fields must be private

namespace Cave.Compression.Core;

/// <summary>
/// This class contains constants used for deflation.
/// </summary>
static class DeflaterConstants
{
    /// <summary>
    /// Set to true to enable debugging.
    /// </summary>
    public const bool Debugging = false;

    /// <summary>
    /// Written to Zip file to identify a stored block.
    /// </summary>
    public const int StoredBlock = 0;

    /// <summary>
    /// Identifies static tree in Zip file.
    /// </summary>
    public const int StaticTree = 1;

    /// <summary>
    /// Identifies dynamic tree in Zip file.
    /// </summary>
    public const int DynamicTree = 2;

    /// <summary>
    /// Header flag indicating a preset dictionary for deflation.
    /// </summary>
    public const int PresetDictionary = 0x20;

    /// <summary>
    /// Sets internal buffer sizes for Huffman encoding.
    /// </summary>
    public const int DefaultMemoryLevel = 8;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int MaxMatch = 258;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int MinMatch = 3;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int MaxWindowBits = 15;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int WindowSize = 1 << MaxWindowBits;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int WindowMask = WindowSize - 1;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int HashBits = DefaultMemoryLevel + 7;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int HashSize = 1 << HashBits;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int HashMask = HashSize - 1;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int HashShift = (HashBits + MinMatch - 1) / MinMatch;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int MinLookahead = MaxMatch + MinMatch + 1;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int MaxDist = WindowSize - MinLookahead;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int PendingBufferSize = 1 << (DefaultMemoryLevel + 8);

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int DeflateStored = 0;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int DeflateFast = 1;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public const int DeflateSlow = 2;

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public static int MaxBlockSize = Math.Min(65535, PendingBufferSize - 5);

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public static int[] GoodLength = { 0, 4, 4, 4, 4, 8, 8, 8, 32, 32 };

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public static int[] MaxLazy = { 0, 4, 5, 6, 4, 16, 16, 32, 128, 258 };

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public static int[] NiceLength = { 0, 8, 16, 32, 16, 32, 128, 128, 258, 258 };

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public static int[] MaxChain = { 0, 4, 8, 32, 16, 32, 128, 256, 1024, 4096 };

    /// <summary>
    /// Internal compression engine constant.
    /// </summary>
    public static int[] CompressionFunction = { 0, 1, 1, 1, 1, 2, 2, 2, 2, 2 };
}
#pragma warning restore SA1401 // Fields must be private
