#nullable disable

using System;
using Cave.Compression.Lzma.RangeCoder;
using Cave.Compression.Lzma.LZ;
using System.IO;
using Cave.Progress;

namespace Cave.Compression.Lzma;

public class LzmaEncoder
{
    #region Private Classes

    class LenEncoder
    {
        #region Private Fields

        RcBitEncoder choice = new RcBitEncoder();
        RcBitEncoder choice2 = new RcBitEncoder();
        RcBitTreeEncoder highCoder = new RcBitTreeEncoder(LzmaBase.kNumHighLenBits);
        RcBitTreeEncoder[] lowCoder = new RcBitTreeEncoder[LzmaBase.kNumPosStatesEncodingMax];
        RcBitTreeEncoder[] midCoder = new RcBitTreeEncoder[LzmaBase.kNumPosStatesEncodingMax];

        #endregion Private Fields

        #region Public Constructors

        public LenEncoder()
        {
            for (uint posState = 0; posState < LzmaBase.kNumPosStatesEncodingMax; posState++)
            {
                lowCoder[posState] = new RcBitTreeEncoder(LzmaBase.kNumLowLenBits);
                midCoder[posState] = new RcBitTreeEncoder(LzmaBase.kNumMidLenBits);
            }
        }

        #endregion Public Constructors

        #region Public Methods

        public void Encode(RcEncoder rangeEncoder, uint symbol, uint posState)
        {
            if (symbol < LzmaBase.kNumLowLenSymbols)
            {
                choice.Encode(rangeEncoder, 0);
                lowCoder[posState].Encode(rangeEncoder, symbol);
            }
            else
            {
                symbol -= LzmaBase.kNumLowLenSymbols;
                choice.Encode(rangeEncoder, 1);
                if (symbol < LzmaBase.kNumMidLenSymbols)
                {
                    choice2.Encode(rangeEncoder, 0);
                    midCoder[posState].Encode(rangeEncoder, symbol);
                }
                else
                {
                    choice2.Encode(rangeEncoder, 1);
                    highCoder.Encode(rangeEncoder, symbol - LzmaBase.kNumMidLenSymbols);
                }
            }
        }

        public void Init(uint numPosStates)
        {
            choice.Init();
            choice2.Init();
            for (uint posState = 0; posState < numPosStates; posState++)
            {
                lowCoder[posState].Init();
                midCoder[posState].Init();
            }
            highCoder.Init();
        }

        public void SetPrices(uint posState, uint numSymbols, uint[] prices, uint st)
        {
            var a0 = choice.GetPrice0();
            var a1 = choice.GetPrice1();
            var b0 = a1 + choice2.GetPrice0();
            var b1 = a1 + choice2.GetPrice1();
            uint i = 0;
            for (i = 0; i < LzmaBase.kNumLowLenSymbols; i++)
            {
                if (i >= numSymbols) return;
                prices[st + i] = a0 + lowCoder[posState].GetPrice(i);
            }
            for (; i < LzmaBase.kNumLowLenSymbols + LzmaBase.kNumMidLenSymbols; i++)
            {
                if (i >= numSymbols) return;
                prices[st + i] = b0 + midCoder[posState].GetPrice(i - LzmaBase.kNumLowLenSymbols);
            }
            for (; i < numSymbols; i++)
            {
                prices[st + i] = b1 + highCoder.GetPrice(i - LzmaBase.kNumLowLenSymbols - LzmaBase.kNumMidLenSymbols);
            }
        }

        #endregion Public Methods
    };

    class LenPriceTableEncoder : LenEncoder
    {
        #region Private Fields

        uint[] counters = new uint[LzmaBase.kNumPosStatesEncodingMax];
        uint[] prices = new uint[LzmaBase.kNumLenSymbols << LzmaBase.kNumPosStatesBitsEncodingMax];
        uint tableSize;

        #endregion Private Fields

        #region Private Methods

        void UpdateTable(uint posState)
        {
            SetPrices(posState, tableSize, prices, posState * LzmaBase.kNumLenSymbols);
            counters[posState] = tableSize;
        }

        #endregion Private Methods

        #region Public Methods

        public new void Encode(RcEncoder rangeEncoder, uint symbol, uint posState)
        {
            base.Encode(rangeEncoder, symbol, posState);
            if (--counters[posState] == 0) UpdateTable(posState);
        }

        public uint GetPrice(uint symbol, uint posState) => prices[(posState * LzmaBase.kNumLenSymbols) + symbol];

        public void SetTableSize(uint tableSize) => this.tableSize = tableSize;

        public void UpdateTables(uint numPosStates)
        {
            for (uint posState = 0; posState < numPosStates; posState++)
            {
                UpdateTable(posState);
            }
        }

        #endregion Public Methods
    }

    class LiteralEncoder
    {
        #region Private Fields

        Encoder2[] coders;

        int numPosBits;
        int numPrevBits;
        uint posMask;

        #endregion Private Fields

        #region Public Structs

        public struct Encoder2
        {
            #region Private Fields

            RcBitEncoder[] encoders;

            #endregion Private Fields

            #region Public Methods

            public void Create() => encoders = new RcBitEncoder[0x300];

            public void Encode(RcEncoder rangeEncoder, byte symbol)
            {
                uint context = 1;
                for (var i = 7; i >= 0; i--)
                {
                    var bit = (uint)((symbol >> i) & 1);
                    encoders[context].Encode(rangeEncoder, bit);
                    context = (context << 1) | bit;
                }
            }

            public void EncodeMatched(RcEncoder rangeEncoder, byte matchByte, byte symbol)
            {
                uint context = 1;
                var same = true;
                for (var i = 7; i >= 0; i--)
                {
                    var bit = (uint)((symbol >> i) & 1);
                    var state = context;
                    if (same)
                    {
                        var matchBit = (uint)((matchByte >> i) & 1);
                        state += (1 + matchBit) << 8;
                        same = matchBit == bit;
                    }
                    encoders[state].Encode(rangeEncoder, bit);
                    context = (context << 1) | bit;
                }
            }

            public uint GetPrice(bool matchMode, byte matchByte, byte symbol)
            {
                uint price = 0;
                uint context = 1;
                var i = 7;
                if (matchMode)
                {
                    for (; i >= 0; i--)
                    {
                        var matchBit = (uint)(matchByte >> i) & 1;
                        var bit = (uint)(symbol >> i) & 1;
                        price += encoders[((1 + matchBit) << 8) + context].GetPrice(bit);
                        context = (context << 1) | bit;
                        if (matchBit != bit)
                        {
                            i--;
                            break;
                        }
                    }
                }
                for (; i >= 0; i--)
                {
                    var bit = (uint)(symbol >> i) & 1;
                    price += encoders[context].GetPrice(bit);
                    context = (context << 1) | bit;
                }
                return price;
            }

