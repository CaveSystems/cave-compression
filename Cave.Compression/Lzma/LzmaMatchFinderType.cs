namespace Cave.Compression.Lzma;

/// <summary>Provides the available match finders</summary>
public enum LzmaMatchFinderType
{
    /// <summary>BT2 (better for very small packets of data)</summary>
    BT2,

    /// <summary>BT4</summary>
    BT4,
};
