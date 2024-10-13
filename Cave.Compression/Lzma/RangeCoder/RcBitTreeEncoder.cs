namespace Cave.Compression.Lzma.RangeCoder;

readonly struct RcBitTreeEncoder
{
    #region Private Fields

    readonly RcBitEncoder[] models;
    readonly int numBitLevels;

    #endregion Private Fields

    #region Public Constructors

    public RcBitTreeEncoder(int numBitLevels)
    {
        this.numBitLevels = numBitLevels;
        models = new RcBitEncoder[1 << numBitLevels];
    }

    #endregion Public Constructors

    #region Public Methods

    public static void ReverseEncode(RcBitEncoder[] models, uint startIndex, RcEncoder rangeEncoder, int numBitLevels, uint symbol)
    {
        uint m = 1;
        for (var i = 0; i < numBitLevels; i++)
        {
            var bit = symbol & 1;
            models[startIndex + m].Encode(rangeEncoder, bit);
            m = (m << 1) | bit;
            symbol >>= 1;
        }
    }

    public static uint ReverseGetPrice(RcBitEncoder[] models, uint startIndex, int numBitLevels, uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var i = numBitLevels; i > 0; i--)
        {
            var bit = symbol & 1;
            symbol >>= 1;
            price += models[startIndex + m].GetPrice(bit);
            m = (m << 1) | bit;
        }
        return price;
    }

    public void Encode(RcEncoder rangeEncoder, uint symbol)
    {
        uint m = 1;
        for (var bitIndex = numBitLevels; bitIndex > 0;)
        {
            bitIndex--;
            var bit = (symbol >> bitIndex) & 1;
            models[m].Encode(rangeEncoder, bit);
            m = (m << 1) | bit;
        }
    }

    public uint GetPrice(uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var bitIndex = numBitLevels; bitIndex > 0;)
        {
            bitIndex--;
            var bit = (symbol >> bitIndex) & 1;
            price += models[m].GetPrice(bit);
            m = (m << 1) + bit;
        }
        return price;
    }

    public void Init()
    {
        for (uint i = 1; i < (1 << numBitLevels); i++)
            models[i].Init();
    }

    public void ReverseEncode(RcEncoder rangeEncoder, uint symbol)
    {
        uint m = 1;
        for (uint i = 0; i < numBitLevels; i++)
        {
            var bit = symbol & 1;
            models[m].Encode(rangeEncoder, bit);
            m = (m << 1) | bit;
            symbol >>= 1;
        }
    }

    public uint ReverseGetPrice(uint symbol)
    {
        uint price = 0;
        uint m = 1;
        for (var i = numBitLevels; i > 0; i--)
        {
            var bit = symbol & 1;
            symbol >>= 1;
            price += models[m].GetPrice(bit);
            m = (m << 1) | bit;
        }
        return price;
    }

    #endregion Public Methods
}
