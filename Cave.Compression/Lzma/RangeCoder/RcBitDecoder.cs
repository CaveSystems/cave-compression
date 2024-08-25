namespace Cave.Compression.Lzma.RangeCoder;

struct RcBitDecoder
{
    #region Private Fields

    const int kNumMoveBits = 5;
    uint Prob;

    #endregion Private Fields

    #region Public Fields

    public const uint kBitModelTotal = 1 << kNumBitModelTotalBits;
    public const int kNumBitModelTotalBits = 11;

    #endregion Public Fields

    #region Public Methods

    public uint Decode(RcDecoder rangeDecoder)
    {
        var newBound = (uint)(rangeDecoder.Range >> kNumBitModelTotalBits) * (uint)Prob;
        if (rangeDecoder.Code < newBound)
        {
            rangeDecoder.Range = newBound;
            Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
            if (rangeDecoder.Range < RcDecoder.kTopValue)
            {
                rangeDecoder.Code = (rangeDecoder.Code << 8) | (byte)rangeDecoder.Stream.ReadByte();
                rangeDecoder.Range <<= 8;
            }
            return 0;
        }
        else
        {
            rangeDecoder.Range -= newBound;
            rangeDecoder.Code -= newBound;
            Prob -= (Prob) >> kNumMoveBits;
            if (rangeDecoder.Range < RcDecoder.kTopValue)
            {
                rangeDecoder.Code = (rangeDecoder.Code << 8) | (byte)rangeDecoder.Stream.ReadByte();
                rangeDecoder.Range <<= 8;
            }
            return 1;
        }
    }

    public void Init() => Prob = kBitModelTotal >> 1;

    public void UpdateModel(int numMoveBits, uint symbol)
    {
        if (symbol == 0)
            Prob += (kBitModelTotal - Prob) >> numMoveBits;
        else
            Prob -= (Prob) >> numMoveBits;
    }

    #endregion Public Methods
}
