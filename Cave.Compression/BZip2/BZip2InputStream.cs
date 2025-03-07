using System;
using System.IO;
using Cave.Compression.Checksum;

namespace Cave.Compression.BZip2;

/// <summary>An input stream that decompresses files in the BZip2 format.</summary>
public class BZip2InputStream : Stream
{
    #region Private Fields

    readonly int[][] baseArray = new int[BZip2Constants.GroupCount][];
    readonly Stream baseStream;
    readonly bool[] inUse = new bool[256];
    readonly int[][] limit = new int[BZip2Constants.GroupCount][];
    readonly BZip2Crc mCrc = new();
    readonly int[] minLens = new int[BZip2Constants.GroupCount];
    readonly int[][] perm = new int[BZip2Constants.GroupCount][];
    readonly byte[] selector = new byte[BZip2Constants.MaximumSelectors];
    readonly byte[] selectorMtf = new byte[BZip2Constants.MaximumSelectors];
    readonly byte[] seqToUnseq = new byte[256];
    readonly byte[] unseqToSeq = new byte[256];
    readonly int[] unzftab = new int[256];
    bool blockRandomised;
    int blockSize100k;
    int bsBuff;
    int bsLive;
    int ch2;
    int chPrev;
    int computedBlockCRC;
    uint computedCombinedCRC;
    int count;
    int currentChar = -1;
    State currentState = State.StartBlock;
    int i2;
    int j2;
    int last;
    byte[] ll8 = [];
    int nInUse;
    int origPtr;
    int rNToGo;
    int rTPos;
    int storedBlockCRC;
    int storedCombinedCRC;
    bool streamEnd;
    int tPos;
    int[] tt = [];
    byte z;

    #endregion Private Fields

    #region Private Enums

    enum State : int
    {
        StartBlock = 1,
        RandPartA,
        RandPartB,
        RandPartC,
        NoRandPartA,
        NoRandPartB,
        NoRandPartC,
    }

    #endregion Private Enums

    #region Private Methods

    static void BadBlockHeader() => throw new InvalidDataException("BZip2 input stream bad block header");

    static void BlockOverrun() => throw new InvalidDataException("BZip2 input stream block overrun");

    static void CompressedStreamEOF() => throw new EndOfStreamException("BZip2 input stream end of compressed stream");

    static void CrcError() => throw new InvalidDataException("BZip2 input stream crc error");

    static void HbCreateDecodeTables(int[] limit, int[] baseArray, int[] perm, char[] length, int minLen, int maxLen, int alphaSize)
    {
        var pp = 0;

        for (var i = minLen; i <= maxLen; ++i)
        {
            for (var j = 0; j < alphaSize; ++j)
            {
                if (length[j] == i)
                {
                    perm[pp] = j;
                    ++pp;
                }
            }
        }

        for (var i = 0; i < BZip2Constants.MaximumCodeLength; i++)
        {
            baseArray[i] = 0;
        }

        for (var i = 0; i < alphaSize; i++)
        {
            ++baseArray[length[i] + 1];
        }

        for (var i = 1; i < BZip2Constants.MaximumCodeLength; i++)
        {
            baseArray[i] += baseArray[i - 1];
        }

        for (var i = 0; i < BZip2Constants.MaximumCodeLength; i++)
        {
            limit[i] = 0;
        }

        var vec = 0;

        for (var i = minLen; i <= maxLen; i++)
        {
            vec += baseArray[i + 1] - baseArray[i];
            limit[i] = vec - 1;
            vec <<= 1;
        }

        for (var i = minLen + 1; i <= maxLen; i++)
        {
            baseArray[i] = ((limit[i - 1] + 1) << 1) - baseArray[i];
        }
    }

    /*--
    freq table collected to save a pass over the data
    during decompression.
    --*/
    /*--
    index of the last char in the block, so
    the block size == last + 1.
    --*/
    /*--
    index in zptr[] of original string after sorting.
    --*/
    /*--
    always: in the range 0 .. 9.
    The current block size is 100000 * this number.
    --*/

