using System;
using System.IO;
using Cave.Compression.Core;
using Cave.Compression.GZip;

namespace Cave.Compression.Streams
{
    /// <summary>
    /// This filter stream is used to decompress data compressed using the "deflate"
    /// format. The "deflate" format is described in RFC 1951.
    ///
    /// This stream may form the basis for other decompression filters, such
    /// as the <see cref="GZipInputStream">GZipInputStream</see>.
    ///
    /// Author of the original java version : John Leuner.
    /// </summary>
    public class InflaterInputStream : Stream
    {
        #region Instance Fields

        /// <summary>
        /// Base stream the inflater reads from.
        /// </summary>
        Stream baseInputStream;

        /// <summary>
        /// Flag indicating wether this instance has been closed or not.
        /// </summary>
        bool isClosed;

        #endregion

        /// <summary>
        /// Gets or sets the decompressor for this stream.
        /// </summary>
        protected Inflater Inflater { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="InflaterInputBuffer"/> for this stream.
        /// </summary>
        protected InflaterInputBuffer InputBuffer { get; set; }

        /// <summary>
        /// Gets or sets the compressed size.
        /// </summary>
        protected long CompressedSize { get; set; }

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="InflaterInputStream"/> class.
        /// </summary>
        /// <remarks>Creates an InflaterInputStream with the default decompressor
        /// and a default buffer size of 4KB.</remarks>
        /// <param name = "baseInputStream">The InputStream to read bytes from.</param>
        public InflaterInputStream(Stream baseInputStream)
            : this(baseInputStream, new Inflater(), 4096)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InflaterInputStream"/> class.
        /// </summary>
        /// <remarks>
        /// Create an InflaterInputStream with the specified decompressor
        /// and a default buffer size of 4KB.
        /// </remarks>
        /// <param name = "baseInputStream">
        /// The source of input data.
        /// </param>
        /// <param name = "inf">
        /// The decompressor used to decompress data read from baseInputStream.
        /// </param>
        public InflaterInputStream(Stream baseInputStream, Inflater inf)
            : this(baseInputStream, inf, 4096)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InflaterInputStream"/> class.
        /// </summary>
        /// <remarks>
        /// Create an InflaterInputStream with the specified decompressor
        /// and the specified buffer size.
        /// </remarks>
        /// <param name = "baseInputStream">
        /// The InputStream to read bytes from.
        /// </param>
        /// <param name = "inflater">
        /// The decompressor to use.
        /// </param>
        /// <param name = "bufferSize">
        /// Size of the buffer to use.
        /// </param>
        public InflaterInputStream(Stream baseInputStream, Inflater inflater, int bufferSize)
        {
            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSize));
            }

            this.baseInputStream = baseInputStream ?? throw new ArgumentNullException(nameof(baseInputStream));
            Inflater = inflater ?? throw new ArgumentNullException(nameof(inflater));

