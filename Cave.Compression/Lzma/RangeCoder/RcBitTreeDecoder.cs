namespace Cave.Compression.Lzma.RangeCoder;

readonly struct RcBitTreeDecoder
{
    #region Private Fields

    readonly RcBitDecoder[] models;
    readonly int numBitLevels;

    #endregion Private Fields

    #region Public Constructors

    public RcBitTreeDecoder(int numBitLevels)
    {
        this.numBitLevels = numBitLevels;
        models = new RcBitDecoder[1 << numBitLevels];
    }

    #endregion Public Constructors

    #region Public Methods

    public static uint ReverseDecode(RcBitDecoder[] models, uint startIndex, RcDecoder rangeDecoder, int numBitLevels)
    {
        uint m = 1;
        uint symbol = 0;
        for (var bitIndex = 0; bitIndex < numBitLevels; bitIndex++)
        {
            var bit = models[startIndex + m].Decode(rangeDecoder);
            m <<= 1;
            m += bit;
            symbol |= bit << bitIndex;
        }
        return symbol;
    }

    public uint Decode(RcDecoder rangeDecoder)
    {
        uint m = 1;
        for (var bitIndex = numBitLevels; bitIndex > 0; bitIndex--)
            m = (m << 1) + models[m].Decode(rangeDecoder);
        return m - ((uint)1 << numBitLevels);
    }

    public void Init()
    {
        for (uint i = 1; i < (1 << numBitLevels); i++)
            models[i].Init();
    }

    public uint ReverseDecode(RcDecoder rangeDecoder)
    {
        uint m = 1;
        uint symbol = 0;
        for (var bitIndex = 0; bitIndex < numBitLevels; bitIndex++)
        {
            var bit = models[m].Decode(rangeDecoder);
            m <<= 1;
            m += bit;
            symbol |= bit << bitIndex;
        }
        return symbol;
    }

    #endregion Public Methods
}
