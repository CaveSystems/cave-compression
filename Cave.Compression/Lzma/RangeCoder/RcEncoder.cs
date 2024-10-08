#nullable disable

using System.IO;

namespace Cave.Compression.Lzma.RangeCoder;

sealed class RcEncoder
{
    #region Private Fields

    byte cache;
    uint cacheSize;
    long startPosition;
    Stream stream;

    #endregion Private Fields

    #region Public Fields

    public const uint KTopValue = 1 << 24;
    public ulong Low;
    public uint Range;

    #endregion Public Fields

    #region Public Methods

    public void CloseStream() => stream.Close();

    public void Encode(uint start, uint size, uint total)
    {
        Low += start * (Range /= total);
        Range *= size;
        while (Range < KTopValue)
        {
            Range <<= 8;
            ShiftLow();
        }
    }

    public void EncodeBit(uint size0, int numTotalBits, uint symbol)
    {
        var newBound = (Range >> numTotalBits) * size0;
        if (symbol == 0)
            Range = newBound;
        else
        {
            Low += newBound;
            Range -= newBound;
        }
        while (Range < KTopValue)
        {
            Range <<= 8;
            ShiftLow();
        }
    }

    public void EncodeDirectBits(uint v, int numTotalBits)
    {
        for (var i = numTotalBits - 1; i >= 0; i--)
        {
            Range >>= 1;
            if (((v >> i) & 1) == 1)
                Low += Range;
            if (Range < KTopValue)
            {
                Range <<= 8;
                ShiftLow();
            }
        }
    }

    public void FlushData()
    {
        for (var i = 0; i < 5; i++)
            ShiftLow();
    }

    public void FlushStream() => stream.Flush();

    public long GetProcessedSizeAdd() => cacheSize + stream.Position - startPosition + 4;// (long)Stream.GetProcessedSize();

    public void Init()
    {
        startPosition = stream.Position;

        Low = 0;
        Range = 0xFFFFFFFF;
        cacheSize = 1;
        cache = 0;
    }

    public void ReleaseStream() => stream = null;

    public void SetStream(Stream stream) => this.stream = stream;

    public void ShiftLow()
    {
        if ((uint)Low < 0xFF000000u || (uint)(Low >> 32) == 1u)
        {
            var temp = cache;
            do
            {
                stream.WriteByte((byte)(temp + (Low >> 32)));
                temp = 0xFF;
            }
            while (--cacheSize != 0);
            cache = (byte)(((uint)Low) >> 24);
        }
        cacheSize++;
        Low = ((uint)Low) << 8;
    }

    #endregion Public Methods
}
