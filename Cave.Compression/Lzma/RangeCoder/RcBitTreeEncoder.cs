namespace Cave.Compression.Lzma.RangeCoder;

struct RcBitTreeEncoder
{
    #region Private Fields

    RcBitEncoder[] Models;
    int NumBitLevels;

    #endregion Private Fields

    #region Public Constructors

    public RcBitTreeEncoder(int numBitLevels)
    {
        NumBitLevels = numBitLevels;
        Models = new RcBitEncoder[1 << numBitLevels];
    }

    #endregion Public Constructors

    #region Public Methods

    public static void ReverseEncode(RcBitEncoder[] Models, uint startIndex,
        RcEncoder rangeEncoder, int NumBitLevels, uint symbol)
    {
        uint m = 1;
        for (var i = 0; i < NumBitLevels; i++)
        {
            var bit = symbol & 1;
            Models[startIndex + m].Encode(rangeEncoder, bit);
            m = (m << 1) | bit;
            symbol >>= 1;
        }
    }

    public static uint ReverseGetPrice(RcBitEncoder[] Models, uint startIndex,
        int NumBitLevels, uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var i = NumBitLevels; i > 0; i--)
        {
            var bit = symbol & 1;
            symbol >>= 1;
            price += Models[startIndex + m].GetPrice(bit);
            m = (m << 1) | bit;
        }
        return price;
    }

    public void Encode(RcEncoder rangeEncoder, uint symbol)
    {
        uint m = 1;
        for (var bitIndex = NumBitLevels; bitIndex > 0;)
        {
            bitIndex--;
            var bit = (symbol >> bitIndex) & 1;
            Models[m].Encode(rangeEncoder, bit);
            m = (m << 1) | bit;
        }
    }

    public uint GetPrice(uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var bitIndex = NumBitLevels; bitIndex > 0;)
        {
            bitIndex--;
            var bit = (symbol >> bitIndex) & 1;
            price += Models[m].GetPrice(bit);
            m = (m << 1) + bit;
        }
        return price;
    }

    public void Init()
    {
        for (uint i = 1; i < (1 << NumBitLevels); i++)
            Models[i].Init();
    }

    public void ReverseEncode(RcEncoder rangeEncoder, uint symbol)
    {
        uint m = 1;
        for (uint i = 0; i < NumBitLevels; i++)
        {
            var bit = symbol & 1;
            Models[m].Encode(rangeEncoder, bit);
            m = (m << 1) | bit;
            symbol >>= 1;
        }
    }

    public uint ReverseGetPrice(uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var i = NumBitLevels; i > 0; i--)
        {
            var bit = symbol & 1;
            symbol >>= 1;
            price += Models[m].GetPrice(bit);
            m = (m << 1) | bit;
        }
        return price;
    }

    #endregion Public Methods
}
