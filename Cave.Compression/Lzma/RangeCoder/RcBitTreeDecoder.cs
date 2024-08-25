namespace Cave.Compression.Lzma.RangeCoder;

struct RcBitTreeDecoder
{
    #region Private Fields

    RcBitDecoder[] Models;
    int NumBitLevels;

    #endregion Private Fields

    #region Public Constructors

    public RcBitTreeDecoder(int numBitLevels)
    {
        NumBitLevels = numBitLevels;
        Models = new RcBitDecoder[1 << numBitLevels];
    }

    #endregion Public Constructors

    #region Public Methods

    public static uint ReverseDecode(RcBitDecoder[] Models, uint startIndex,
        RcDecoder rangeDecoder, int NumBitLevels)
    {
        uint m = 1;
        uint symbol = 0;
        for (var bitIndex = 0; bitIndex < NumBitLevels; bitIndex++)
        {
            var bit = Models[startIndex + m].Decode(rangeDecoder);
            m <<= 1;
            m += bit;
            symbol |= bit << bitIndex;
        }
        return symbol;
    }

    public uint Decode(RcDecoder rangeDecoder)
    {
        uint m = 1;
        for (var bitIndex = NumBitLevels; bitIndex > 0; bitIndex--)
            m = (m << 1) + Models[m].Decode(rangeDecoder);
        return m - ((uint)1 << NumBitLevels);
    }

    public void Init()
    {
        for (uint i = 1; i < (1 << NumBitLevels); i++)
            Models[i].Init();
    }

    public uint ReverseDecode(RcDecoder rangeDecoder)
    {
        uint m = 1;
        uint symbol = 0;
        for (var bitIndex = 0; bitIndex < NumBitLevels; bitIndex++)
        {
            var bit = Models[m].Decode(rangeDecoder);
            m <<= 1;
            m += bit;
            symbol |= bit << bitIndex;
        }
        return symbol;
    }

    #endregion Public Methods
}
