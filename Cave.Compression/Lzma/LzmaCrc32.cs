namespace Cave.Compression.Lzma;

class LzmaCrc32
{
    #region Private Fields

    uint _value = 0xFFFFFFFF;

    #endregion Private Fields

    #region Private Methods

    static uint CalculateDigest(byte[] data, uint offset, uint size)
    {
        var crc = new LzmaCrc32();
        // crc.Init();
        crc.Update(data, offset, size);
        return crc.GetDigest();
    }

    static bool VerifyDigest(uint digest, byte[] data, uint offset, uint size) => CalculateDigest(data, offset, size) == digest;

    #endregion Private Methods

    #region Public Fields

    public static readonly uint[] Table;

    #endregion Public Fields

    #region Public Constructors

    static LzmaCrc32()
    {
        Table = new uint[256];
        const uint kPoly = 0xEDB88320;
        for (uint i = 0; i < 256; i++)
        {
            var r = i;
            for (var j = 0; j < 8; j++)
                if ((r & 1) != 0)
                    r = (r >> 1) ^ kPoly;
                else
                    r >>= 1;
            Table[i] = r;
        }
    }

    #endregion Public Constructors

    #region Public Methods

    public uint GetDigest() => _value ^ 0xFFFFFFFF;

    public void Init() => _value = 0xFFFFFFFF;

    public void Update(byte[] data, uint offset, uint size)
    {
        for (uint i = 0; i < size; i++)
            _value = Table[((byte)_value) ^ data[offset + i]] ^ (_value >> 8);
    }

    public void UpdateByte(byte b) => _value = Table[((byte)_value) ^ b] ^ (_value >> 8);

    #endregion Public Methods
}