    int BsGetInt32()
    {
        var result = BsR(8);
        result = (result << 8) | BsR(8);
        result = (result << 8) | BsR(8);
        result = (result << 8) | BsR(8);
        return result;
    }

    int BsGetIntVS(int numBits) => BsR(numBits);

    char BsGetUChar() => (char)BsR(8);

    int BsR(int n)
    {
        while (bsLive < n)
        {
            FillBuffer();
        }

        var v = (bsBuff >> (bsLive - n)) & ((1 << n) - 1);
        bsLive -= n;
        return v;
    }

    void Complete()
    {
        storedCombinedCRC = BsGetInt32();
        if (storedCombinedCRC != (int)computedCombinedCRC)
        {
            CrcError();
        }

        streamEnd = true;
    }

    void EndBlock()
    {
        computedBlockCRC = (int)mCrc.Value;

        // -- A bad CRC is considered a fatal error. --
        if (storedBlockCRC != computedBlockCRC)
        {
            CrcError();
        }

        // 1528150659
        computedCombinedCRC = ((computedCombinedCRC << 1) & 0xFFFFFFFF) | (computedCombinedCRC >> 31);
        computedCombinedCRC ^= (uint)computedBlockCRC;
    }

    void FillBuffer()
    {
        var thech = 0;

        try
        {
            thech = baseStream.ReadByte();
        }
        catch (Exception)
        {
            CompressedStreamEOF();
        }

        if (thech == -1)
        {
            CompressedStreamEOF();
        }

        bsBuff = (bsBuff << 8) | (thech & 0xFF);
        bsLive += 8;
    }

    void GetAndMoveToFrontDecode()
    {
        var yy = new byte[256];
        int nextSym;

        var limitLast = BZip2Constants.BlockSize * blockSize100k;
        origPtr = BsGetIntVS(24);

        RecvDecodingTables();
        var endOfBlock = nInUse + 1;
        var groupNo = -1;
        var groupPos = 0;

        /*--
        Setting up the unzftab entries here is not strictly
        necessary, but it does save having to do it later
        in a separate pass, and so saves a block's worth of
        cache misses.
        --*/
        for (var i = 0; i <= 255; i++)
        {
            unzftab[i] = 0;
        }

        for (var i = 0; i <= 255; i++)
        {
            yy[i] = (byte)i;
        }

        last = -1;

        if (groupPos == 0)
        {
            groupNo++;
            groupPos = BZip2Constants.GroupSize;
        }

        groupPos--;
        int zt = selector[groupNo];
        var zn = minLens[zt];
        var zvec = BsR(zn);
        int zj;

        while (zvec > limit[zt][zn])
        {
            if (zn > 20)
            { // the longest code
                throw new InvalidDataException("Bzip data error");
            }

            zn++;
            while (bsLive < 1)
            {
                FillBuffer();
            }

            zj = (bsBuff >> (bsLive - 1)) & 1;
            bsLive--;
            zvec = (zvec << 1) | zj;
        }

        if (zvec - baseArray[zt][zn] is < 0 or >= BZip2Constants.MaximumAlphaSize)
        {
            throw new InvalidDataException("Bzip data error");
        }

        nextSym = perm[zt][zvec - baseArray[zt][zn]];

        while (true)
        {
            if (nextSym == endOfBlock)
            {
                break;
            }

            if (nextSym is BZip2Constants.RunA or BZip2Constants.RunB)
            {
                var s = -1;
                var n = 1;
                do
                {
                    if (nextSym == BZip2Constants.RunA)
                    {
                        s += (0 + 1) * n;
                    }
                    else if (nextSym == BZip2Constants.RunB)
                    {
                        s += (1 + 1) * n;
                    }

                    n <<= 1;

                    if (groupPos == 0)
                    {
                        groupNo++;
                        groupPos = BZip2Constants.GroupSize;
                    }

                    groupPos--;

                    zt = selector[groupNo];
                    zn = minLens[zt];
                    zvec = BsR(zn);

                    while (zvec > limit[zt][zn])
                    {
                        zn++;
                        while (bsLive < 1)
                        {
                            FillBuffer();
                        }

                        zj = (bsBuff >> (bsLive - 1)) & 1;
                        bsLive--;
                        zvec = (zvec << 1) | zj;
                    }

                    nextSym = perm[zt][zvec - baseArray[zt][zn]];
                }
                while (nextSym is BZip2Constants.RunA or BZip2Constants.RunB);

                s++;
                var ch = seqToUnseq[yy[0]];
                unzftab[ch] += s;

                while (s > 0)
                {
                    last++;
                    ll8[last] = ch;
                    s--;
                }

                if (last >= limitLast)
                {
                    BlockOverrun();
                }

                continue;
            }
            else
            {
                last++;
                if (last >= limitLast)
                {
                    BlockOverrun();
                }

                var tmp = yy[nextSym - 1];
                unzftab[seqToUnseq[tmp]]++;
                ll8[last] = seqToUnseq[tmp];

                for (var j = nextSym - 1; j > 0; --j)
                {
                    yy[j] = yy[j - 1];
                }

                yy[0] = tmp;

                if (groupPos == 0)
                {
                    groupNo++;
                    groupPos = BZip2Constants.GroupSize;
                }

                groupPos--;
                zt = selector[groupNo];
                zn = minLens[zt];
                zvec = BsR(zn);
                while (zvec > limit[zt][zn])
                {
                    zn++;
                    while (bsLive < 1)
                    {
                        FillBuffer();
                    }

                    zj = (bsBuff >> (bsLive - 1)) & 1;
                    bsLive--;
                    zvec = (zvec << 1) | zj;
                }

                nextSym = perm[zt][zvec - baseArray[zt][zn]];
                continue;
            }
        }
    }

