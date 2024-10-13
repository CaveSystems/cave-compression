namespace Cave.Compression.Lzma;

/// <summary>Lzma coder properties</summary>
public class LzmaCoderProperties
{
    #region Public Properties

    /// <summary>Specifies size of dictionary.</summary>
    /// <remarks>Default = 1 &lt;&lt; 22, if set to 0 use default.</remarks>
    public uint DictionarySize { get; init; }

    /// <summary>Specifies mode with end marker. (Use this with streaming, can skipped on fixed container sizes.)</summary>
    /// <remarks>Default = false</remarks>
    public bool EndMarker { get; init; }

    /// <summary>Specifies number of literal context bits for LZMA (0 &lt;= x &lt;= 8).</summary>
    /// <remarks>Default = 3</remarks>
    public int LiteralContextBits { get; init; } = 3;

    /// <summary>Specifies number of literal position bits for LZMA (0 &lt;= x &lt;= 4).</summary>
    /// <remarks>Default = 0</remarks>
    public int LiteralPosStateBits { get; init; }

    /// <summary>Specifies match finder. LZMA: "BT2" or "BT4".</summary>
    /// <remarks>Default = BT4</remarks>
    public LzmaMatchFinderType MatchFinder { get; init; } = LzmaMatchFinderType.BT4;

    /// <summary>Specifies number of fast bytes for LZ*. (5 &lt;= x &lt;= 273)</summary>
    /// <remarks>Default = 32</remarks>
    public int NumFastBytes { get; init; } = 0x20;

    /// <summary>Specifies number of postion state bits for LZMA (0 &lt;= x &lt;= 4).</summary>
    /// <remarks>Default = 2</remarks>
    public int PosStateBits { get; init; } = 2;

    #endregion Public Properties
}
