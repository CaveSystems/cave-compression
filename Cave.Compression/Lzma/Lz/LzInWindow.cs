#nullable disable

namespace Cave.Compression.Lzma.LZ;

class LzInWindow
{
    #region Private Fields

    uint keepSizeAfter;
    uint keepSizeBefore;
    uint pointerToLastSafePosition;
    uint posLimit;
    System.IO.Stream stream;

    // offset (from _buffer) of first byte when new block reading must be done
    bool streamEndWasReached;

    #endregion Private Fields

    #region Private Methods

    void Free() => BufferBase = null;

    #endregion Private Methods

    #region Public Fields

    public uint BlockSize;

    public byte[] BufferBase;

    // if (true) then _streamPos shows real end of stream
    public uint BufferOffset;

    // pointer to buffer with data Size of Allocated memory block
    public uint Pos;

    // how many BYTEs must be kept in buffer before _pos how many BYTEs must be kept buffer after _pos
    public uint StreamPos;

    #endregion Public Fields

    // offset (from _buffer) of curent byte offset (from _buffer) of first not read byte from Stream

    #region Public Methods

    public void Create(uint keepSizeBefore, uint keepSizeAfter, uint keepSizeReserv)
    {
        this.keepSizeBefore = keepSizeBefore;
        this.keepSizeAfter = keepSizeAfter;
        var blockSize = keepSizeBefore + keepSizeAfter + keepSizeReserv;
        if (BufferBase == null || BlockSize != blockSize)
        {
            Free();
            BlockSize = blockSize;
            BufferBase = new byte[BlockSize];
        }
        pointerToLastSafePosition = BlockSize - keepSizeAfter;
    }

    public byte GetIndexByte(int index) => BufferBase[BufferOffset + Pos + index];

    // index + limit have not to exceed _keepSizeAfter;
    public uint GetMatchLen(int index, uint distance, uint limit)
    {
        if (streamEndWasReached)
            if (Pos + index + limit > StreamPos)
                limit = StreamPos - (uint)(Pos + index);
        distance++;
        // Byte *pby = _buffer + (size_t)_pos + index;
        var pby = BufferOffset + Pos + (uint)index;

        uint i;
        for (i = 0; i < limit && BufferBase[pby + i] == BufferBase[pby + i - distance]; i++) ;
        return i;
    }

    public uint GetNumAvailableBytes() => StreamPos - Pos;

    public void Init()
    {
        BufferOffset = 0;
        Pos = 0;
        StreamPos = 0;
        streamEndWasReached = false;
        ReadBlock();
    }

    public void MoveBlock()
    {
        var offset = BufferOffset + Pos - keepSizeBefore;
        // we need one additional byte, since MovePos moves on 1 byte.
        if (offset > 0)
            offset--;

        var numBytes = BufferOffset + StreamPos - offset;

        // check negative offset ????
        for (uint i = 0; i < numBytes; i++)
        {
            BufferBase[i] = BufferBase[offset + i];
        }
        BufferOffset -= offset;
    }

    public void MovePos()
    {
        Pos++;
        if (Pos > posLimit)
        {
            var pointerToPostion = BufferOffset + Pos;
            if (pointerToPostion > pointerToLastSafePosition) MoveBlock();
            ReadBlock();
        }
    }

    public virtual void ReadBlock()
    {
        if (streamEndWasReached) return;
        while (true)
        {
            var size = (int)(0 - BufferOffset + BlockSize - StreamPos);
            if (size == 0)
                return;
            var numReadBytes = stream.Read(BufferBase, (int)(BufferOffset + StreamPos), size);
            if (numReadBytes == 0)
            {
                posLimit = StreamPos;
                var pointerToPostion = BufferOffset + posLimit;
                if (pointerToPostion > pointerToLastSafePosition)
                {
                    posLimit = pointerToLastSafePosition - BufferOffset;
                }

                streamEndWasReached = true;
                return;
            }
            StreamPos += (uint)numReadBytes;
            if (StreamPos >= Pos + keepSizeAfter)
            {
                posLimit = StreamPos - keepSizeAfter;
            }
        }
    }

    public void ReduceOffsets(int subValue)
    {
        BufferOffset += (uint)subValue;
        posLimit -= (uint)subValue;
        Pos -= (uint)subValue;
        StreamPos -= (uint)subValue;
    }

    public void ReleaseStream() => stream = null;

    public void SetStream(System.IO.Stream stream) => this.stream = stream;

    #endregion Public Methods
}
