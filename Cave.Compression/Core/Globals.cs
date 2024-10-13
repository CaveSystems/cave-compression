namespace Cave.Compression.Core;

static class Globals
{
    #region Internal Fields

    /// <summary>
    /// The lengths of the bit length codes are sent in order of decreasing probability, to avoid transmitting the lengths for unused bit length codes.
    /// </summary>
    internal static readonly int[] BitLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    #endregion Internal Fields
}
