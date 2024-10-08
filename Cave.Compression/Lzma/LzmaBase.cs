// LzmaBase.cs

namespace Cave.Compression.Lzma;

internal abstract class LzmaBase
{
    #region Public Structs

    public struct State
    {
        #region Public Fields

        public uint Index;

        #endregion Public Fields

        #region Public Methods

        public void Init() => Index = 0;

        public bool IsCharState() => Index < 7;

        public void UpdateChar()
        {
            if (Index < 4) Index = 0;
            else if (Index < 10) Index -= 3;
            else Index -= 6;
        }

        public void UpdateMatch() => Index = (uint)(Index < 7 ? 7 : 10);

        public void UpdateRep() => Index = (uint)(Index < 7 ? 8 : 11);

        public void UpdateShortRep() => Index = (uint)(Index < 7 ? 9 : 11);

        #endregion Public Methods
    }

    #endregion Public Structs

    #region Public Fields

    public const uint KAlignMask = KAlignTableSize - 1;
    public const uint KAlignTableSize = 1 << KNumAlignBits;
    public const int KDicLogSizeMin = 0;
    public const uint KEndPosModelIndex = 14;
    public const uint KMatchMaxLen = KMatchMinLen + KNumLenSymbols - 1;
    public const uint KMatchMinLen = 2;
    public const int KNumAlignBits = 4;
    public const uint KNumFullDistances = 1 << ((int)KEndPosModelIndex / 2);
    public const int KNumHighLenBits = 8;
    public const uint KNumLenSymbols = KNumLowLenSymbols + KNumMidLenSymbols + (1 << KNumHighLenBits);
    public const uint KNumLenToPosStates = 1 << KNumLenToPosStatesBits;
    public const int KNumLenToPosStatesBits = 2;
    public const uint KNumLitContextBitsMax = 8;
    public const uint KNumLitPosStatesBitsEncodingMax = 4;
    public const int KNumLowLenBits = 3;
    public const uint KNumLowLenSymbols = 1 << KNumLowLenBits;
    public const int KNumMidLenBits = 3;
    public const uint KNumMidLenSymbols = 1 << KNumMidLenBits;
    public const uint KNumPosModels = KEndPosModelIndex - KStartPosModelIndex;
    public const int KNumPosSlotBits = 6;
    public const int KNumPosStatesBitsEncodingMax = 4;
    public const int KNumPosStatesBitsMax = 4;
    public const uint KNumPosStatesEncodingMax = 1 << KNumPosStatesBitsEncodingMax;
    public const uint KNumPosStatesMax = 1 << KNumPosStatesBitsMax;
    public const uint KNumRepDistances = 4;
    public const uint KNumStates = 12;
    public const uint KStartPosModelIndex = 4;

    #endregion Public Fields

    #region Public Methods

    // it's for speed optimization
    public static uint GetLenToPosState(uint len)
    {
        len -= KMatchMinLen;
        if (len < KNumLenToPosStates)
            return len;
        return KNumLenToPosStates - 1;
    }

    #endregion Public Methods
}
