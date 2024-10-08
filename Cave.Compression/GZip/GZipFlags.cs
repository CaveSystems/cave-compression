using System;

namespace Cave.Compression.GZip;

/// <summary>
/// GZip header flags.
/// </summary>
[Flags]
public enum GZipFlags
{
    /// <summary>
    /// Flag bit mask for text
    /// </summary>
    Text = 1 << 0,

    /// <summary>
    /// Flag bitmask for Crc
    /// </summary>
    CRC = 1 << 1,

    /// <summary>
    /// Flag bit mask for extra
    /// </summary>
    Extra = 1 << 2,

    /// <summary>
    /// flag bitmask for name
    /// </summary>
    Name = 1 << 3,

    /// <summary>
    /// flag bit mask indicating comment is present
    /// </summary>
    Comment = 1 << 4,
}