    void InitBlock()
    {
        var magic1 = BsGetUChar();
        var magic2 = BsGetUChar();
        var magic3 = BsGetUChar();
        var magic4 = BsGetUChar();
        var magic5 = BsGetUChar();
        var magic6 = BsGetUChar();

        if (magic1 == 0x17 && magic2 == 0x72 && magic3 == 0x45 && magic4 == 0x38 && magic5 == 0x50 && magic6 == 0x90)
        {
            Complete();
            return;
        }

        if (magic1 != 0x31 || magic2 != 0x41 || magic3 != 0x59 || magic4 != 0x26 || magic5 != 0x53 || magic6 != 0x59)
        {
            BadBlockHeader();
            streamEnd = true;
            return;
        }

        storedBlockCRC = BsGetInt32();

        blockRandomised = BsR(1) == 1;

        GetAndMoveToFrontDecode();

        mCrc.Reset();
        currentState = State.StartBlock;
    }

    void Initialize()
    {
        var magic1 = BsGetUChar();
        var magic2 = BsGetUChar();

        var magic3 = BsGetUChar();
        var magic4 = BsGetUChar();

        if (magic1 != 'B' || magic2 != 'Z' || magic3 != 'h' || magic4 < '1' || magic4 > '9')
        {
            streamEnd = true;
            return;
        }

        SetDecompressStructureSizes(magic4 - '0');
        computedCombinedCRC = 0;
    }

    void MakeMaps()
    {
        nInUse = 0;
        for (var i = 0; i < 256; ++i)
        {
            if (inUse[i])
            {
                seqToUnseq[nInUse] = (byte)i;
                unseqToSeq[i] = (byte)nInUse;
                nInUse++;
            }
        }
    }