            public void Init() { for (var i = 0; i < 0x300; i++) encoders[i].Init(); }

            #endregion Public Methods
        }

        #endregion Public Structs

        #region Public Methods

        public void Create(int numPosBits, int numPrevBits)
        {
            if (coders != null && this.numPrevBits == numPrevBits && this.numPosBits == numPosBits) return;
            this.numPosBits = numPosBits;
            posMask = ((uint)1 << numPosBits) - 1;
            this.numPrevBits = numPrevBits;
            var numStates = (uint)1 << (this.numPrevBits + this.numPosBits);
            coders = new Encoder2[numStates];
            for (uint i = 0; i < numStates; i++)
            {
                coders[i].Create();
            }
        }

        public Encoder2 GetSubCoder(uint pos, byte prevByte) => coders[((pos & posMask) << numPrevBits) + (uint)(prevByte >> (8 - numPrevBits))];

        public void Init()
        {
            var numStates = (uint)1 << (numPrevBits + numPosBits);
            for (uint i = 0; i < numStates; i++)
            {
                coders[i].Init();
            }
        }

        #endregion Public Methods
    }

    class Optimal
    {
        #region Public Fields

        public uint BackPrev;
        public uint BackPrev2;
        public uint Backs0;
        public uint Backs1;
        public uint Backs2;
        public uint Backs3;
        public uint PosPrev;
        public uint PosPrev2;
        public bool Prev1IsChar;
        public bool Prev2;
        public uint Price;
        public LzmaBase.State State;

        #endregion Public Fields

        #region Public Methods

        public bool IsShortRep() => BackPrev == 0;

        public void MakeAsChar() { BackPrev = 0xFFFFFFFF; Prev1IsChar = false; }

        public void MakeAsShortRep() { BackPrev = 0; ; Prev1IsChar = false; }

        #endregion Public Methods
    };

    #endregion Private Classes

    #region Private Fields

    const uint kIfinityPrice = 0xFFFFFFF;
    const uint kNumLenSpecSymbols = LzmaBase.kNumLowLenSymbols + LzmaBase.kNumMidLenSymbols;
    const uint kNumOpts = 1 << 12;
    static byte[] globalFastPos = new byte[1 << 11];
    uint additionalOffset;
    uint alignPriceCount;
    uint[] alignPrices = new uint[LzmaBase.kAlignTableSize];
    uint dictionarySize;
    uint dictionarySizePrev = 0xFFFFFFFF;
    uint[] distancesPrices = new uint[LzmaBase.kNumFullDistances << LzmaBase.kNumLenToPosStatesBits];
    uint distTableSize = kDefaultDictionaryLogSize * 2;
    bool finished;
    Stream inStream;
    RcBitEncoder[] isMatch = new RcBitEncoder[LzmaBase.kNumStates << LzmaBase.kNumPosStatesBitsMax];
    RcBitEncoder[] isRep = new RcBitEncoder[LzmaBase.kNumStates];
    RcBitEncoder[] isRep0Long = new RcBitEncoder[LzmaBase.kNumStates << LzmaBase.kNumPosStatesBitsMax];
    RcBitEncoder[] isRepG0 = new RcBitEncoder[LzmaBase.kNumStates];
    RcBitEncoder[] isRepG1 = new RcBitEncoder[LzmaBase.kNumStates];
    RcBitEncoder[] isRepG2 = new RcBitEncoder[LzmaBase.kNumStates];
    LenPriceTableEncoder lenEncoder = new LenPriceTableEncoder();
    LiteralEncoder literalEncoder = new LiteralEncoder();
    uint longestMatchLength;
    bool longestMatchWasFound;
    uint[] matchDistances = new uint[(LzmaBase.kMatchMaxLen * 2) + 2];
    ILzMatchFinder matchFinder = null;
    LzmaMatchFinderType matchFinderType = LzmaMatchFinderType.BT4;
    uint matchPriceCount;
    bool needReleaseMFStream;
    long nowPos64;
    uint numDistancePairs;
    uint numFastBytes = kNumFastBytesDefault;
    uint numFastBytesPrev = 0xFFFFFFFF;
    int numLiteralContextBits = 3;
    int numLiteralPosStateBits = 0;
    Optimal[] optimum = new Optimal[kNumOpts];
    uint optimumCurrentIndex;
    uint optimumEndIndex;
    RcBitTreeEncoder posAlignEncoder = new RcBitTreeEncoder(LzmaBase.kNumAlignBits);
    RcBitEncoder[] posEncoders = new RcBitEncoder[LzmaBase.kNumFullDistances - LzmaBase.kEndPosModelIndex];
    RcBitTreeEncoder[] posSlotEncoder = new RcBitTreeEncoder[LzmaBase.kNumLenToPosStates];
    uint[] posSlotPrices = new uint[1 << (LzmaBase.kNumPosSlotBits + LzmaBase.kNumLenToPosStatesBits)];
    int posStateBits = 2;
    uint posStateMask = 4 - 1;
    byte previousByte;
    byte[] properties = new byte[kPropSize];
    RcEncoder rangeEncoder = new RcEncoder();
    uint[] repDistances = new uint[LzmaBase.kNumRepDistances];
    uint[] repLens = new uint[LzmaBase.kNumRepDistances];
    LenPriceTableEncoder repMatchLenEncoder = new LenPriceTableEncoder();
    uint[] reps = new uint[LzmaBase.kNumRepDistances];
    LzmaBase.State state = new LzmaBase.State();
    uint[] tempPrices = new uint[LzmaBase.kNumFullDistances];
    uint trainSize = 0;
    bool writeEndMark = false;

    #endregion Private Fields

    #region Private Methods

    static uint GetPosSlot(uint pos)
    {
        if (pos < 1 << 11)
            return globalFastPos[pos];
        if (pos < 1 << 21)
            return (uint)(globalFastPos[pos >> 10] + 20);
        return (uint)(globalFastPos[pos >> 20] + 40);
    }

    static uint GetPosSlot2(uint pos)
    {
        if (pos < 1 << 17)
            return (uint)(globalFastPos[pos >> 6] + 12);
        if (pos < 1 << 27)
            return (uint)(globalFastPos[pos >> 16] + 32);
        return (uint)(globalFastPos[pos >> 26] + 52);
    }

