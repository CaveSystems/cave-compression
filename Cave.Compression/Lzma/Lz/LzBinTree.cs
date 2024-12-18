#nullable disable

using System;
using System.Diagnostics;

namespace Cave.Compression.Lzma.LZ;

sealed class LzBinTree : LzInWindow, ILzMatchFinder
{
    #region Private Fields

    const uint KBT2HashSize = 1 << 16;
    const uint KEmptyHashValue = 0;
    const uint KHash2Size = 1 << 10;
    const uint KHash3Offset = KHash2Size;
    const uint KHash3Size = 1 << 16;
    const uint KMaxValForNormalize = ((uint)1 << 31) - 1;
    const uint KStartMaxLen = 1;
    uint cutValue = 0xFF;
    uint cyclicBufferPos;
    uint cyclicBufferSize;
    uint[] hash;
    bool hashArray = true;
    uint hashMask;
    uint hashSizeSum;
    uint kFixHashSize = KHash2Size + KHash3Size;
    uint kMinMatchCheck = 4;
    uint kNumHashDirectBytes;
    uint matchMaxLen;
    uint[] son;

    #endregion Private Fields

    #region Private Methods

    void Normalize()
    {
        var subValue = Pos - cyclicBufferSize;
        NormalizeLinks(son, cyclicBufferSize * 2, subValue);
        NormalizeLinks(hash, hashSizeSum, subValue);
        ReduceOffsets((int)subValue);
    }

    void NormalizeLinks(uint[] items, uint numItems, uint subValue)
    {
        for (uint i = 0; i < numItems; i++)
        {
            var value = items[i];
            if (value <= subValue)
                value = KEmptyHashValue;
            else
                value -= subValue;
            items[i] = value;
        }
    }

    #endregion Private Methods

    #region Public Methods

    public void Create(uint historySize, uint keepAddBufferBefore, uint matchMaxLen, uint keepAddBufferAfter)
    {
        if (historySize > KMaxValForNormalize - 256)
            throw new Exception();
        cutValue = 16 + (matchMaxLen >> 1);

        var windowReservSize = ((historySize + keepAddBufferBefore +
                matchMaxLen + keepAddBufferAfter) / 2) + 256;

        base.Create(historySize + keepAddBufferBefore, matchMaxLen + keepAddBufferAfter, windowReservSize);

        this.matchMaxLen = matchMaxLen;

        var cyclicBufferSize = historySize + 1;
        if (this.cyclicBufferSize < cyclicBufferSize)
            son = new uint[(this.cyclicBufferSize = cyclicBufferSize) * 2];

        var hs = KBT2HashSize;

        if (hashArray)
        {
            hs = historySize - 1;
            hs |= hs >> 1;
            hs |= hs >> 2;
            hs |= hs >> 4;
            hs |= hs >> 8;
            hs >>= 1;
            hs |= 0xFFFF;
            if (hs > (1 << 24))
                hs >>= 1;
            hashMask = hs;
            hs++;
            hs += kFixHashSize;
        }
        if (hs != hashSizeSum)
            hash = new uint[hashSizeSum = hs];
    }

    public new byte GetIndexByte(int index) => base.GetIndexByte(index);

