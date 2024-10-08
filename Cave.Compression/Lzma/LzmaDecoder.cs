#nullable disable

using System;
using Cave.Compression.Lzma.RangeCoder;
using Cave.Compression.Lzma.LZ;
using System.IO;
using Cave.Progress;

namespace Cave.Compression.Lzma;

/// <summary>Lzma decoder</summary>
/// <remarks>
/// Use same <see cref="DictionarySize"/> and <see cref="SetDecoderState(byte)"/> used for encoding. Alternative use <see cref="SetDecoderProperties(byte[])"/>
/// after using <see cref="LzmaEncoder.WriteCoderProperties(Stream)"/>
/// </remarks>
public class LzmaDecoder
{
    #region Private Classes

    sealed class LenDecoder
    {
        #region Private Fields

        readonly RcBitTreeDecoder highCoder = new(LzmaBase.KNumHighLenBits);
        readonly RcBitTreeDecoder[] lowCoder = new RcBitTreeDecoder[LzmaBase.KNumPosStatesMax];
        readonly RcBitTreeDecoder[] midCoder = new RcBitTreeDecoder[LzmaBase.KNumPosStatesMax];
        RcBitDecoder choice;
        RcBitDecoder choice2;
        uint numPosStates;

        #endregion Private Fields

        #region Public Methods

        public void Create(uint numPosStates)
        {
            for (var posState = this.numPosStates; posState < numPosStates; posState++)
            {
                lowCoder[posState] = new RcBitTreeDecoder(LzmaBase.KNumLowLenBits);
                midCoder[posState] = new RcBitTreeDecoder(LzmaBase.KNumMidLenBits);
            }
            this.numPosStates = numPosStates;
        }

        public uint Decode(RcDecoder rangeDecoder, uint posState)
        {
            if (choice.Decode(rangeDecoder) == 0)
                return lowCoder[posState].Decode(rangeDecoder);
            else
            {
                var symbol = LzmaBase.KNumLowLenSymbols;
                if (choice2.Decode(rangeDecoder) == 0)
                    symbol += midCoder[posState].Decode(rangeDecoder);
                else
                {
                    symbol += LzmaBase.KNumMidLenSymbols;
                    symbol += highCoder.Decode(rangeDecoder);
                }
                return symbol;
            }
        }

        public void Init()
        {
            choice.Init();
            for (uint posState = 0; posState < numPosStates; posState++)
            {
                lowCoder[posState].Init();
                midCoder[posState].Init();
            }
            choice2.Init();
            highCoder.Init();
        }

        #endregion Public Methods
    }

    sealed class LiteralDecoder
    {
        #region Private Structs

        struct Decoder2
        {
            #region Private Fields

            RcBitDecoder[] decoders;

            #endregion Private Fields

            #region Public Methods

            public void Create() => decoders = new RcBitDecoder[0x300];

            public byte DecodeNormal(RcDecoder rangeDecoder)
            {
                uint symbol = 1;
                do
                    symbol = (symbol << 1) | decoders[symbol].Decode(rangeDecoder);
                while (symbol < 0x100);
                return (byte)symbol;
            }

            public byte DecodeWithMatchByte(RcDecoder rangeDecoder, byte matchByte)
            {
                uint symbol = 1;
                do
                {
                    var matchBit = (uint)(matchByte >> 7) & 1;
                    matchByte <<= 1;
                    var bit = decoders[((1 + matchBit) << 8) + symbol].Decode(rangeDecoder);
                    symbol = (symbol << 1) | bit;
                    if (matchBit != bit)
                    {
                        while (symbol < 0x100)
                            symbol = (symbol << 1) | decoders[symbol].Decode(rangeDecoder);
                        break;
                    }
                }
                while (symbol < 0x100);
                return (byte)symbol;
            }

            public void Init() { for (var i = 0; i < 0x300; i++) decoders[i].Init(); }

            #endregion Public Methods
        }

        #endregion Private Structs

        #region Private Fields

        Decoder2[] coders;
        int numPosBits;
        int numPrevBits;
        uint posMask;

        #endregion Private Fields

        #region Private Methods

        uint GetState(uint pos, byte prevByte) => ((pos & posMask) << numPrevBits) + (uint)(prevByte >> (8 - numPrevBits));

        #endregion Private Methods

        #region Public Methods

