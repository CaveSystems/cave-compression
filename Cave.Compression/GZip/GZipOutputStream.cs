using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Cave.Compression.Checksum;
using Cave.Compression.Core;
using Cave.Compression.Streams;

namespace Cave.Compression.GZip;

/// <summary>
/// This filter stream is used to compress a stream into a "GZIP" stream. The "GZIP" format is described in RFC 1952.
///
/// author of the original java version : John Leuner.
/// </summary>
/// <example>
/// This sample shows how to gzip a file.
/// <code>
///using System;
///using System.IO;
///
///using Cave.Compression.GZip;
///using Cave.Compression.Core;
///
///class MainClass
///{
///public static void Main(string[] args)
///{
///using (Stream s = new GZipOutputStream(File.Create(args[0] + ".gz")))
///using (FileStream fs = File.OpenRead(args[0])) {
///byte[] writeData = new byte[4096];
///Streamutils.Copy(s, fs, writeData);
///}
///}
///}
///}
/// </code>
/// </example>
public class GZipOutputStream : DeflaterOutputStream
{
    #region Private Fields

    /// <summary>CRC-32 value for uncompressed data.</summary>
    readonly ZipCrc32 crc = new();

    OutputState state = OutputState.Header;

    #endregion Private Fields

    #region Private Enums

    enum OutputState
    {
        Header,
        Footer,
        Finished,
        Closed,
    }

    #endregion Private Enums

    #region Private Methods

    void WriteHeader()
    {
        if (state == OutputState.Header)
        {
            state = OutputState.Footer;

            var mod_time = (int)((DateTime.Now.Ticks - new DateTime(1970, 1, 1).Ticks) / 10000000L);  // Ticks give back 100ns intervals
            byte[] gzipHeader =
            [
                // The two magic bytes
                 GZipConstants.MAGIC >> 8,
                GZipConstants.MAGIC & 0xff,

                // The compression type
                Deflater.DEFLATED,

                // The flags (not set)
                0,

                // The modification time
                (byte)mod_time,
                (byte)(mod_time >> 8),
                (byte)(mod_time >> 16),
                (byte)(mod_time >> 24),

                // The extra flags
                0,

                // The OS type (unknown)
                255,
            ];
            BaseOutputStream.Write(gzipHeader, 0, gzipHeader.Length);
        }
    }

    #endregion Private Methods

    #region Protected Methods

    /// <summary>Writes remaining compressed output data to the output stream and closes it.</summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    [SuppressMessage("Usage", "CA2215")]
    protected override void Dispose(bool disposing)
    {
        try
        {
            Finish();
        }
        finally
        {
            if (state != OutputState.Closed)
            {
                state = OutputState.Closed;
                if (IsStreamOwner)
                {
                    BaseOutputStream.Dispose();
                }
            }
        }
    }

    #endregion Protected Methods

    #region Public Constructors

    /// <summary>Initializes a new instance of the <see cref="GZipOutputStream"/> class.</summary>
    /// <param name="baseOutputStream">The stream to read data (to be compressed) from.</param>
    /// <param name="level">The level.</param>
    /// <param name="size">Size of the buffer to use.</param>
    public GZipOutputStream(Stream baseOutputStream, CompressionStrength level = CompressionStrength.Best, int size = 4096)
        : base(baseOutputStream, new Deflater(level, true), size)
    {
    }

    #endregion Public Constructors

    #region Public Properties

    /// <summary>Gets or sets the active compression level (1-9). The new level will be activated immediately.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Level specified is not supported.</exception>
    /// <see cref="Deflater"/>
    public CompressionStrength Strength
    {
        get => Deflater.Strength;
        set => Deflater.Strength = value;
    }

    #endregion Public Properties

    #region Public Methods

    /// <summary>Finish compression and write any footer information required to stream.</summary>
    public override void Finish()
    {
        // If no data has been written a header should be added.
        if (state == OutputState.Header)
        {
            WriteHeader();
        }

        if (state == OutputState.Footer)
        {
            state = OutputState.Finished;
            base.Finish();

            var totalin = (uint)(Deflater.TotalIn & 0xffffffff);
            var crcval = (uint)(crc.Value & 0xffffffff);

            byte[] gzipFooter;

            unchecked
            {
                gzipFooter =
                [
                    (byte)crcval,
                    (byte)(crcval >> 8),
                    (byte)(crcval >> 16),
                    (byte)(crcval >> 24),
                    (byte)totalin,
                    (byte)(totalin >> 8),
                    (byte)(totalin >> 16),
                    (byte)(totalin >> 24),
                ];
            }

            BaseOutputStream.Write(gzipFooter, 0, gzipFooter.Length);
        }
    }

    /// <summary>Write given buffer to output updating crc.</summary>
    /// <param name="buffer">Buffer to write.</param>
    /// <param name="offset">Offset of first byte in buf to write.</param>
    /// <param name="count">Number of bytes to write.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (state == OutputState.Header)
        {
            WriteHeader();
        }

        if (state != OutputState.Footer)
        {
            throw new InvalidOperationException("Write not permitted in current state");
        }

        crc.Update(buffer, offset, count);
        base.Write(buffer, offset, count);
    }

    #endregion Public Methods
}