    public uint GetMatches(uint[] distances)
    {
        uint lenLimit;
        if (Pos + matchMaxLen <= StreamPos)
            lenLimit = matchMaxLen;
        else
        {
            lenLimit = StreamPos - Pos;
            if (lenLimit < kMinMatchCheck)
            {
                MovePos();
                return 0;
            }
        }

        uint offset = 0;
        var matchMinPos = (Pos > cyclicBufferSize) ? (Pos - cyclicBufferSize) : 0;
        var cur = BufferOffset + Pos;
        var maxLen = KStartMaxLen; // to avoid items for len < hashSize;
        uint hashValue, hash2Value = 0, hash3Value = 0;

        if (hashArray)
        {
            var temp = LzmaCrc32.Table[BufferBase[cur]] ^ BufferBase[cur + 1];
            hash2Value = temp & (KHash2Size - 1);
            temp ^= (uint)BufferBase[cur + 2] << 8;
            hash3Value = temp & (KHash3Size - 1);
            hashValue = (temp ^ (LzmaCrc32.Table[BufferBase[cur + 3]] << 5)) & hashMask;
        }
        else
            hashValue = BufferBase[cur] ^ ((uint)BufferBase[cur + 1] << 8);

        var curMatch = hash[kFixHashSize + hashValue];
        if (hashArray)
        {
            var curMatch2 = hash[hash2Value];
            var curMatch3 = hash[KHash3Offset + hash3Value];
            hash[hash2Value] = Pos;
            hash[KHash3Offset + hash3Value] = Pos;
            if (curMatch2 > matchMinPos)
                if (BufferBase[BufferOffset + curMatch2] == BufferBase[cur])
                {
                    distances[offset++] = maxLen = 2;
                    distances[offset++] = Pos - curMatch2 - 1;
                }
            if (curMatch3 > matchMinPos)
                if (BufferBase[BufferOffset + curMatch3] == BufferBase[cur])
                {
                    if (curMatch3 == curMatch2)
                        offset -= 2;
                    distances[offset++] = maxLen = 3;
                    distances[offset++] = Pos - curMatch3 - 1;
                    curMatch2 = curMatch3;
                }
            if (offset != 0 && curMatch2 == curMatch)
            {
                offset -= 2;
                maxLen = KStartMaxLen;
            }
        }

        hash[kFixHashSize + hashValue] = Pos;

        var ptr0 = (cyclicBufferPos << 1) + 1;
        var ptr1 = cyclicBufferPos << 1;

        uint len0, len1;
        len0 = len1 = kNumHashDirectBytes;

        if (kNumHashDirectBytes != 0)
        {
            if (curMatch > matchMinPos)
            {
                if (BufferBase[BufferOffset + curMatch + kNumHashDirectBytes] !=
                        BufferBase[cur + kNumHashDirectBytes])
                {
                    distances[offset++] = maxLen = kNumHashDirectBytes;
                    distances[offset++] = Pos - curMatch - 1;
                }
            }
        }

        var count = cutValue;

        while (true)
        {
            if (curMatch <= matchMinPos || count-- == 0)
            {
                son[ptr0] = son[ptr1] = KEmptyHashValue;
                break;
            }
            var delta = Pos - curMatch;
            var cyclicPos = ((delta <= cyclicBufferPos) ?
                        (cyclicBufferPos - delta) :
                        (cyclicBufferPos - delta + cyclicBufferSize)) << 1;

            var pby1 = BufferOffset + curMatch;
            var len = Math.Min(len0, len1);
            if (BufferBase[pby1 + len] == BufferBase[cur + len])
            {
                while (++len != lenLimit)
                    if (BufferBase[pby1 + len] != BufferBase[cur + len])
                        break;
                if (maxLen < len)
                {
                    distances[offset++] = maxLen = len;
                    distances[offset++] = delta - 1;
                    if (len == lenLimit)
                    {
                        son[ptr1] = son[cyclicPos];
                        son[ptr0] = son[cyclicPos + 1];
                        break;
                    }
                }
            }
            if (BufferBase[pby1 + len] < BufferBase[cur + len])
            {
                son[ptr1] = curMatch;
                ptr1 = cyclicPos + 1;
                curMatch = son[ptr1];
                len1 = len;
            }
            else
            {
                son[ptr0] = curMatch;
                ptr0 = cyclicPos;
                curMatch = son[ptr0];
                len0 = len;
            }
        }
        MovePos();
        return offset;
    }

    public new uint GetMatchLen(int index, uint distance, uint limit) => base.GetMatchLen(index, distance, limit);

    public new uint GetNumAvailableBytes() => base.GetNumAvailableBytes();

