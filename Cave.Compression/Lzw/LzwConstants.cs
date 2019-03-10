namespace Cave.Compression.Lzw
{
    /// <summary>
    /// This class contains constants used for LZW.
    /// </summary>
    public sealed class LzwConstants
    {
        /// <summary>
        /// Magic number found at start of LZW header: 0x1f 0x9d.
        /// </summary>
        public const int MAGIC = 0x1f9d;

        /// <summary>
        /// Maximum number of bits per code.
        /// </summary>
        public const int MaxBits = 16;

        /* 3rd header byte:
         * bit 0..4 Number of compression bits
         * bit 5    Extended header
         * bit 6    Free
         * bit 7    Block mode
         */

        /// <summary>
        /// Mask for 'number of compression bits'.
        /// </summary>
        public const int BitMask = 0x1f;

        /// <summary>
        /// Indicates the presence of a fourth header byte.
        /// </summary>
        public const int ExtendedMask = 0x20;

        /// <summary>
        /// Reserved bits.
        /// </summary>
        public const int ReservedMask = 0x60;

        /// <summary>
        /// Block compression: if table is full and compression rate is dropping,
        /// clear the dictionary.
        /// </summary>
        public const int BlockModeMask = 0x80;

        /// <summary>
        /// LZW file header size (in bytes).
        /// </summary>
        public const int HeaderSize = 3;

        /// <summary>
        /// Initial number of bits per code.
        /// </summary>
        public const int InitBits = 9;

        LzwConstants()
        {
        }
    }
}
