#nullable disable

namespace Cave.Compression.Lzma.RangeCoder;

class RcDecoder
{
    #region Public Fields

    public const uint kTopValue = 1 << 24;
    public uint Code;
    public uint Range;

    // public Buffer.InBuffer Stream = new Buffer.InBuffer(1 << 16);
    public System.IO.Stream Stream;

    #endregion Public Fields

    #region Public Methods

    public void CloseStream() => Stream.Close();

    public void Decode(uint start, uint size, uint total)
    {
        Code -= start * Range;
        Range *= size;
        Normalize();
    }

    public uint DecodeBit(uint size0, int numTotalBits)
    {
        var newBound = (Range >> numTotalBits) * size0;
        uint symbol;
        if (Code < newBound)
        {
            symbol = 0;
            Range = newBound;
        }
        else
        {
            symbol = 1;
            Code -= newBound;
            Range -= newBound;
        }
        Normalize();
        return symbol;
    }

    public uint DecodeDirectBits(int numTotalBits)
    {
        var range = Range;
        var code = Code;
        uint result = 0;
        for (var i = numTotalBits; i > 0; i--)
        {
            range >>= 1;
            /*
				result <<= 1;
				if (code >= range)
				{
					code -= range;
					result |= 1;
				}
				*/
            var t = (code - range) >> 31;
            code -= range & (t - 1);
            result = (result << 1) | (1 - t);

            if (range < kTopValue)
            {
                code = (code << 8) | (byte)Stream.ReadByte();
                range <<= 8;
            }
        }
        Range = range;
        Code = code;
        return result;
    }

    public uint GetThreshold(uint total) => Code / (Range /= total);

    public void Init(System.IO.Stream stream)
    {
        // Stream.Init(stream);
        Stream = stream;

        Code = 0;
        Range = 0xFFFFFFFF;
        for (var i = 0; i < 5; i++)
            Code = (Code << 8) | (byte)Stream.ReadByte();
    }

    public void Normalize()
    {
        while (Range < kTopValue)
        {
            Code = (Code << 8) | (byte)Stream.ReadByte();
            Range <<= 8;
        }
    }

    public void Normalize2()
    {
        if (Range < kTopValue)
        {
            Code = (Code << 8) | (byte)Stream.ReadByte();
            Range <<= 8;
        }
    }

    public void ReleaseStream() =>
        // Stream.ReleaseStream();
        Stream = null;

    #endregion Public Methods

    // ulong GetProcessedSize() {return Stream.GetProcessedSize(); }
}