    public new void Init()
    {
        base.Init();

        /* net buffers cleared by default
        for (uint i = 0; i < hashSizeSum; i++)
        {
            hash[i] = kEmptyHashValue;
        }
        */

        cyclicBufferPos = 0;
        ReduceOffsets(-1);
    }

    public new void MovePos()
    {
        if (++cyclicBufferPos >= cyclicBufferSize)
            cyclicBufferPos = 0;
        base.MovePos();
        if (Pos == KMaxValForNormalize)
            Normalize();
    }

    public new void ReleaseStream() => base.ReleaseStream();

    public void SetCutValue(uint cutValue) => this.cutValue = cutValue;

    public new void SetStream(System.IO.Stream stream) => base.SetStream(stream);

    public void SetType(int numHashBytes)
    {
        hashArray = numHashBytes > 2;
        if (hashArray)
        {
            kNumHashDirectBytes = 0;
            kMinMatchCheck = 4;
            kFixHashSize = KHash2Size + KHash3Size;
        }
        else
        {
            kNumHashDirectBytes = 2;
            kMinMatchCheck = 2 + 1;
            kFixHashSize = 0;
        }
    }

    public void Skip(uint num)
    {
        do
        {
            uint lenLimit;
            if (Pos + matchMaxLen <= StreamPos)
                lenLimit = matchMaxLen;
            else
            {
                lenLimit = StreamPos - Pos;
                if (lenLimit < kMinMatchCheck)
                {
                    MovePos();
                    continue;
                }
            }

            var matchMinPos = (Pos > cyclicBufferSize) ? (Pos - cyclicBufferSize) : 0;
            var cur = BufferOffset + Pos;

            uint hashValue;

            if (hashArray)
            {
                var temp = LzmaCrc32.Table[BufferBase[cur]] ^ BufferBase[cur + 1];
                var hash2Value = temp & (KHash2Size - 1);
                hash[hash2Value] = Pos;
                temp ^= (uint)BufferBase[cur + 2] << 8;
                var hash3Value = temp & (KHash3Size - 1);
                hash[KHash3Offset + hash3Value] = Pos;
                hashValue = (temp ^ (LzmaCrc32.Table[BufferBase[cur + 3]] << 5)) & hashMask;
            }
            else
                hashValue = BufferBase[cur] ^ ((uint)BufferBase[cur + 1] << 8);

            var curMatch = hash[kFixHashSize + hashValue];
            hash[kFixHashSize + hashValue] = Pos;

            var ptr0 = (cyclicBufferPos << 1) + 1;
            var ptr1 = cyclicBufferPos << 1;

            uint len0, len1;
            len0 = len1 = kNumHashDirectBytes;

            var count = cutValue;
            while (true)
            {
                if (curMatch <= matchMinPos || count-- == 0)
                {
                    son[ptr0] = son[ptr1] = KEmptyHashValue;
                    break;
                }

                var delta = Pos - curMatch;
                var cyclicPos = ((delta <= cyclicBufferPos) ?
                            (cyclicBufferPos - delta) :
                            (cyclicBufferPos - delta + cyclicBufferSize)) << 1;

                var pby1 = BufferOffset + curMatch;
                var len = Math.Min(len0, len1);
                if (BufferBase[pby1 + len] == BufferBase[cur + len])
                {
                    while (++len != lenLimit)
                        if (BufferBase[pby1 + len] != BufferBase[cur + len])
                            break;
                    if (len == lenLimit)
                    {
                        son[ptr1] = son[cyclicPos];
                        son[ptr0] = son[cyclicPos + 1];
                        break;
                    }
                }
                if (BufferBase[pby1 + len] < BufferBase[cur + len])
                {
                    son[ptr1] = curMatch;
                    ptr1 = cyclicPos + 1;
                    curMatch = son[ptr1];
                    len1 = len;
                }
                else
                {
                    son[ptr0] = curMatch;
                    ptr0 = cyclicPos;
                    curMatch = son[ptr0];
                    len0 = len;
                }
            }
            MovePos();
        }
        while (--num != 0);
    }

    #endregion Public Methods
}