        public void Create(int numPosBits, int numPrevBits)
        {
            if (coders != null && this.numPrevBits == numPrevBits &&
                this.numPosBits == numPosBits)
                return;
            this.numPosBits = numPosBits;
            posMask = ((uint)1 << numPosBits) - 1;
            this.numPrevBits = numPrevBits;
            var numStates = (uint)1 << (this.numPrevBits + this.numPosBits);
            coders = new Decoder2[numStates];
            for (uint i = 0; i < numStates; i++)
                coders[i].Create();
        }

        public byte DecodeNormal(RcDecoder rangeDecoder, uint pos, byte prevByte) => coders[GetState(pos, prevByte)].DecodeNormal(rangeDecoder);

        public byte DecodeWithMatchByte(RcDecoder rangeDecoder, uint pos, byte prevByte, byte matchByte) => coders[GetState(pos, prevByte)].DecodeWithMatchByte(rangeDecoder, matchByte);

        public void Init()
        {
            var numStates = (uint)1 << (numPrevBits + numPosBits);
            for (uint i = 0; i < numStates; i++)
                coders[i].Init();
        }

        #endregion Public Methods
    };

    #endregion Private Classes

    #region Private Fields

    readonly RcBitDecoder[] isMatchDecoders = new RcBitDecoder[LzmaBase.KNumStates << LzmaBase.KNumPosStatesBitsMax];
    readonly RcBitDecoder[] isRep0LongDecoders = new RcBitDecoder[LzmaBase.KNumStates << LzmaBase.KNumPosStatesBitsMax];
    readonly RcBitDecoder[] isRepDecoders = new RcBitDecoder[LzmaBase.KNumStates];
    readonly RcBitDecoder[] isRepG0Decoders = new RcBitDecoder[LzmaBase.KNumStates];
    readonly RcBitDecoder[] isRepG1Decoders = new RcBitDecoder[LzmaBase.KNumStates];
    readonly RcBitDecoder[] isRepG2Decoders = new RcBitDecoder[LzmaBase.KNumStates];
    readonly LenDecoder lenDecoder = new();
    readonly LiteralDecoder literalDecoder = new();
    readonly LzOutWindow outWindow = new();
    readonly RcBitTreeDecoder posAlignDecoder = new(LzmaBase.KNumAlignBits);
    readonly RcBitDecoder[] posDecoders = new RcBitDecoder[LzmaBase.KNumFullDistances - LzmaBase.KEndPosModelIndex];
    readonly RcBitTreeDecoder[] posSlotDecoder = new RcBitTreeDecoder[LzmaBase.KNumLenToPosStates];
    readonly RcDecoder rangeDecoder = new();
    readonly LenDecoder repLenDecoder = new();
    uint dictionarySize;
    uint dictionarySizeCheck;
    uint posStateMask;
    bool solid;

    #endregion Private Fields

    #region Private Methods

    void Init(Stream inStream, Stream outStream)
    {
        if (dictionarySize == 0) throw new InvalidOperationException($"{nameof(DictionarySize)} was not set. Use {nameof(SetDictionarySize)} first!");
        rangeDecoder.Init(inStream);
        outWindow.Init(outStream, solid);

        uint i;
        for (i = 0; i < LzmaBase.KNumStates; i++)
        {
            for (uint j = 0; j <= posStateMask; j++)
            {
                var index = (i << LzmaBase.KNumPosStatesBitsMax) + j;
                isMatchDecoders[index].Init();
                isRep0LongDecoders[index].Init();
            }
            isRepDecoders[i].Init();
            isRepG0Decoders[i].Init();
            isRepG1Decoders[i].Init();
            isRepG2Decoders[i].Init();
        }

        literalDecoder.Init();
        for (i = 0; i < LzmaBase.KNumLenToPosStates; i++)
            posSlotDecoder[i].Init();
        // m_PosSpecDecoder.Init();
        for (i = 0; i < LzmaBase.KNumFullDistances - LzmaBase.KEndPosModelIndex; i++)
            posDecoders[i].Init();

        lenDecoder.Init();
        repLenDecoder.Init();
        posAlignDecoder.Init();
    }

    #endregion Private Methods

    #region Public Constructors

    /// <summary>Creates a new instance of the decoder</summary>
    public LzmaDecoder()
    {
        for (var i = 0; i < LzmaBase.KNumLenToPosStates; i++)
        {
            posSlotDecoder[i] = new RcBitTreeDecoder(LzmaBase.KNumPosSlotBits);
        }
    }

    #endregion Public Constructors

    #region Public Properties