    uint Backward(out uint backRes, uint cur)
    {
        optimumEndIndex = cur;
        var posMem = optimum[cur].PosPrev;
        var backMem = optimum[cur].BackPrev;
        do
        {
            if (optimum[cur].Prev1IsChar)
            {
                optimum[posMem].MakeAsChar();
                optimum[posMem].PosPrev = posMem - 1;
                if (optimum[cur].Prev2)
                {
                    optimum[posMem - 1].Prev1IsChar = false;
                    optimum[posMem - 1].PosPrev = optimum[cur].PosPrev2;
                    optimum[posMem - 1].BackPrev = optimum[cur].BackPrev2;
                }
            }
            var posPrev = posMem;
            var backCur = backMem;

            backMem = optimum[posPrev].BackPrev;
            posMem = optimum[posPrev].PosPrev;

            optimum[posPrev].BackPrev = backCur;
            optimum[posPrev].PosPrev = cur;
            cur = posPrev;
        }
        while (cur > 0);
        backRes = optimum[0].BackPrev;
        optimumCurrentIndex = optimum[0].PosPrev;
        return optimumCurrentIndex;
    }

    void BaseInit()
    {
        state.Init();
        previousByte = 0;
        for (uint i = 0; i < LzmaBase.kNumRepDistances; i++)
            repDistances[i] = 0;
    }

    bool ChangePair(uint smallDist, uint bigDist)
    {
        const int kDif = 7;
        return smallDist < (uint)1 << (32 - kDif) && bigDist >= smallDist << kDif;
    }

    void CodeOneBlock(out long inSize, out long outSize, out bool finished)
    {
        inSize = 0;
        outSize = 0;
        finished = true;

        if (inStream != null)
        {
            matchFinder.SetStream(inStream);
            matchFinder.Init();
            needReleaseMFStream = true;
            inStream = null;
            if (trainSize > 0)
                matchFinder.Skip(trainSize);
        }

        if (this.finished)
            return;
        this.finished = true;

        var progressPosValuePrev = nowPos64;
        if (nowPos64 == 0)
        {
            if (matchFinder.GetNumAvailableBytes() == 0)
            {
                Flush((uint)nowPos64);
                return;
            }
            uint len, numDistancePairs; // it's not used
            ReadMatchDistances(out len, out numDistancePairs);
            var posState = (uint)nowPos64 & posStateMask;
            isMatch[(state.Index << LzmaBase.kNumPosStatesBitsMax) + posState].Encode(rangeEncoder, 0);
            state.UpdateChar();
            var curByte = matchFinder.GetIndexByte((int)(0 - additionalOffset));
            literalEncoder.GetSubCoder((uint)nowPos64, previousByte).Encode(rangeEncoder, curByte);
            previousByte = curByte;
            additionalOffset--;
            nowPos64++;
        }
        if (matchFinder.GetNumAvailableBytes() == 0)
        {
            Flush((uint)nowPos64);
            return;
        }
        while (true)
        {
            uint pos;
            var len = GetOptimum((uint)nowPos64, out pos);

            var posState = (uint)nowPos64 & posStateMask;
            var complexState = (state.Index << LzmaBase.kNumPosStatesBitsMax) + posState;
            if (len == 1 && pos == 0xFFFFFFFF)
            {
                isMatch[complexState].Encode(rangeEncoder, 0);
                var curByte = matchFinder.GetIndexByte((int)(0 - additionalOffset));
                var subCoder = literalEncoder.GetSubCoder((uint)nowPos64, previousByte);
                if (!state.IsCharState())
                {
                    var matchByte = matchFinder.GetIndexByte((int)(0 - repDistances[0] - 1 - additionalOffset));
                    subCoder.EncodeMatched(rangeEncoder, matchByte, curByte);
                }
                else
                    subCoder.Encode(rangeEncoder, curByte);
                previousByte = curByte;
                state.UpdateChar();
            }
            else
            {
                isMatch[complexState].Encode(rangeEncoder, 1);
                if (pos < LzmaBase.kNumRepDistances)
                {
                    isRep[state.Index].Encode(rangeEncoder, 1);
                    if (pos == 0)
                    {
                        isRepG0[state.Index].Encode(rangeEncoder, 0);
                        if (len == 1)
                            isRep0Long[complexState].Encode(rangeEncoder, 0);
                        else
                            isRep0Long[complexState].Encode(rangeEncoder, 1);
                    }
                    else
                    {
                        isRepG0[state.Index].Encode(rangeEncoder, 1);
                        if (pos == 1)
                            isRepG1[state.Index].Encode(rangeEncoder, 0);
                        else
                        {
                            isRepG1[state.Index].Encode(rangeEncoder, 1);
                            isRepG2[state.Index].Encode(rangeEncoder, pos - 2);
                        }
                    }
                    if (len == 1)
                        state.UpdateShortRep();
                    else
                    {
                        repMatchLenEncoder.Encode(rangeEncoder, len - LzmaBase.kMatchMinLen, posState);
                        state.UpdateRep();
                    }
                    var distance = repDistances[pos];
                    if (pos != 0)
                    {
                        for (var i = pos; i >= 1; i--)
                            repDistances[i] = repDistances[i - 1];
                        repDistances[0] = distance;
                    }
                }
                else
                {
                    isRep[state.Index].Encode(rangeEncoder, 0);
                    state.UpdateMatch();
                    lenEncoder.Encode(rangeEncoder, len - LzmaBase.kMatchMinLen, posState);
                    pos -= LzmaBase.kNumRepDistances;
                    var posSlot = GetPosSlot(pos);
                    var lenToPosState = LzmaBase.GetLenToPosState(len);
                    posSlotEncoder[lenToPosState].Encode(rangeEncoder, posSlot);

                    if (posSlot >= LzmaBase.kStartPosModelIndex)
                    {
                        var footerBits = (int)((posSlot >> 1) - 1);
                        var baseVal = (2 | (posSlot & 1)) << footerBits;
                        var posReduced = pos - baseVal;

                        if (posSlot < LzmaBase.kEndPosModelIndex)
                            RangeCoder.RcBitTreeEncoder.ReverseEncode(posEncoders,
                                    baseVal - posSlot - 1, rangeEncoder, footerBits, posReduced);
                        else
                        {
                            rangeEncoder.EncodeDirectBits(posReduced >> LzmaBase.kNumAlignBits, footerBits - LzmaBase.kNumAlignBits);
                            posAlignEncoder.ReverseEncode(rangeEncoder, posReduced & LzmaBase.kAlignMask);
                            alignPriceCount++;
                        }
                    }
                    var distance = pos;
                    for (var i = LzmaBase.kNumRepDistances - 1; i >= 1; i--)
                        repDistances[i] = repDistances[i - 1];
                    repDistances[0] = distance;
                    matchPriceCount++;
                }
                previousByte = matchFinder.GetIndexByte((int)(len - 1 - additionalOffset));
            }
            additionalOffset -= len;
            nowPos64 += len;
            if (additionalOffset == 0)
            {
                // if (!_fastMode)
                if (matchPriceCount >= 1 << 7)
                    FillDistancesPrices();
                if (alignPriceCount >= LzmaBase.kAlignTableSize)
                    FillAlignPrices();
                inSize = nowPos64;
                outSize = rangeEncoder.GetProcessedSizeAdd();
                if (matchFinder.GetNumAvailableBytes() == 0)
                {
                    Flush((uint)nowPos64);
                    return;
                }

                if (nowPos64 - progressPosValuePrev >= 1 << 12)
                {
                    this.finished = false;
                    finished = false;
                    return;
                }
            }
        }
    }

