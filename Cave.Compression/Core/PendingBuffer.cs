using System;

namespace Cave.Compression.Core;

/// <summary>
/// This class is general purpose class for writing data to a buffer.
///
/// It allows you to write bits as well as bytes
/// Based on DeflaterPending.java
///
/// author of the original java version : Jochen Hoenicke.
/// </summary>
class PendingBuffer
{
    #region Instance Fields

    /// <summary>
    /// Internal work buffer.
    /// </summary>
    readonly byte[] buffer;

    int start;
    int end;

    uint bits;
    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="PendingBuffer"/> class.
    /// </summary>
    public PendingBuffer()
        : this(4096)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PendingBuffer"/> class.
    /// </summary>
    /// <param name="bufferSize">
    /// size to use for internal buffer.
    /// </param>
    public PendingBuffer(int bufferSize) => buffer = new byte[bufferSize];

    #endregion

    /// <summary>
    /// Clear internal state/buffers.
    /// </summary>
    public void Reset() => start = end = BitCount = 0;

    /// <summary>
    /// Write a byte to buffer.
    /// </summary>
    /// <param name="value">
    /// The value to write.
    /// </param>
    public void WriteByte(int value)
    {
#if DebugDeflation
			if (DeflaterConstants.DEBUGGING && (start != 0) )
			{
				throw new SharpZipBaseException("Debug check: start != 0");
			}
#endif
        buffer[end++] = unchecked((byte)value);
    }

    /// <summary>
    /// Write a short value to buffer LSB first.
    /// </summary>
    /// <param name="value">
    /// The value to write.
    /// </param>
    public void WriteShort(int value)
    {
#if DebugDeflation
			if (DeflaterConstants.DEBUGGING && (start != 0) )
			{
				throw new SharpZipBaseException("Debug check: start != 0");
			}
#endif
        buffer[end++] = unchecked((byte)value);
        buffer[end++] = unchecked((byte)(value >> 8));
    }

    /// <summary>
    /// write an integer LSB first.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt(int value)
    {
#if DebugDeflation
			if (DeflaterConstants.DEBUGGING && (start != 0) )
			{
				throw new SharpZipBaseException("Debug check: start != 0");
			}
#endif
        buffer[end++] = unchecked((byte)value);
        buffer[end++] = unchecked((byte)(value >> 8));
        buffer[end++] = unchecked((byte)(value >> 16));
        buffer[end++] = unchecked((byte)(value >> 24));
    }

    /// <summary>
    /// Write a block of data to buffer.
    /// </summary>
    /// <param name="block">data to write.</param>
    /// <param name="offset">offset of first byte to write.</param>
    /// <param name="length">number of bytes to write.</param>
    public void WriteBlock(byte[] block, int offset, int length)
    {
#if DebugDeflation
			if (DeflaterConstants.DEBUGGING && (start != 0) ) 
			{
				throw new SharpZipBaseException("Debug check: start != 0");
			}
#endif
        Array.Copy(block, offset, buffer, end, length);
        end += length;
    }

    /// <summary>
    /// Gets or sets the number of bits written to the buffer.
    /// </summary>
    public int BitCount { get; set; }

    /// <summary>
    /// Align internal buffer on a byte boundary.
    /// </summary>
    public void AlignToByte()
    {
#if DebugDeflation
			if (DeflaterConstants.DEBUGGING && (start != 0) ) 
			{
				throw new SharpZipBaseException("Debug check: start != 0");
			}
#endif
        if (BitCount > 0)
        {
            buffer[end++] = unchecked((byte)bits);
            if (BitCount > 8)
            {
                buffer[end++] = unchecked((byte)(bits >> 8));
            }
        }

        bits = 0;
        BitCount = 0;
    }

    /// <summary>
    /// Write bits to internal buffer.
    /// </summary>
    /// <param name="b">source of bits.</param>
    /// <param name="count">number of bits to write.</param>
    public void WriteBits(int b, int count)
    {
#if DebugDeflation
			if (DeflaterConstants.DEBUGGING && (start != 0) ) 
			{
				throw new SharpZipBaseException("Debug check: start != 0");
			}

			//			if (DeflaterConstants.DEBUGGING) {
			//				//Console.WriteLine("writeBits("+b+","+count+")");
			//			}
#endif
        bits |= (uint)(b << BitCount);
        BitCount += count;
        if (BitCount >= 16)
        {
            buffer[end++] = unchecked((byte)bits);
            buffer[end++] = unchecked((byte)(bits >> 8));
            bits >>= 16;
            BitCount -= 16;
        }
    }

    /// <summary>
    /// Write a short value to internal buffer most significant byte first.
    /// </summary>
    /// <param name="s">value to write.</param>
    public void WriteShortMSB(int s)
    {
#if DebugDeflation
			if (DeflaterConstants.DEBUGGING && (start != 0) ) 
			{
				throw new SharpZipBaseException("Debug check: start != 0");
			}
#endif
        buffer[end++] = unchecked((byte)(s >> 8));
        buffer[end++] = unchecked((byte)s);
    }

    /// <summary>
    /// Gets a value indicating whether the buffer has been flushed.
    /// </summary>
    public bool IsFlushed => end == 0;

    /// <summary>
    /// Flushes the pending buffer into the given output array.  If the
    /// output array is to small, only a partial flush is done.
    /// </summary>
    /// <param name="output">The output array.</param>
    /// <param name="offset">The offset into output array.</param>
    /// <param name="length">The maximum number of bytes to store.</param>
    /// <returns>The number of bytes flushed.</returns>
    public int Flush(byte[] output, int offset, int length)
    {
        if (BitCount >= 8)
        {
            buffer[end++] = unchecked((byte)bits);
            bits >>= 8;
            BitCount -= 8;
        }

        if (length > end - start)
        {
            length = end - start;
            Array.Copy(buffer, start, output, offset, length);
            start = 0;
            end = 0;
        }
        else
        {
            Array.Copy(buffer, start, output, offset, length);
            start += length;
        }

        return length;
    }

    /// <summary>
    /// Convert internal buffer to byte array.
    /// Buffer is empty on completion.
    /// </summary>
    /// <returns>
    /// The internal buffer contents converted to a byte array.
    /// </returns>
    public byte[] ToByteArray()
    {
        AlignToByte();

        var result = new byte[end - start];
        Array.Copy(buffer, start, result, 0, result.Length);
        start = 0;
        end = 0;
        return result;
    }
}
