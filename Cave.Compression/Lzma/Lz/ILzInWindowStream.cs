namespace Cave.Compression.Lzma.LZ;

interface ILzInWindowStream
{
    #region Public Methods

    byte GetIndexByte(int index);

    uint GetMatchLen(int index, uint distance, uint limit);

    uint GetNumAvailableBytes();

    void Init();

    void ReleaseStream();

    void SetStream(System.IO.Stream inStream);

    #endregion Public Methods
}