    void Create()
    {
        if (matchFinder == null)
        {
            var bt = new LzBinTree();
            var numHashBytes = 4;
            if (matchFinderType == LzmaMatchFinderType.BT2)
                numHashBytes = 2;
            bt.SetType(numHashBytes);
            matchFinder = bt;
        }
        literalEncoder.Create(numLiteralPosStateBits, numLiteralContextBits);

        if (dictionarySize == dictionarySizePrev && numFastBytesPrev == numFastBytes)
            return;
        matchFinder.Create(dictionarySize, kNumOpts, numFastBytes, LzmaBase.kMatchMaxLen + 1);
        dictionarySizePrev = dictionarySize;
        numFastBytesPrev = numFastBytes;
    }

    void FillAlignPrices()
    {
        for (uint i = 0; i < LzmaBase.kAlignTableSize; i++)
            alignPrices[i] = posAlignEncoder.ReverseGetPrice(i);
        alignPriceCount = 0;
    }

    void FillDistancesPrices()
    {
        for (var i = LzmaBase.kStartPosModelIndex; i < LzmaBase.kNumFullDistances; i++)
        {
            var posSlot = GetPosSlot(i);
            var footerBits = (int)((posSlot >> 1) - 1);
            var baseVal = (2 | (posSlot & 1)) << footerBits;
            tempPrices[i] = RcBitTreeEncoder.ReverseGetPrice(posEncoders,
                baseVal - posSlot - 1, footerBits, i - baseVal);
        }

        for (uint lenToPosState = 0; lenToPosState < LzmaBase.kNumLenToPosStates; lenToPosState++)
        {
            uint posSlot;
            var encoder = posSlotEncoder[lenToPosState];

            var st = lenToPosState << LzmaBase.kNumPosSlotBits;
            for (posSlot = 0; posSlot < distTableSize; posSlot++)
                posSlotPrices[st + posSlot] = encoder.GetPrice(posSlot);
            for (posSlot = LzmaBase.kEndPosModelIndex; posSlot < distTableSize; posSlot++)
                posSlotPrices[st + posSlot] += ((posSlot >> 1) - 1 - LzmaBase.kNumAlignBits) << RangeCoder.RcBitEncoder.kNumBitPriceShiftBits;

            var st2 = lenToPosState * LzmaBase.kNumFullDistances;
            uint i;
            for (i = 0; i < LzmaBase.kStartPosModelIndex; i++)
                distancesPrices[st2 + i] = posSlotPrices[st + i];
            for (; i < LzmaBase.kNumFullDistances; i++)
                distancesPrices[st2 + i] = posSlotPrices[st + GetPosSlot(i)] + tempPrices[i];
        }
        matchPriceCount = 0;
    }

    void Flush(uint nowPos)
    {
        ReleaseMFStream();
        WriteEndMarker(nowPos & posStateMask);
        rangeEncoder.FlushData();
        rangeEncoder.FlushStream();
    }

