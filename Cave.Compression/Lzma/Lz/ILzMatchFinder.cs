// IMatchFinder.cs

namespace Cave.Compression.Lzma.LZ;

interface ILzMatchFinder : ILzInWindowStream
{
    #region Public Methods

    void Create(uint historySize, uint keepAddBufferBefore, uint matchMaxLen, uint keepAddBufferAfter);

    uint GetMatches(uint[] distances);

    void Skip(uint num);

    #endregion Public Methods
}