    void RecvDecodingTables()
    {
        var len = new char[BZip2Constants.GroupCount][];
        for (var i = 0; i < BZip2Constants.GroupCount; ++i)
        {
            len[i] = new char[BZip2Constants.MaximumAlphaSize];
        }

        var inUse16 = new bool[16];

        //--- Receive the mapping table ---
        for (var i = 0; i < 16; i++)
        {
            inUse16[i] = BsR(1) == 1;
        }

        for (var i = 0; i < 16; i++)
        {
            if (inUse16[i])
            {
                for (var j = 0; j < 16; j++)
                {
                    inUse[(i * 16) + j] = BsR(1) == 1;
                }
            }
            else
            {
                for (var j = 0; j < 16; j++)
                {
                    inUse[(i * 16) + j] = false;
                }
            }
        }

        MakeMaps();
        var alphaSize = nInUse + 2;

        //--- Now the selectors ---
        var nGroups = BsR(3);
        var nSelectors = BsR(15);

        for (var i = 0; i < nSelectors; i++)
        {
            var j = 0;
            while (BsR(1) == 1)
            {
                j++;
            }

            selectorMtf[i] = (byte)j;
        }

        //--- Undo the MTF values for the selectors. ---
        var pos = new byte[BZip2Constants.GroupCount];
        for (var v = 0; v < nGroups; v++)
        {
            pos[v] = (byte)v;
        }

        for (var i = 0; i < nSelectors; i++)
        {
            int v = selectorMtf[i];
            var tmp = pos[v];
            while (v > 0)
            {
                pos[v] = pos[v - 1];
                v--;
            }

            pos[0] = tmp;
            selector[i] = tmp;
        }

        //--- Now the coding tables ---
        for (var t = 0; t < nGroups; t++)
        {
            var curr = BsR(5);
            for (var i = 0; i < alphaSize; i++)
            {
                while (BsR(1) == 1)
                {
                    if (BsR(1) == 0)
                    {
                        curr++;
                    }
                    else
                    {
                        curr--;
                    }
                }

                len[t][i] = (char)curr;
            }
        }

        //--- Create the Huffman decoding tables ---
        for (var t = 0; t < nGroups; t++)
        {
            var minLen = 32;
            var maxLen = 0;
            for (var i = 0; i < alphaSize; i++)
            {
                maxLen = Math.Max(maxLen, len[t][i]);
                minLen = Math.Min(minLen, len[t][i]);
            }

            HbCreateDecodeTables(limit[t], baseArray[t], perm[t], len[t], minLen, maxLen, alphaSize);
            minLens[t] = minLen;
        }
    }

    void SetDecompressStructureSizes(int newSize100k)
    {
        if (!(newSize100k >= 0 && newSize100k <= 9 && blockSize100k >= 0 && blockSize100k <= 9))
        {
            throw new InvalidDataException("Invalid block size");
        }

        blockSize100k = newSize100k;

        if (newSize100k == 0)
        {
            return;
        }

        var n = BZip2Constants.BlockSize * newSize100k;
        ll8 = new byte[n];
        tt = new int[n];
    }

    void SetupBlock()
    {
        {
            var cftab = new int[257];

            cftab[0] = 0;
            Array.Copy(unzftab, 0, cftab, 1, 256);

            for (var i = 1; i <= 256; i++)
            {
                cftab[i] += cftab[i - 1];
            }

            for (var i = 0; i <= last; i++)
            {
                var ch = ll8[i];
                tt[cftab[ch]] = i;
                cftab[ch]++;
            }
        }

        tPos = tt[origPtr];

        count = 0;
        i2 = 0;
        ch2 = 256;   /*-- not a char and not EOF --*/

        if (blockRandomised)
        {
            rNToGo = 0;
            rTPos = 0;
            SetupRandPartA();
        }
        else
        {
            SetupNoRandPartA();
        }
    }

    void SetupNoRandPartA()
    {
        if (i2 <= last)
        {
            chPrev = ch2;
            ch2 = ll8[tPos];
            tPos = tt[tPos];
            i2++;

            currentChar = ch2;
            currentState = State.NoRandPartB;
            mCrc.Update(ch2);
        }
        else
        {
            EndBlock();
            InitBlock();
            SetupBlock();
        }
    }