    uint GetOptimum(uint position, out uint backRes)
    {
        if (optimumEndIndex != optimumCurrentIndex)
        {
            var lenRes = optimum[optimumCurrentIndex].PosPrev - optimumCurrentIndex;
            backRes = optimum[optimumCurrentIndex].BackPrev;
            optimumCurrentIndex = optimum[optimumCurrentIndex].PosPrev;
            return lenRes;
        }
        optimumCurrentIndex = optimumEndIndex = 0;

        uint lenMain, numDistancePairs;
        if (!longestMatchWasFound)
        {
            ReadMatchDistances(out lenMain, out numDistancePairs);
        }
        else
        {
            lenMain = longestMatchLength;
            numDistancePairs = this.numDistancePairs;
            longestMatchWasFound = false;
        }

        var numAvailableBytes = matchFinder.GetNumAvailableBytes() + 1;
        if (numAvailableBytes < 2)
        {
            backRes = 0xFFFFFFFF;
            return 1;
        }
        if (numAvailableBytes > LzmaBase.kMatchMaxLen)
            numAvailableBytes = LzmaBase.kMatchMaxLen;

        uint repMaxIndex = 0;
        uint i;
        for (i = 0; i < LzmaBase.kNumRepDistances; i++)
        {
            reps[i] = repDistances[i];
            repLens[i] = matchFinder.GetMatchLen(0 - 1, reps[i], LzmaBase.kMatchMaxLen);
            if (repLens[i] > repLens[repMaxIndex])
                repMaxIndex = i;
        }
        if (repLens[repMaxIndex] >= numFastBytes)
        {
            backRes = repMaxIndex;
            var lenRes = repLens[repMaxIndex];
            MovePos(lenRes - 1);
            return lenRes;
        }

        if (lenMain >= numFastBytes)
        {
            backRes = matchDistances[numDistancePairs - 1] + LzmaBase.kNumRepDistances;
            MovePos(lenMain - 1);
            return lenMain;
        }

        var currentByte = matchFinder.GetIndexByte(0 - 1);
        var matchByte = matchFinder.GetIndexByte((int)(0 - repDistances[0] - 1 - 1));

        if (lenMain < 2 && currentByte != matchByte && repLens[repMaxIndex] < 2)
        {
            backRes = 0xFFFFFFFF;
            return 1;
        }

        optimum[0].State = state;

        var posState = position & posStateMask;

        optimum[1].Price = isMatch[(state.Index << LzmaBase.kNumPosStatesBitsMax) + posState].GetPrice0() +
                literalEncoder.GetSubCoder(position, previousByte).GetPrice(!state.IsCharState(), matchByte, currentByte);
        optimum[1].MakeAsChar();

        var matchPrice = isMatch[(state.Index << LzmaBase.kNumPosStatesBitsMax) + posState].GetPrice1();
        var repMatchPrice = matchPrice + isRep[state.Index].GetPrice1();

        if (matchByte == currentByte)
        {
            var shortRepPrice = repMatchPrice + GetRepLen1Price(state, posState);
            if (shortRepPrice < optimum[1].Price)
            {
                optimum[1].Price = shortRepPrice;
                optimum[1].MakeAsShortRep();
            }
        }

        var lenEnd = lenMain >= repLens[repMaxIndex] ? lenMain : repLens[repMaxIndex];

        if (lenEnd < 2)
        {
            backRes = optimum[1].BackPrev;
            return 1;
        }

        optimum[1].PosPrev = 0;

        optimum[0].Backs0 = reps[0];
        optimum[0].Backs1 = reps[1];
        optimum[0].Backs2 = reps[2];
        optimum[0].Backs3 = reps[3];

        var len = lenEnd;
        do
            optimum[len--].Price = kIfinityPrice;
        while (len >= 2);

        for (i = 0; i < LzmaBase.kNumRepDistances; i++)
        {
            var repLen = repLens[i];
            if (repLen < 2)
                continue;
            var price = repMatchPrice + GetPureRepPrice(i, state, posState);
            do
            {
                var curAndLenPrice = price + repMatchLenEncoder.GetPrice(repLen - 2, posState);
                var optimum = this.optimum[repLen];
                if (curAndLenPrice < optimum.Price)
                {
                    optimum.Price = curAndLenPrice;
                    optimum.PosPrev = 0;
                    optimum.BackPrev = i;
                    optimum.Prev1IsChar = false;
                }
            }
            while (--repLen >= 2);
        }

        var normalMatchPrice = matchPrice + isRep[state.Index].GetPrice0();

        len = repLens[0] >= 2 ? repLens[0] + 1 : 2;
        if (len <= lenMain)
        {
            uint offs = 0;
            while (len > matchDistances[offs])
                offs += 2;
            for (; ; len++)
            {
                var distance = matchDistances[offs + 1];
                var curAndLenPrice = normalMatchPrice + GetPosLenPrice(distance, len, posState);
                var optimum = this.optimum[len];
                if (curAndLenPrice < optimum.Price)
                {
                    optimum.Price = curAndLenPrice;
                    optimum.PosPrev = 0;
                    optimum.BackPrev = distance + LzmaBase.kNumRepDistances;
                    optimum.Prev1IsChar = false;
                }
                if (len == matchDistances[offs])
                {
                    offs += 2;
                    if (offs == numDistancePairs)
                        break;
                }
            }
        }

        uint cur = 0;

        while (true)
        {
            cur++;
            if (cur == lenEnd)
                return Backward(out backRes, cur);
            uint newLen;
            ReadMatchDistances(out newLen, out numDistancePairs);
            if (newLen >= numFastBytes)
            {
                this.numDistancePairs = numDistancePairs;
                longestMatchLength = newLen;
                longestMatchWasFound = true;
                return Backward(out backRes, cur);
            }
            position++;
            var posPrev = optimum[cur].PosPrev;
            LzmaBase.State state;
            if (optimum[cur].Prev1IsChar)
            {
                posPrev--;
                if (optimum[cur].Prev2)
                {
                    state = optimum[optimum[cur].PosPrev2].State;
                    if (optimum[cur].BackPrev2 < LzmaBase.kNumRepDistances)
                        state.UpdateRep();
                    else
                        state.UpdateMatch();
                }
                else
                    state = optimum[posPrev].State;
                state.UpdateChar();
            }
            else
                state = optimum[posPrev].State;
            if (posPrev == cur - 1)
            {
                if (optimum[cur].IsShortRep())
                    state.UpdateShortRep();
                else
                    state.UpdateChar();
            }
            else
            {
                uint pos;
                if (optimum[cur].Prev1IsChar && optimum[cur].Prev2)
                {
                    posPrev = optimum[cur].PosPrev2;
                    pos = optimum[cur].BackPrev2;
                    state.UpdateRep();
                }
                else
                {
                    pos = optimum[cur].BackPrev;
                    if (pos < LzmaBase.kNumRepDistances)
                        state.UpdateRep();
                    else
                        state.UpdateMatch();
                }
                var opt = optimum[posPrev];
                if (pos < LzmaBase.kNumRepDistances)
                {
                    if (pos == 0)
                    {
                        reps[0] = opt.Backs0;
                        reps[1] = opt.Backs1;
                        reps[2] = opt.Backs2;
                        reps[3] = opt.Backs3;
                    }
                    else if (pos == 1)
                    {
                        reps[0] = opt.Backs1;
                        reps[1] = opt.Backs0;
                        reps[2] = opt.Backs2;
                        reps[3] = opt.Backs3;
                    }
                    else if (pos == 2)
                    {
                        reps[0] = opt.Backs2;
                        reps[1] = opt.Backs0;
                        reps[2] = opt.Backs1;
                        reps[3] = opt.Backs3;
                    }
                    else
                    {
                        reps[0] = opt.Backs3;
                        reps[1] = opt.Backs0;
                        reps[2] = opt.Backs1;
                        reps[3] = opt.Backs2;
                    }
                }
                else
                {
                    reps[0] = pos - LzmaBase.kNumRepDistances;
                    reps[1] = opt.Backs0;
                    reps[2] = opt.Backs1;
                    reps[3] = opt.Backs2;
                }
            }
            optimum[cur].State = state;
            optimum[cur].Backs0 = reps[0];
            optimum[cur].Backs1 = reps[1];
            optimum[cur].Backs2 = reps[2];
            optimum[cur].Backs3 = reps[3];
            var curPrice = optimum[cur].Price;

            currentByte = matchFinder.GetIndexByte(0 - 1);
            matchByte = matchFinder.GetIndexByte((int)(0 - reps[0] - 1 - 1));

            posState = position & posStateMask;

            var curAnd1Price = curPrice +
                isMatch[(state.Index << LzmaBase.kNumPosStatesBitsMax) + posState].GetPrice0() +
                literalEncoder.GetSubCoder(position, matchFinder.GetIndexByte(0 - 2)).
                GetPrice(!state.IsCharState(), matchByte, currentByte);

            var nextOptimum = optimum[cur + 1];

            var nextIsChar = false;
            if (curAnd1Price < nextOptimum.Price)
            {
                nextOptimum.Price = curAnd1Price;
                nextOptimum.PosPrev = cur;
                nextOptimum.MakeAsChar();
                nextIsChar = true;
            }

            matchPrice = curPrice + isMatch[(state.Index << LzmaBase.kNumPosStatesBitsMax) + posState].GetPrice1();
            repMatchPrice = matchPrice + isRep[state.Index].GetPrice1();

            if (matchByte == currentByte &&
                !(nextOptimum.PosPrev < cur && nextOptimum.BackPrev == 0))
            {
                var shortRepPrice = repMatchPrice + GetRepLen1Price(state, posState);
                if (shortRepPrice <= nextOptimum.Price)
                {
                    nextOptimum.Price = shortRepPrice;
                    nextOptimum.PosPrev = cur;
                    nextOptimum.MakeAsShortRep();
                    nextIsChar = true;
                }
            }

            var numAvailableBytesFull = matchFinder.GetNumAvailableBytes() + 1;
            numAvailableBytesFull = Math.Min(kNumOpts - 1 - cur, numAvailableBytesFull);
            numAvailableBytes = numAvailableBytesFull;

            if (numAvailableBytes < 2)
                continue;
            if (numAvailableBytes > numFastBytes)
                numAvailableBytes = numFastBytes;
            if (!nextIsChar && matchByte != currentByte)
            {
                // try Literal + rep0
                var t = Math.Min(numAvailableBytesFull - 1, numFastBytes);
                var lenTest2 = matchFinder.GetMatchLen(0, reps[0], t);
                if (lenTest2 >= 2)
                {
                    var state2 = state;
                    state2.UpdateChar();
                    var posStateNext = (position + 1) & posStateMask;
                    var nextRepMatchPrice = curAnd1Price +
                        isMatch[(state2.Index << LzmaBase.kNumPosStatesBitsMax) + posStateNext].GetPrice1() +
                        isRep[state2.Index].GetPrice1();
                    {
                        var offset = cur + 1 + lenTest2;
                        while (lenEnd < offset)
                            this.optimum[++lenEnd].Price = kIfinityPrice;
                        var curAndLenPrice = nextRepMatchPrice + GetRepPrice(
                            0, lenTest2, state2, posStateNext);
                        var optimum = this.optimum[offset];
                        if (curAndLenPrice < optimum.Price)
                        {
                            optimum.Price = curAndLenPrice;
                            optimum.PosPrev = cur + 1;
                            optimum.BackPrev = 0;
                            optimum.Prev1IsChar = true;
                            optimum.Prev2 = false;
                        }
                    }
                }
            }

            uint startLen = 2; // speed optimization

            for (uint repIndex = 0; repIndex < LzmaBase.kNumRepDistances; repIndex++)
            {
                var lenTest = matchFinder.GetMatchLen(0 - 1, reps[repIndex], numAvailableBytes);
                if (lenTest < 2)
                    continue;
                var lenTestTemp = lenTest;
                do
                {
                    while (lenEnd < cur + lenTest)
                        this.optimum[++lenEnd].Price = kIfinityPrice;
                    var curAndLenPrice = repMatchPrice + GetRepPrice(repIndex, lenTest, state, posState);
                    var optimum = this.optimum[cur + lenTest];
                    if (curAndLenPrice < optimum.Price)
                    {
                        optimum.Price = curAndLenPrice;
                        optimum.PosPrev = cur;
                        optimum.BackPrev = repIndex;
                        optimum.Prev1IsChar = false;
                    }
                }
                while (--lenTest >= 2);
                lenTest = lenTestTemp;

                if (repIndex == 0)
                    startLen = lenTest + 1;

                // if (_maxMode)
                if (lenTest < numAvailableBytesFull)
                {
                    var t = Math.Min(numAvailableBytesFull - 1 - lenTest, numFastBytes);
                    var lenTest2 = matchFinder.GetMatchLen((int)lenTest, reps[repIndex], t);
                    if (lenTest2 >= 2)
                    {
                        var state2 = state;
                        state2.UpdateRep();
                        var posStateNext = (position + lenTest) & posStateMask;
                        var curAndLenCharPrice =
                                repMatchPrice + GetRepPrice(repIndex, lenTest, state, posState) +
                                isMatch[(state2.Index << LzmaBase.kNumPosStatesBitsMax) + posStateNext].GetPrice0() +
                                literalEncoder.GetSubCoder(position + lenTest,
                                matchFinder.GetIndexByte((int)lenTest - 1 - 1)).GetPrice(true,
                                matchFinder.GetIndexByte((int)lenTest - 1 - (int)(reps[repIndex] + 1)),
                                matchFinder.GetIndexByte((int)lenTest - 1));
                        state2.UpdateChar();
                        posStateNext = (position + lenTest + 1) & posStateMask;
                        var nextMatchPrice = curAndLenCharPrice + isMatch[(state2.Index << LzmaBase.kNumPosStatesBitsMax) + posStateNext].GetPrice1();
                        var nextRepMatchPrice = nextMatchPrice + isRep[state2.Index].GetPrice1();

                        // for(; lenTest2 >= 2; lenTest2--)
                        {
                            var offset = lenTest + 1 + lenTest2;
                            while (lenEnd < cur + offset)
                                this.optimum[++lenEnd].Price = kIfinityPrice;
                            var curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                            var optimum = this.optimum[cur + offset];
                            if (curAndLenPrice < optimum.Price)
                            {
                                optimum.Price = curAndLenPrice;
                                optimum.PosPrev = cur + lenTest + 1;
                                optimum.BackPrev = 0;
                                optimum.Prev1IsChar = true;
                                optimum.Prev2 = true;
                                optimum.PosPrev2 = cur;
                                optimum.BackPrev2 = repIndex;
                            }
                        }
                    }
                }
            }

            if (newLen > numAvailableBytes)
            {
                newLen = numAvailableBytes;
                for (numDistancePairs = 0; newLen > matchDistances[numDistancePairs]; numDistancePairs += 2) ;
                matchDistances[numDistancePairs] = newLen;
                numDistancePairs += 2;
            }
            if (newLen >= startLen)
            {
                normalMatchPrice = matchPrice + isRep[state.Index].GetPrice0();
                while (lenEnd < cur + newLen)
                    optimum[++lenEnd].Price = kIfinityPrice;

                uint offs = 0;
                while (startLen > matchDistances[offs])
                    offs += 2;

                for (var lenTest = startLen; ; lenTest++)
                {
                    var curBack = matchDistances[offs + 1];
                    var curAndLenPrice = normalMatchPrice + GetPosLenPrice(curBack, lenTest, posState);
                    var optimum = this.optimum[cur + lenTest];
                    if (curAndLenPrice < optimum.Price)
                    {
                        optimum.Price = curAndLenPrice;
                        optimum.PosPrev = cur;
                        optimum.BackPrev = curBack + LzmaBase.kNumRepDistances;
                        optimum.Prev1IsChar = false;
                    }

                    if (lenTest == matchDistances[offs])
                    {
                        if (lenTest < numAvailableBytesFull)
                        {
                            var t = Math.Min(numAvailableBytesFull - 1 - lenTest, numFastBytes);
                            var lenTest2 = matchFinder.GetMatchLen((int)lenTest, curBack, t);
                            if (lenTest2 >= 2)
                            {
                                var state2 = state;
                                state2.UpdateMatch();
                                var posStateNext = (position + lenTest) & posStateMask;
                                var curAndLenCharPrice = curAndLenPrice +
                                    isMatch[(state2.Index << LzmaBase.kNumPosStatesBitsMax) + posStateNext].GetPrice0() +
                                    literalEncoder.GetSubCoder(position + lenTest,
                                    matchFinder.GetIndexByte((int)lenTest - 1 - 1)).
                                    GetPrice(true,
                                    matchFinder.GetIndexByte((int)lenTest - (int)(curBack + 1) - 1),
                                    matchFinder.GetIndexByte((int)lenTest - 1));
                                state2.UpdateChar();
                                posStateNext = (position + lenTest + 1) & posStateMask;
                                var nextMatchPrice = curAndLenCharPrice + isMatch[(state2.Index << LzmaBase.kNumPosStatesBitsMax) + posStateNext].GetPrice1();
                                var nextRepMatchPrice = nextMatchPrice + isRep[state2.Index].GetPrice1();

                                var offset = lenTest + 1 + lenTest2;
                                while (lenEnd < cur + offset)
                                    this.optimum[++lenEnd].Price = kIfinityPrice;
                                curAndLenPrice = nextRepMatchPrice + GetRepPrice(0, lenTest2, state2, posStateNext);
                                optimum = this.optimum[cur + offset];
                                if (curAndLenPrice < optimum.Price)
                                {
                                    optimum.Price = curAndLenPrice;
                                    optimum.PosPrev = cur + lenTest + 1;
                                    optimum.BackPrev = 0;
                                    optimum.Prev1IsChar = true;
                                    optimum.Prev2 = true;
                                    optimum.PosPrev2 = cur;
                                    optimum.BackPrev2 = curBack + LzmaBase.kNumRepDistances;
                                }
                            }
                        }
                        offs += 2;
                        if (offs == numDistancePairs)
                            break;
                    }
                }
            }
        }
    }

