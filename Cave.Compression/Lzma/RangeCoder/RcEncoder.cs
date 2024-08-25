#nullable disable

using System.IO;

namespace Cave.Compression.Lzma.RangeCoder;

class RcEncoder
{
    #region Private Fields

    byte _cache;
    uint _cacheSize;
    long StartPosition;
    System.IO.Stream Stream;

    #endregion Private Fields

    #region Public Fields

    public const uint kTopValue = 1 << 24;
    public ulong Low;
    public uint Range;

    #endregion Public Fields

    #region Public Methods

    public void CloseStream() => Stream.Close();

    public void Encode(uint start, uint size, uint total)
    {
        Low += start * (Range /= total);
        Range *= size;
        while (Range < kTopValue)
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
        while (Range < kTopValue)
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
            if (Range < kTopValue)
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

    public void FlushStream() => Stream.Flush();

    public long GetProcessedSizeAdd() => _cacheSize + Stream.Position - StartPosition + 4;// (long)Stream.GetProcessedSize();

    public void Init()
    {
        StartPosition = Stream.Position;

        Low = 0;
        Range = 0xFFFFFFFF;
        _cacheSize = 1;
        _cache = 0;
    }

    public void ReleaseStream() => Stream = null;

    public void SetStream(Stream stream) => Stream = stream;

    public void ShiftLow()
    {
        if ((uint)Low < (uint)0xFF000000 || (uint)(Low >> 32) == 1)
        {
            var temp = _cache;
            do
            {
                Stream.WriteByte((byte)(temp + (Low >> 32)));
                temp = 0xFF;
            }
            while (--_cacheSize != 0);
            _cache = (byte)(((uint)Low) >> 24);
        }
        _cacheSize++;
        Low = ((uint)Low) << 8;
    }

    #endregion Public Methods
}
