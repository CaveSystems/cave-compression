#nullable disable

namespace Cave.Compression.Lzma.LZ;

class LzOutWindow
{
    #region Private Fields

    byte[] _buffer = null;
    uint _pos;
    System.IO.Stream _stream;
    uint _streamPos;
    uint _windowSize = 0;

    #endregion Private Fields

    #region Public Fields

    public uint TrainSize = 0;

    #endregion Public Fields

    #region Public Methods

    public void CopyBlock(uint distance, uint len)
    {
        var pos = _pos - distance - 1;
        if (pos >= _windowSize)
            pos += _windowSize;
        for (; len > 0; len--)
        {
            if (pos >= _windowSize)
                pos = 0;
            _buffer[_pos++] = _buffer[pos++];
            if (_pos >= _windowSize)
                Flush();
        }
    }

    public void Create(uint windowSize)
    {
        if (_windowSize != windowSize)
        {
            // System.GC.Collect();
            _buffer = new byte[windowSize];
        }
        _windowSize = windowSize;
        _pos = 0;
        _streamPos = 0;
    }

    public void Flush()
    {
        var size = _pos - _streamPos;
        if (size == 0)
            return;
        _stream.Write(_buffer, (int)_streamPos, (int)size);
        if (_pos >= _windowSize)
            _pos = 0;
        _streamPos = _pos;
    }

    public byte GetByte(uint distance)
    {
        var pos = _pos - distance - 1;
        if (pos >= _windowSize)
            pos += _windowSize;
        return _buffer[pos];
    }

    public void Init(System.IO.Stream stream, bool solid)
    {
        ReleaseStream();
        _stream = stream;
        if (!solid)
        {
            _streamPos = 0;
            _pos = 0;
            TrainSize = 0;
        }
    }

    public void PutByte(byte b)
    {
        _buffer[_pos++] = b;
        if (_pos >= _windowSize)
            Flush();
    }

    public void ReleaseStream()
    {
        Flush();
        _stream = null;
    }

    public bool Train(System.IO.Stream stream)
    {
        var len = stream.Length;
        var size = (len < _windowSize) ? (uint)len : _windowSize;
        TrainSize = size;
        stream.Position = len - size;
        _streamPos = _pos = 0;
        while (size > 0)
        {
            var curSize = _windowSize - _pos;
            if (size < curSize)
                curSize = size;
            var numReadBytes = stream.Read(_buffer, (int)_pos, (int)curSize);
            if (numReadBytes == 0)
                return false;
            size -= (uint)numReadBytes;
            _pos += (uint)numReadBytes;
            _streamPos += (uint)numReadBytes;
            if (_pos == _windowSize)
                _streamPos = _pos = 0;
        }
        return true;
    }

    #endregion Public Methods
}