    uint GetPosLenPrice(uint pos, uint len, uint posState)
    {
        uint price;
        var lenToPosState = LzmaBase.GetLenToPosState(len);
        if (pos < LzmaBase.kNumFullDistances)
            price = distancesPrices[(lenToPosState * LzmaBase.kNumFullDistances) + pos];
        else
            price = posSlotPrices[(lenToPosState << LzmaBase.kNumPosSlotBits) + GetPosSlot2(pos)] +
                alignPrices[pos & LzmaBase.kAlignMask];
        return price + lenEncoder.GetPrice(len - LzmaBase.kMatchMinLen, posState);
    }

    uint GetPureRepPrice(uint repIndex, LzmaBase.State state, uint posState)
    {
        uint price;
        if (repIndex == 0)
        {
            price = isRepG0[state.Index].GetPrice0();
            price += isRep0Long[(state.Index << LzmaBase.kNumPosStatesBitsMax) + posState].GetPrice1();
        }
        else
        {
            price = isRepG0[state.Index].GetPrice1();
            if (repIndex == 1)
                price += isRepG1[state.Index].GetPrice0();
            else
            {
                price += isRepG1[state.Index].GetPrice1();
                price += isRepG2[state.Index].GetPrice(repIndex - 2);
            }
        }
        return price;
    }