    /// <summary>Gets or sets the used dictionary size</summary>
    public uint DictionarySize => dictionarySize > 0 ? dictionarySize : throw new InvalidOperationException($"{nameof(DictionarySize)} was not set. Use {nameof(SetDictionarySize)} first!");

    /// <summary>Use <see cref="DefaultProgressManager"/> during decoding.</summary>
    public bool UseProgressManager { get; set; }

    #endregion Public Properties

    #region Public Methods

    /// <summary>Decodes the specified <paramref name="inStream"/> to <paramref name="outStream"/>.</summary>
    /// <param name="inStream">Input stream (lzma)</param>
    /// <param name="outStream">Output stream (decoded)</param>
    /// <param name="outSize">
    /// Expected output size or -1. If -1 is used the stream needs to be terminated during encoding with <see cref="LzmaCoderProperties.EndMarker"/> = true.
    /// </param>
    /// <exception cref="LzmaDataErrorException"></exception>
    public void Decode(Stream inStream, Stream outStream, long outSize)
    {
        var progress = UseProgressManager ? ProgressManager.CreateProgress(this) : null;
        progress?.Update(0, $"{nameof(LzmaDecoder)}.{nameof(Decode)}");
        Init(inStream, outStream);

        var state = new LzmaBase.State();
        state.Init();
        uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;

        ulong nowPos64 = 0;
        var outSize64 = (ulong)outSize;
        if (nowPos64 < outSize64)
        {
            if (isMatchDecoders[state.Index << LzmaBase.KNumPosStatesBitsMax].Decode(rangeDecoder) != 0)
                throw new LzmaDataErrorException();
            state.UpdateChar();
            var b = literalDecoder.DecodeNormal(rangeDecoder, 0, 0);
            outWindow.PutByte(b);
            nowPos64++;
        }
        var nextProgress = progress is null ? ulong.MaxValue : 0ul;
        while (nowPos64 < outSize64)
        {
            var posState = (uint)nowPos64 & posStateMask;
            if (isMatchDecoders[(state.Index << LzmaBase.KNumPosStatesBitsMax) + posState].Decode(rangeDecoder) == 0)
            {
                byte b;
                var prevByte = outWindow.GetByte(0);
                if (!state.IsCharState())
                    b = literalDecoder.DecodeWithMatchByte(rangeDecoder,
                        (uint)nowPos64, prevByte, outWindow.GetByte(rep0));
                else
                    b = literalDecoder.DecodeNormal(rangeDecoder, (uint)nowPos64, prevByte);
                outWindow.PutByte(b);
                state.UpdateChar();
                nowPos64++;
            }
            else
            {
                uint len;
                if (isRepDecoders[state.Index].Decode(rangeDecoder) == 1)
                {
                    if (isRepG0Decoders[state.Index].Decode(rangeDecoder) == 0)
                    {
                        if (isRep0LongDecoders[(state.Index << LzmaBase.KNumPosStatesBitsMax) + posState].Decode(rangeDecoder) == 0)
                        {
                            state.UpdateShortRep();
                            outWindow.PutByte(outWindow.GetByte(rep0));
                            nowPos64++;
                            continue;
                        }
                    }
                    else
                    {
                        uint distance;
                        if (isRepG1Decoders[state.Index].Decode(rangeDecoder) == 0)
                        {
                            distance = rep1;
                        }
                        else
                        {
                            if (isRepG2Decoders[state.Index].Decode(rangeDecoder) == 0)
                                distance = rep2;
                            else
                            {
                                distance = rep3;
                                rep3 = rep2;
                            }
                            rep2 = rep1;
                        }
                        rep1 = rep0;
                        rep0 = distance;
                    }
                    len = repLenDecoder.Decode(rangeDecoder, posState) + LzmaBase.KMatchMinLen;
                    state.UpdateRep();
                }
                else
                {
                    rep3 = rep2;
                    rep2 = rep1;
                    rep1 = rep0;
                    len = LzmaBase.KMatchMinLen + lenDecoder.Decode(rangeDecoder, posState);
                    state.UpdateMatch();
                    var posSlot = posSlotDecoder[LzmaBase.GetLenToPosState(len)].Decode(rangeDecoder);
                    if (posSlot >= LzmaBase.KStartPosModelIndex)
                    {
                        var numDirectBits = (int)((posSlot >> 1) - 1);
                        rep0 = (2 | (posSlot & 1)) << numDirectBits;
                        if (posSlot < LzmaBase.KEndPosModelIndex)
                            rep0 += RcBitTreeDecoder.ReverseDecode(posDecoders,
                                    rep0 - posSlot - 1, rangeDecoder, numDirectBits);
                        else
                        {
                            rep0 += rangeDecoder.DecodeDirectBits(
                                numDirectBits - LzmaBase.KNumAlignBits) << LzmaBase.KNumAlignBits;
                            rep0 += posAlignDecoder.ReverseDecode(rangeDecoder);
                        }
                    }
                    else
                        rep0 = posSlot;
                }
                if (rep0 >= outWindow.TrainSize + nowPos64 || rep0 >= dictionarySizeCheck)
                {
                    //end marker
                    if (rep0 == 0xFFFFFFFF)
                        break;
                    throw new LzmaDataErrorException();
                }
                outWindow.CopyBlock(rep0, len);
                nowPos64 += len;

                if (nowPos64 > nextProgress)
                {
                    progress.Update(nowPos64 / (float)outSize64);
                    nextProgress = Math.Min(nowPos64 + (1 << 18), outSize64);
                }
            }
        }
        outWindow.Flush();
        outWindow.ReleaseStream();
        rangeDecoder.ReleaseStream();
    }

