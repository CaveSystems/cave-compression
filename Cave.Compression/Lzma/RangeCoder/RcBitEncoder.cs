namespace Cave.Compression.Lzma.RangeCoder;

struct RcBitEncoder
{
    #region Private Fields

    const int KNumMoveBits = 5;
    const int KNumMoveReducingBits = 2;
    private static readonly uint[] ProbPrices = new uint[KBitModelTotal >> KNumMoveReducingBits];
    uint prob;

    #endregion Private Fields

    #region Public Fields

    public const uint KBitModelTotal = 1 << KNumBitModelTotalBits;
    public const int KNumBitModelTotalBits = 11;
    public const int KNumBitPriceShiftBits = 6;

    #endregion Public Fields

    #region Public Constructors

    static RcBitEncoder()
    {
        const int KNumBits = KNumBitModelTotalBits - KNumMoveReducingBits;
        for (var i = KNumBits - 1; i >= 0; i--)
        {
            var start = (uint)1 << (KNumBits - i - 1);
            var end = (uint)1 << (KNumBits - i);
            for (var j = start; j < end; j++)
                ProbPrices[j] = ((uint)i << KNumBitPriceShiftBits) +
                    (((end - j) << KNumBitPriceShiftBits) >> (KNumBits - i - 1));
        }
    }

    #endregion Public Constructors

    #region Public Methods

    public void Encode(RcEncoder encoder, uint symbol)
    {
        // encoder.EncodeBit(Prob, kNumBitModelTotalBits, symbol); UpdateModel(symbol);
        var newBound = (encoder.Range >> KNumBitModelTotalBits) * prob;
        if (symbol == 0)
        {
            encoder.Range = newBound;
            prob += (KBitModelTotal - prob) >> KNumMoveBits;
        }
        else
        {
            encoder.Low += newBound;
            encoder.Range -= newBound;
            prob -= (prob) >> KNumMoveBits;
        }
        if (encoder.Range < RcEncoder.KTopValue)
        {
            encoder.Range <<= 8;
            encoder.ShiftLow();
        }
    }

    public uint GetPrice(uint symbol) => ProbPrices[(((prob - symbol) ^ (-(int)symbol)) & (KBitModelTotal - 1)) >> KNumMoveReducingBits];

    public uint GetPrice0() => ProbPrices[prob >> KNumMoveReducingBits];

    public uint GetPrice1() => ProbPrices[(KBitModelTotal - prob) >> KNumMoveReducingBits];

    public void Init() => prob = KBitModelTotal >> 1;

    public void UpdateModel(uint symbol)
    {
        if (symbol == 0)
            prob += (KBitModelTotal - prob) >> KNumMoveBits;
        else
            prob -= (prob) >> KNumMoveBits;
    }

    #endregion Public Methods
}