    uint GetRepLen1Price(LzmaBase.State state, uint posState) => isRepG0[state.Index].GetPrice0() + isRep0Long[(state.Index << LzmaBase.kNumPosStatesBitsMax) + posState].GetPrice0();

    uint GetRepPrice(uint repIndex, uint len, LzmaBase.State state, uint posState)
    {
        var price = repMatchLenEncoder.GetPrice(len - LzmaBase.kMatchMinLen, posState);
        return price + GetPureRepPrice(repIndex, state, posState);
    }

    void Init()
    {
        BaseInit();
        rangeEncoder.Init();

        uint i;
        for (i = 0; i < LzmaBase.kNumStates; i++)
        {
            for (uint j = 0; j <= posStateMask; j++)
            {
                var complexState = (i << LzmaBase.kNumPosStatesBitsMax) + j;
                isMatch[complexState].Init();
                isRep0Long[complexState].Init();
            }
            isRep[i].Init();
            isRepG0[i].Init();
            isRepG1[i].Init();
            isRepG2[i].Init();
        }
        literalEncoder.Init();
        for (i = 0; i < LzmaBase.kNumLenToPosStates; i++)
            posSlotEncoder[i].Init();
        for (i = 0; i < LzmaBase.kNumFullDistances - LzmaBase.kEndPosModelIndex; i++)
            posEncoders[i].Init();

        lenEncoder.Init((uint)1 << posStateBits);
        repMatchLenEncoder.Init((uint)1 << posStateBits);

        posAlignEncoder.Init();

        longestMatchWasFound = false;
        optimumEndIndex = 0;
        optimumCurrentIndex = 0;
        additionalOffset = 0;
    }

    void MovePos(uint num)
    {
        if (num > 0)
        {
            matchFinder.Skip(num);
            additionalOffset += num;
        }
    }

    void ReadMatchDistances(out uint lenRes, out uint numDistancePairs)
    {
        lenRes = 0;
        numDistancePairs = matchFinder.GetMatches(matchDistances);
        if (numDistancePairs > 0)
        {
            lenRes = matchDistances[numDistancePairs - 2];
            if (lenRes == numFastBytes)
                lenRes += matchFinder.GetMatchLen((int)lenRes - 1, matchDistances[numDistancePairs - 1],
                    LzmaBase.kMatchMaxLen - lenRes);
        }
        additionalOffset++;
    }

    void ReleaseMFStream()
    {
        if (matchFinder != null && needReleaseMFStream)
        {
            matchFinder.ReleaseStream();
            needReleaseMFStream = false;
        }
    }

    void ReleaseOutStream() => rangeEncoder.ReleaseStream();

    void ReleaseStreams()
    {
        ReleaseMFStream();
        ReleaseOutStream();
    }

    void SetOutStream(Stream outStream) => rangeEncoder.SetStream(outStream);

    void SetStreams(Stream inStream, Stream outStream, long inSize)
    {
        this.inStream = inStream;
        finished = false;
        Create();
        SetOutStream(outStream);
        Init();

        // if (!_fastMode)
        {
            FillDistancesPrices();
            FillAlignPrices();
        }

        lenEncoder.SetTableSize(numFastBytes + 1 - LzmaBase.kMatchMinLen);
        lenEncoder.UpdateTables((uint)1 << posStateBits);
        repMatchLenEncoder.SetTableSize(numFastBytes + 1 - LzmaBase.kMatchMinLen);
        repMatchLenEncoder.UpdateTables((uint)1 << posStateBits);

        nowPos64 = 0;
    }

    void WriteEndMarker(uint posState)
    {
        if (!writeEndMark)
            return;

        isMatch[(state.Index << LzmaBase.kNumPosStatesBitsMax) + posState].Encode(rangeEncoder, 1);
        isRep[state.Index].Encode(rangeEncoder, 0);
        state.UpdateMatch();
        var len = LzmaBase.kMatchMinLen;
        lenEncoder.Encode(rangeEncoder, len - LzmaBase.kMatchMinLen, posState);
        uint posSlot = (1 << LzmaBase.kNumPosSlotBits) - 1;
        var lenToPosState = LzmaBase.GetLenToPosState(len);
        posSlotEncoder[lenToPosState].Encode(rangeEncoder, posSlot);
        var footerBits = 30;
        var posReduced = ((uint)1 << footerBits) - 1;
        rangeEncoder.EncodeDirectBits(posReduced >> LzmaBase.kNumAlignBits, footerBits - LzmaBase.kNumAlignBits);
        posAlignEncoder.ReverseEncode(rangeEncoder, posReduced & LzmaBase.kAlignMask);
    }