    void SetupNoRandPartB()
    {
        if (ch2 != chPrev)
        {
            currentState = State.NoRandPartA;
            count = 1;
            SetupNoRandPartA();
        }
        else
        {
            count++;
            if (count >= 4)
            {
                z = ll8[tPos];
                tPos = tt[tPos];
                currentState = State.NoRandPartC;
                j2 = 0;
                SetupNoRandPartC();
            }
            else
            {
                currentState = State.NoRandPartA;
                SetupNoRandPartA();
            }
        }
    }

    void SetupNoRandPartC()
    {
        if (j2 < z)
        {
            currentChar = ch2;
            mCrc.Update(ch2);
            j2++;
        }
        else
        {
            currentState = State.NoRandPartA;
            i2++;
            count = 0;
            SetupNoRandPartA();
        }
    }

    void SetupRandPartA()
    {
        if (i2 <= last)
        {
            chPrev = ch2;
            ch2 = ll8[tPos];
            tPos = tt[tPos];
            if (rNToGo == 0)
            {
                rNToGo = BZip2Constants.RandomNumbers[rTPos];
                rTPos++;
                if (rTPos == 512)
                {
                    rTPos = 0;
                }
            }

            rNToGo--;
            ch2 ^= (rNToGo == 1) ? 1 : 0;
            i2++;

            currentChar = ch2;
            currentState = State.RandPartB;
            mCrc.Update(ch2);
        }
        else
        {
            EndBlock();
            InitBlock();
            SetupBlock();
        }
    }

    void SetupRandPartB()
    {
        if (ch2 != chPrev)
        {
            currentState = State.RandPartA;
            count = 1;
            SetupRandPartA();
        }
        else
        {
            count++;
            if (count >= 4)
            {
                z = ll8[tPos];
                tPos = tt[tPos];
                if (rNToGo == 0)
                {
                    rNToGo = BZip2Constants.RandomNumbers[rTPos];
                    rTPos++;
                    if (rTPos == 512)
                    {
                        rTPos = 0;
                    }
                }

                rNToGo--;
                z ^= (byte)((rNToGo == 1) ? 1 : 0);
                j2 = 0;
                currentState = State.RandPartC;
                SetupRandPartC();
            }
            else
            {
                currentState = State.RandPartA;
                SetupRandPartA();
            }
        }
    }

    void SetupRandPartC()
    {
        if (j2 < z)
        {
            currentChar = ch2;
            mCrc.Update(ch2);
            j2++;
        }
        else
        {
            currentState = State.RandPartA;
            i2++;
            count = 0;
            SetupRandPartA();
        }
    }

    #endregion Private Methods

    #region Protected Methods

