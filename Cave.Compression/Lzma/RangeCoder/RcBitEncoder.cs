namespace Cave.Compression.Lzma.RangeCoder;

struct RcBitEncoder
{
    #region Private Fields

    const int kNumMoveBits = 5;
    const int kNumMoveReducingBits = 2;
    private static uint[] ProbPrices = new uint[kBitModelTotal >> kNumMoveReducingBits];
    uint Prob;

    #endregion Private Fields

    #region Public Fields

    public const uint kBitModelTotal = 1 << kNumBitModelTotalBits;
    public const int kNumBitModelTotalBits = 11;
    public const int kNumBitPriceShiftBits = 6;

    #endregion Public Fields

    #region Public Constructors

    static RcBitEncoder()
    {
        const int kNumBits = kNumBitModelTotalBits - kNumMoveReducingBits;
        for (var i = kNumBits - 1; i >= 0; i--)
        {
            var start = (uint)1 << (kNumBits - i - 1);
            var end = (uint)1 << (kNumBits - i);
            for (var j = start; j < end; j++)
                ProbPrices[j] = ((uint)i << kNumBitPriceShiftBits) +
                    (((end - j) << kNumBitPriceShiftBits) >> (kNumBits - i - 1));
        }
    }

    #endregion Public Constructors

    #region Public Methods

    public void Encode(RcEncoder encoder, uint symbol)
    {
        // encoder.EncodeBit(Prob, kNumBitModelTotalBits, symbol); UpdateModel(symbol);
        var newBound = (encoder.Range >> kNumBitModelTotalBits) * Prob;
        if (symbol == 0)
        {
            encoder.Range = newBound;
            Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
        }
        else
        {
            encoder.Low += newBound;
            encoder.Range -= newBound;
            Prob -= (Prob) >> kNumMoveBits;
        }
        if (encoder.Range < RcEncoder.kTopValue)
        {
            encoder.Range <<= 8;
            encoder.ShiftLow();
        }
    }

    public uint GetPrice(uint symbol) => ProbPrices[(((Prob - symbol) ^ (-(int)symbol)) & (kBitModelTotal - 1)) >> kNumMoveReducingBits];

    public uint GetPrice0() => ProbPrices[Prob >> kNumMoveReducingBits];

    public uint GetPrice1() => ProbPrices[(kBitModelTotal - Prob) >> kNumMoveReducingBits];

    public void Init() => Prob = kBitModelTotal >> 1;

    public void UpdateModel(uint symbol)
    {
        if (symbol == 0)
            Prob += (kBitModelTotal - Prob) >> kNumMoveBits;
        else
            Prob -= (Prob) >> kNumMoveBits;
    }

    #endregion Public Methods
}