    #endregion Private Methods

    #region Public Fields

    public const int kDefaultDictionaryLogSize = 22;

    public const uint kNumFastBytesDefault = 0x20;

    public const int kPropSize = 5;

    #endregion Public Fields

    #region Public Constructors

    static LzmaEncoder()
    {
        const byte kFastSlots = 22;
        var c = 2;
        globalFastPos[0] = 0;
        globalFastPos[1] = 1;
        for (byte slotFast = 2; slotFast < kFastSlots; slotFast++)
        {
            var k = (uint)1 << ((slotFast >> 1) - 1);
            for (uint j = 0; j < k; j++, c++)
                globalFastPos[c] = slotFast;
        }
    }

    public LzmaEncoder()
    {
        for (var i = 0; i < kNumOpts; i++)
            optimum[i] = new Optimal();
        for (var i = 0; i < LzmaBase.kNumLenToPosStates; i++)
            posSlotEncoder[i] = new RcBitTreeEncoder(LzmaBase.kNumPosSlotBits);
    }

    #endregion Public Constructors

    #region Public Properties

    /// <summary>Gets or sets the used dictionary size</summary>
    public uint DictionarySize => dictionarySize > 0 ? dictionarySize : throw new InvalidOperationException($"{nameof(DictionarySize)} was not set. Use {nameof(SetDictionarySize)} first!");

    /// <summary>Use <see cref="DefaultProgressManager"/> during encoding.</summary>
    public bool UseProgressManager { get; set; } = false;

    #endregion Public Properties

    #region Public Methods

    /// <summary>Decodes the specified <paramref name="inStream"/> to <paramref name="outStream"/>.</summary>
    /// <param name="inStream">Input stream (raw)</param>
    /// <param name="outStream">Output stream (lzma)</param>
    /// <param name="inSize">If -1 is used the stream needs to be terminated during encoding with <see cref="LzmaCoderProperties.EndMarker"/> = true.</param>
    /// <exception cref="InvalidOperationException"></exception>
    public void Encode(Stream inStream, Stream outStream, long inSize)
    {
        if (inSize < 0 && !writeEndMark) throw new InvalidOperationException($"{nameof(WriteEndMarker)} was not set and {nameof(inSize)} is {inSize}!");
        var progress = UseProgressManager ? ProgressManager.CreateProgress(this) : null;
        progress?.Update(0, $"{nameof(LzmaEncoder)}.{nameof(Encode)}");
        if (dictionarySize == 0) throw new InvalidOperationException($"{nameof(DictionarySize)} was not set. Use {nameof(SetDictionarySize)} first!");
        needReleaseMFStream = false;
        try
        {
            SetStreams(inStream, outStream, inSize);
            while (true)
            {
                CodeOneBlock(out var processedInSize, out var processedOutSize, out var finished);
                if (finished)
                {
                    return;
                }
                if (progress != null)
                {
                    var value = inSize > 0 ? processedInSize / (float)inSize : inStream.CanSeek ? inStream.Position / inStream.Length : 0;
                    progress.Update(value);
                }
            }
        }
        finally
        {
            ReleaseStreams();
        }
    }

    /// <summary>Gets the coder state byte needed together with the dictionary size at decoding.</summary>
    /// <returns></returns>
    public byte GetCoderState() => (byte)((((posStateBits * 5) + numLiteralPosStateBits) * 9) + numLiteralContextBits);

    /// <inheritdoc/>
    public void SetCoderProperties(LzmaCoderProperties settings)
    {
        if (settings.NumFastBytes < 5 || settings.NumFastBytes > LzmaBase.kMatchMaxLen) throw new LzmaInvalidParamException("Out of range!", nameof(settings.NumFastBytes));
        numFastBytes = (uint)settings.NumFastBytes;

        matchFinderType = settings.MatchFinder;
        if (matchFinder != null)
        {
            dictionarySizePrev = 0xFFFFFFFF;
            matchFinder = null;
        }

        if (settings.DictionarySize != 0)
        {
            SetDictionarySize(settings.DictionarySize);
        }
        else if (dictionarySize == 0)
        {
            SetDictionarySize(dictionarySize = 1 << kDefaultDictionaryLogSize);
        }

        if (settings.PosStateBits < 0 || settings.PosStateBits > (uint)LzmaBase.kNumPosStatesBitsEncodingMax) throw new LzmaInvalidParamException("Out of range!", nameof(settings.PosStateBits));
        posStateBits = settings.PosStateBits;
        posStateMask = ((uint)1 << posStateBits) - 1;

        if (settings.LiteralPosStateBits < 0 || settings.LiteralPosStateBits > LzmaBase.kNumLitPosStatesBitsEncodingMax) throw new LzmaInvalidParamException("Out of range!", nameof(settings.LiteralPosStateBits));
        numLiteralPosStateBits = settings.LiteralPosStateBits;

        if (settings.LiteralContextBits < 0 || settings.LiteralContextBits > LzmaBase.kNumLitContextBitsMax) throw new LzmaInvalidParamException("Out of range!", nameof(settings.LiteralContextBits));
        numLiteralContextBits = settings.LiteralContextBits;

        SetWriteEndMarkerMode(settings.EndMarker);
    }

    public void SetDictionarySize(uint newDictionarySize)
    {
        const int kDicLogSizeMaxCompress = 30;
        if (newDictionarySize < (uint)(1 << LzmaBase.kDicLogSizeMin) || dictionarySize > (uint)(1 << kDicLogSizeMaxCompress)) throw new LzmaInvalidParamException("Out of range!", nameof(newDictionarySize));
        dictionarySize = newDictionarySize;
        int dicLogSize;
        for (dicLogSize = 0; dicLogSize < (uint)kDicLogSizeMaxCompress; dicLogSize++)
            if (dictionarySize <= (uint)1 << dicLogSize)
                break;
        distTableSize = (uint)dicLogSize * 2;
    }

    public void SetTrainSize(uint trainSize) => this.trainSize = trainSize;

    public void SetWriteEndMarkerMode(bool writeEndMarker) => writeEndMark = writeEndMarker;

    /// <inheritdoc/>
    public void WriteCoderProperties(Stream outStream)
    {
        if (dictionarySize == 0) throw new InvalidOperationException($"{nameof(DictionarySize)} was not set. Use {nameof(SetDictionarySize)} first!");
        properties[0] = GetCoderState();
        for (var i = 0; i < 4; i++)
            properties[1 + i] = (byte)((dictionarySize >> (8 * i)) & 0xFF);
        outStream.Write(properties, 0, kPropSize);
    }

    #endregion Public Methods
}
