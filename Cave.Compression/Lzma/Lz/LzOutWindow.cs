#nullable disable

namespace Cave.Compression.Lzma.LZ;

sealed class LzOutWindow
{
    #region Private Fields

    byte[] buffer;
    uint position;
    System.IO.Stream stream;
    uint streamPos;
    uint windowSize;

    #endregion Private Fields

    #region Public Fields

    public uint TrainSize;

    #endregion Public Fields

    #region Public Methods

    public void CopyBlock(uint distance, uint len)
    {
        var pos = position - distance - 1;
        if (pos >= windowSize)
            pos += windowSize;
        for (; len > 0; len--)
        {
            if (pos >= windowSize)
                pos = 0;
            buffer[position++] = buffer[pos++];
            if (position >= windowSize)
                Flush();
        }
    }

    public void Create(uint windowSize)
    {
        if (this.windowSize != windowSize)
        {
            // System.GC.Collect();
            buffer = new byte[windowSize];
        }
        this.windowSize = windowSize;
        position = 0;
        streamPos = 0;
    }

    public void Flush()
    {
        var size = position - streamPos;
        if (size == 0)
            return;
        stream.Write(buffer, (int)streamPos, (int)size);
        if (position >= windowSize)
            position = 0;
        streamPos = position;
    }

    public byte GetByte(uint distance)
    {
        var pos = position - distance - 1;
        if (pos >= windowSize)
            pos += windowSize;
        return buffer[pos];
    }

    public void Init(System.IO.Stream stream, bool solid)
    {
        ReleaseStream();
        this.stream = stream;
        if (!solid)
        {
            streamPos = 0;
            position = 0;
            TrainSize = 0;
        }
    }

    public void PutByte(byte b)
    {
        buffer[position++] = b;
        if (position >= windowSize)
            Flush();
    }

    public void ReleaseStream()
    {
        Flush();
        stream = null;
    }

    public bool Train(System.IO.Stream stream)
    {
        var len = stream.Length;
        var size = (len < windowSize) ? (uint)len : windowSize;
        TrainSize = size;
        stream.Position = len - size;
        streamPos = position = 0;
        while (size > 0)
        {
            var curSize = windowSize - position;
            if (size < curSize)
                curSize = size;
            var numReadBytes = stream.Read(buffer, (int)position, (int)curSize);
            if (numReadBytes == 0)
                return false;
            size -= (uint)numReadBytes;
            position += (uint)numReadBytes;
            streamPos += (uint)numReadBytes;
            if (position == windowSize)
                streamPos = position = 0;
        }
        return true;
    }

    #endregion Public Methods
}