            InputBuffer = new InflaterInputBuffer(baseInputStream, bufferSize);
        }

        #endregion

        /// <summary>
        /// Gets or sets a value indicating whether this instance will close the underlying stream.
        /// </summary>
        /// <remarks>The default value is true.</remarks>
        public bool IsStreamOwner { get; set; } = true;

        /// <summary>
        /// Skip specified number of bytes of uncompressed data.
        /// </summary>
        /// <param name ="count">
        /// Number of bytes to skip.
        /// </param>
        /// <returns>
        /// The number of bytes skipped, zero if the end of
        /// stream has been reached.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="count">The number of bytes</paramref> to skip is less than or equal to zero.
        /// </exception>
        public long Skip(long count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            // v0.80 Skip by seeking if underlying stream supports it...
            if (baseInputStream.CanSeek)
            {
                baseInputStream.Seek(count, SeekOrigin.Current);
                return count;
            }
            else
            {
                int length = 2048;
                if (count < length)
                {
                    length = (int)count;
                }

                byte[] tmp = new byte[length];
                int readCount = 1;
                long toSkip = count;

                while ((toSkip > 0) && (readCount > 0))
                {
                    if (toSkip < length)
                    {
                        length = (int)toSkip;
                    }

                    readCount = baseInputStream.Read(tmp, 0, length);
                    toSkip -= readCount;
                }

                return count - toSkip;
            }
        }

        /// <summary>
        /// Clear any cryptographic state.
        /// </summary>
        protected void StopDecrypting()
        {
            InputBuffer.CryptoTransform = null;
        }

        /// <summary>
        /// Gets 0 once the end of the stream (EOF) has been reached.
        /// Otherwise returns 1.
        /// </summary>
        public virtual int Available
        {
            get
            {
                return Inflater.IsDone ? 0 : 1;
            }
        }

        /// <summary>
        /// Fills the buffer with more data to decompress.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// Stream ends early.
        /// </exception>
        protected void Fill()
        {
            // Protect against redundant calls
            if (InputBuffer.Available <= 0)
            {
                InputBuffer.Fill();
                if (InputBuffer.Available <= 0)
                {
                    throw new InvalidDataException("Unexpected EOF");
                }
            }

            InputBuffer.SetInflaterInput(Inflater);
        }

        #region Stream Overrides

        /// <summary>
        /// Gets a value indicating whether the current stream supports reading.
        /// </summary>
        public override bool CanRead
        {
            get
            {
                return baseInputStream.CanRead;
            }
        }

        /// <summary>
        /// Gets a value indicating whether seeking is supported for this stream.
        /// </summary>
        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this stream is writeable.
        /// </summary>
        public override bool CanWrite
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value representing the length of the stream in bytes.
        /// </summary>
        public override long Length
        {
            get
            {
                // return inputBuffer.RawLength;
                throw new NotSupportedException("InflaterInputStream Length is not supported");
            }
        }

        /// <summary>
        /// Gets or sets the current position within the stream.
        /// Throws a NotSupportedException when attempting to set the position.
        /// </summary>
        /// <exception cref="NotSupportedException">Attempting to set the position.</exception>
        public override long Position
        {
            get
            {
                return baseInputStream.Position;
            }
            set
            {
                throw new NotSupportedException("InflaterInputStream Position not supported");
            }
        }

        /// <summary>
        /// Flushes the baseInputStream.
        /// </summary>
        public override void Flush()
        {
            baseInputStream.Flush();
        }

        /// <summary>
        /// Sets the position within the current stream
        /// Always throws a NotSupportedException.
        /// </summary>
        /// <param name="offset">The relative offset to seek to.</param>
        /// <param name="origin">The <see cref="SeekOrigin"/> defining where to seek from.</param>
        /// <returns>The new position in the stream.</returns>
        /// <exception cref="NotSupportedException">Any access.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Seek not supported");
        }

        /// <summary>
        /// Set the length of the current stream
        /// Always throws a NotSupportedException.
        /// </summary>
        /// <param name="value">The new length value for the stream.</param>
        /// <exception cref="NotSupportedException">Any access.</exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException("InflaterInputStream SetLength not supported");
        }

        /// <summary>
        /// Writes a sequence of bytes to stream and advances the current position
        /// This method always throws a NotSupportedException.
        /// </summary>
        /// <param name="buffer">Thew buffer containing data to write.</param>
        /// <param name="offset">The offset of the first byte to write.</param>
        /// <param name="count">The number of bytes to write.</param>
        /// <exception cref="NotSupportedException">Any access.</exception>
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("InflaterInputStream Write not supported");
        }

        /// <summary>
        /// Writes one byte to the current stream and advances the current position
        /// Always throws a NotSupportedException.
        /// </summary>
        /// <param name="value">The byte to write.</param>
        /// <exception cref="NotSupportedException">Any access.</exception>
        public override void WriteByte(byte value)
        {
            throw new NotSupportedException("InflaterInputStream WriteByte not supported");
        }

        /// <summary>
        /// Closes the input stream.  When <see cref="IsStreamOwner"></see>
        /// is true the underlying stream is also closed.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!isClosed)
            {
                isClosed = true;
                if (IsStreamOwner)
                {
                    baseInputStream.Dispose();
                }
            }
        }

        /// <summary>
        /// Reads decompressed data into the provided buffer byte array.
        /// </summary>
        /// <param name ="buffer">
        /// The array to read and decompress data into.
        /// </param>
        /// <param name ="offset">
        /// The offset indicating where the data should be placed.
        /// </param>
        /// <param name ="count">
        /// The number of bytes to decompress.
        /// </param>
        /// <returns>The number of bytes read.  Zero signals the end of stream.</returns>
        /// <exception cref="InvalidDataException">
        /// Inflater needs a dictionary.
        /// </exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Inflater.IsNeedingDictionary)
            {
                throw new InvalidDataException("Need a dictionary");
            }

            int remainingBytes = count;
            while (true)
            {
                int bytesRead = Inflater.Inflate(buffer, offset, remainingBytes);
                offset += bytesRead;
                remainingBytes -= bytesRead;

                if (remainingBytes == 0 || Inflater.IsDone)
                {
                    break;
                }

                if (Inflater.IsNeedingInput)
                {
                    Fill();
                }
                else if (bytesRead == 0)
                {
                    throw new InvalidDataException("Dont know what to do");
                }
            }

            return count - remainingBytes;
        }
        #endregion
    }
}
