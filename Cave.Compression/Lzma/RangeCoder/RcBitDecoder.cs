namespace Cave.Compression.Lzma.RangeCoder;

struct RcBitDecoder
{
    #region Private Fields

    const int KNumMoveBits = 5;
    uint prob;

    #endregion Private Fields

    #region Public Fields

    public const uint KBitModelTotal = 1 << KNumBitModelTotalBits;
    public const int KNumBitModelTotalBits = 11;

    #endregion Public Fields

    #region Public Methods

    public uint Decode(RcDecoder rangeDecoder)
    {
        var newBound = (rangeDecoder.Range >> KNumBitModelTotalBits) * prob;
        if (rangeDecoder.Code < newBound)
        {
            rangeDecoder.Range = newBound;
            prob += (KBitModelTotal - prob) >> KNumMoveBits;
            if (rangeDecoder.Range < RcDecoder.KTopValue)
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
            prob -= (prob) >> KNumMoveBits;
            if (rangeDecoder.Range < RcDecoder.KTopValue)
            {
                rangeDecoder.Code = (rangeDecoder.Code << 8) | (byte)rangeDecoder.Stream.ReadByte();
                rangeDecoder.Range <<= 8;
            }
            return 1;
        }
    }

    public void Init() => prob = KBitModelTotal >> 1;

    public void UpdateModel(int numMoveBits, uint symbol)
    {
        if (symbol == 0)
            prob += (KBitModelTotal - prob) >> numMoveBits;
        else
            prob -= (prob) >> numMoveBits;
    }

    #endregion Public Methods
}