    /// <summary>Sets the decoder properties from a 5 byte array (state and dictionary size)</summary>
    /// <param name="properties"></param>
    /// <exception cref="LzmaInvalidParamException"></exception>
    public void SetDecoderProperties(byte[] properties)
    {
        if (properties.Length < 5) throw new LzmaInvalidParamException(nameof(properties));
        SetDecoderState(properties[0]);
        uint dictionarySize = 0;
        for (var i = 0; i < 4; i++) dictionarySize += (uint)properties[1 + i] << (i * 8);
        SetDictionarySize(dictionarySize);
    }

    /// <summary>Sets the decoder state</summary>
    /// <param name="state"></param>
    /// <exception cref="LzmaInvalidParamException"></exception>
    public void SetDecoderState(byte state)
    {
        var lc = state % 9;
        var remainder = state / 9;
        var lp = remainder % 5;
        var pb = remainder / 5;
        if (pb > LzmaBase.KNumPosStatesBitsMax) throw new LzmaInvalidParamException(nameof(LzmaBase.KNumPosStatesBitsMax));
        SetLiteralProperties(lp, lc);
        SetPosBitsProperties(pb);
    }

    /// <summary>Sets the dictionary size</summary>
    /// <param name="dictionarySize"></param>
    public void SetDictionarySize(uint dictionarySize)
    {
        if (this.dictionarySize != dictionarySize)
        {
            this.dictionarySize = dictionarySize;
            dictionarySizeCheck = Math.Max(this.dictionarySize, 1);
            var blockSize = Math.Max(dictionarySizeCheck, 1 << 12);
            outWindow.Create(blockSize);
        }
    }

    /// <summary>Sets the literal decoder properties (part of the decoder state byte)</summary>
    /// <param name="numPosBits"></param>
    /// <param name="numPrevBits"></param>
    /// <exception cref="LzmaInvalidParamException"></exception>
    public void SetLiteralProperties(int numPosBits, int numPrevBits)
    {
        if (numPosBits > 8) throw new LzmaInvalidParamException($"{nameof(numPosBits)} > 8!", nameof(numPosBits));
        if (numPrevBits > 8) throw new LzmaInvalidParamException($"{nameof(numPrevBits)} > 8!", nameof(numPrevBits));
        literalDecoder.Create(numPosBits, numPrevBits);
    }

    /// <summary>Sets the position bits of the decoder properties (part of the decoder state byte)</summary>
    /// <param name="posBits"></param>
    /// <exception cref="LzmaInvalidParamException"></exception>
    public void SetPosBitsProperties(int posBits)
    {
        if (posBits > LzmaBase.KNumPosStatesBitsMax) throw new LzmaInvalidParamException($"{nameof(posBits)} > {nameof(LzmaBase.KNumPosStatesBitsMax)}", nameof(posBits));
        var numPosStates = (uint)1 << posBits;
        lenDecoder.Create(numPosStates);
        repLenDecoder.Create(numPosStates);
        posStateMask = numPosStates - 1;
    }

    /// <summary>Trains the decoder with the specified data</summary>
    /// <param name="stream"></param>
    /// <returns></returns>
    public bool Train(Stream stream)
    {
        solid = true;
        return outWindow.Train(stream);
    }

    #endregion Public Methods
}