    /// <summary>Releases the unmanaged resources used and optionally releases the managed resources.</summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && IsStreamOwner)
        {
            baseStream.Dispose();
        }
        base.Dispose(disposing);
    }

    #endregion Protected Methods

    #region Public Constructors

    /// <summary>Initializes a new instance of the <see cref="BZip2InputStream"/> class.</summary>
    /// <param name="stream">Data source.</param>
    public BZip2InputStream(Stream stream)
    {
        // init arrays
        for (var i = 0; i < BZip2Constants.GroupCount; ++i)
        {
            limit[i] = new int[BZip2Constants.MaximumAlphaSize];
            baseArray[i] = new int[BZip2Constants.MaximumAlphaSize];
            perm[i] = new int[BZip2Constants.MaximumAlphaSize];
        }

        baseStream = stream ?? throw new ArgumentNullException(nameof(stream));
        bsLive = 0;
        bsBuff = 0;
        Initialize();
        InitBlock();
        SetupBlock();
    }

    #endregion Public Constructors

    #region Public Properties

    /// <summary>Gets a value indicating whether the stream supports reading.</summary>
    public override bool CanRead => baseStream.CanRead;

    /// <summary>Gets a value indicating whether the current stream supports seeking.</summary>
    public override bool CanSeek => false;

    /// <summary>Gets a value indicating whether the current stream supports writing. This property always returns false.</summary>
    public override bool CanWrite => false;

    /// <summary>Gets or sets a value indicating whether the underlying stream shall be closed by this instance.</summary>
    public bool IsStreamOwner { get; set; } = true;

    /// <summary>Gets the length in bytes of the stream.</summary>
    public override long Length => baseStream.Length;

    /// <summary>Gets or sets gets the current position of the stream. Setting the position is not supported and will throw a NotSupportException.</summary>
    /// <exception cref="NotSupportedException">Any attempt to set the position.</exception>
    public override long Position
    {
        get => baseStream.Position;
        set => throw new NotSupportedException("BZip2InputStream position cannot be set");
    }

    #endregion Public Properties

    #region Public Methods

    /// <summary>Flushes the stream.</summary>
    public override void Flush() => baseStream.Flush();

    /// <summary>Read a sequence of bytes and advances the read position by one byte.</summary>
    /// <param name="buffer">Array of bytes to store values in.</param>
    /// <param name="offset">Offset in array to begin storing data.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>
    /// The total number of bytes read into the buffer. This might be less than the number of bytes requested if that number of bytes are not currently
    /// available or zero if the end of the stream is reached.
    /// </returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        for (var i = 0; i < count; ++i)
        {
            var rb = ReadByte();
            if (rb == -1)
            {
                return i;
            }

            buffer[offset + i] = (byte)rb;
        }

        return count;
    }

    /// <summary>Read a byte from stream advancing position.</summary>
    /// <returns>byte read or -1 on end of stream.</returns>
    public override int ReadByte()
    {
        if (streamEnd)
        {
            return -1; // ok
        }

        var retChar = currentChar;
        switch (currentState)
        {
            case State.RandPartB:
                SetupRandPartB();
                break;

            case State.RandPartC:
                SetupRandPartC();
                break;

            case State.NoRandPartB:
                SetupNoRandPartB();
                break;

            case State.NoRandPartC:
                SetupNoRandPartC();
                break;

            case State.StartBlock:
            case State.NoRandPartA:
            case State.RandPartA:
                break;
        }

        return retChar;
    }

    /// <summary>Set the streams position. This operation is not supported and will throw a NotSupportedException.</summary>
    /// <param name="offset">A byte offset relative to the <paramref name="origin"/> parameter.</param>
    /// <param name="origin">A value of type <see cref="SeekOrigin"/> indicating the reference point used to obtain the new position.</param>
    /// <returns>The new position of the stream.</returns>
    /// <exception cref="NotSupportedException">Any access.</exception>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("BZip2InputStream Seek not supported");

    /// <summary>Sets the length of this stream to the given value. This operation is not supported and will throw a NotSupportedExceptionortedException.</summary>
    /// <param name="value">The new length for the stream.</param>
    /// <exception cref="NotSupportedException">Any access.</exception>
    public override void SetLength(long value) => throw new NotSupportedException("BZip2InputStream SetLength not supported");

    /// <summary>Writes a block of bytes to this stream using data from a buffer. This operation is not supported and will throw a NotSupportedException.</summary>
    /// <param name="buffer">The buffer to source data from.</param>
    /// <param name="offset">The offset to start obtaining data from.</param>
    /// <param name="count">The number of bytes of data to write.</param>
    /// <exception cref="NotSupportedException">Any access.</exception>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("BZip2InputStream Write not supported");

    /// <summary>Writes a byte to the current position in the file stream. This operation is not supported and will throw a NotSupportedException.</summary>
    /// <param name="value">The value to write.</param>
    /// <exception cref="NotSupportedException">Any access.</exception>
    public override void WriteByte(byte value) => throw new NotSupportedException("BZip2InputStream WriteByte not supported");

    #endregion Public Methods
}
