using System;
using System.IO;
using System.Security.Cryptography;
using Cave.Compression.Core;

namespace Cave.Compression.Streams
{
    /// <summary>
    /// A special stream deflating or compressing the bytes that are
    /// written to it.  It uses a Deflater to perform actual deflating.<br/>
    /// Authors of the original java version : Tom Tromey, Jochen Hoenicke.
    /// </summary>
    public class DeflaterOutputStream : Stream
    {
        #region Instance Fields

        /// <summary>
        /// This buffer is used temporarily to retrieve the bytes from the
        /// deflater and write them to the underlying output stream.
        /// </summary>
        byte[] buffer;

        bool isClosed;
        #endregion

        /// <summary>
        /// Gets or sets the deflater which is used to deflate the stream.
        /// </summary>
        protected Deflater Deflater { get; set; }

        /// <summary>
        /// Gets or sets the base stream the deflater depends on.
        /// </summary>
        protected Stream BaseOutputStream { get; set; }

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflaterOutputStream"/> class.
        /// </summary>
        /// <remarks>
        /// Creates a new DeflaterOutputStream with a default Deflater and default buffer size.
        /// </remarks>
        /// <param name="baseOutputStream">
        /// the output stream where deflated output should be written.
        /// </param>
        public DeflaterOutputStream(Stream baseOutputStream)
            : this(baseOutputStream, new Deflater(), 512)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflaterOutputStream"/> class.
        /// </summary>
        /// <remarks>
        /// Creates a new DeflaterOutputStream with the given Deflater and
        /// default buffer size.
        /// </remarks>
        /// <param name="baseOutputStream">
        /// the output stream where deflated output should be written.
        /// </param>
        /// <param name="deflater">
        /// the underlying deflater.
        /// </param>
        public DeflaterOutputStream(Stream baseOutputStream, Deflater deflater)
            : this(baseOutputStream, deflater, 512)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflaterOutputStream"/> class.
        /// </summary>
        /// <remarks>
        /// Creates a new DeflaterOutputStream with the given Deflater and
        /// buffer size.
        /// </remarks>
        /// <param name="baseOutputStream">
        /// The output stream where deflated output is written.
        /// </param>
        /// <param name="deflater">
        /// The underlying deflater to use.
        /// </param>
        /// <param name="bufferSize">
        /// The buffer size in bytes to use when deflating (minimum value 512).
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// bufsize is less than or equal to zero.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// baseOutputStream does not support writing.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// deflater instance is null.
        /// </exception>
        public DeflaterOutputStream(Stream baseOutputStream, Deflater deflater, int bufferSize)
        {
            if (baseOutputStream == null)
            {
                throw new ArgumentNullException(nameof(baseOutputStream));
            }

            if (baseOutputStream.CanWrite == false)
            {
                throw new ArgumentException("Must support writing", nameof(baseOutputStream));
            }

            if (bufferSize < 512)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferSize));
            }

            this.BaseOutputStream = baseOutputStream;
            buffer = new byte[bufferSize];
            this.Deflater = deflater ?? throw new ArgumentNullException(nameof(deflater));
        }
        #endregion

        #region Public API

        /// <summary>
        /// Finishes the stream by calling finish() on the deflater.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// Not all input is deflated.
        /// </exception>
        public virtual void Finish()
        {
            Deflater.Finish();
            while (!Deflater.IsFinished)
            {
                var len = Deflater.Deflate(buffer, 0, buffer.Length);
                if (len <= 0)
                {
                    break;
                }

                if (CryptoTransform != null)
                {
                    EncryptBlock(buffer, 0, len);
                }

                BaseOutputStream.Write(buffer, 0, len);
            }

            if (!Deflater.IsFinished)
            {
                throw new InvalidDataException("Can't deflate all input?");
            }

            BaseOutputStream.Flush();
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance will close the underlying stream.
        /// </summary>
        /// <remarks>The default value is true.</remarks>
        public bool IsStreamOwner { get; set; } = true;

        /// <summary>
        /// Gets a value indicating whether an entry can be patched after it was added.
        /// </summary>
        public bool CanPatchEntries
        {
            get
            {
                return BaseOutputStream.CanSeek;
            }
        }

        #endregion

        #region Encryption

        string password;

        /// <summary>
        /// Gets or sets the current <see cref="ICryptoTransform"/> instance.
        /// </summary>
        protected ICryptoTransform CryptoTransform { get; set; }

        /// <summary>
        /// Gets or sets the 10 byte AUTH CODE to be appended immediately following the AES data stream.
        /// </summary>
        protected byte[] AESAuthCode { get; set; }

        /// <summary>
        /// Gets or sets the password used for encryption.
        /// </summary>
        /// <remarks>When set to null or if the password is empty no encryption is performed.</remarks>
        public string Password
        {
            get
            {
                return password;
            }

            set
            {
                if ((value != null) && (value.Length == 0))
                {
                    password = null;
                }
                else
                {
                    password = value;
                }
            }
        }

        /// <summary>
        /// Encrypt a block of data.
        /// </summary>
        /// <param name="buffer">
        /// Data to encrypt.  NOTE the original contents of the buffer are lost.
        /// </param>
        /// <param name="offset">
        /// Offset of first byte in buffer to encrypt.
        /// </param>
        /// <param name="length">
        /// Number of bytes in buffer to encrypt.
        /// </param>
        protected void EncryptBlock(byte[] buffer, int offset, int length)
        {
            CryptoTransform.TransformBlock(buffer, offset, length, buffer, 0);
        }

        #endregion

        #region Deflation Support

        /// <summary>
        /// Deflates everything in the input buffers.  This will call.
        /// <code>def.deflate()</code> until all bytes from the input buffers
        /// are processed.
        /// </summary>
        protected void Deflate()
        {
            while (!Deflater.IsNeedingInput)
            {
                var deflateCount = Deflater.Deflate(buffer, 0, buffer.Length);

                if (deflateCount <= 0)
                {
                    break;
                }

                if (CryptoTransform != null)
                {
                    EncryptBlock(buffer, 0, deflateCount);
                }

                BaseOutputStream.Write(buffer, 0, deflateCount);
            }

            if (!Deflater.IsNeedingInput)
            {
                throw new InvalidDataException("DeflaterOutputStream can't deflate all input?");
            }
        }
        #endregion

        #region Stream Overrides

        /// <summary>
        /// Gets a value indicating whether stream can be read from.
        /// </summary>
        public override bool CanRead
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether seeking is supported for this stream
        /// This property always returns false.
        /// </summary>
        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this stream supports writing.
        /// </summary>
        public override bool CanWrite
        {
            get
            {
                return BaseOutputStream.CanWrite;
            }
        }

        /// <summary>
        /// Gets get current length of stream.
        /// </summary>
        public override long Length
        {
            get
            {
                return BaseOutputStream.Length;
            }
        }

        /// <summary>
        /// Gets or sets the current position within the stream.
        /// </summary>
        /// <exception cref="NotSupportedException">Any attempt to set position.</exception>
        public override long Position
        {
            get
            {
                return BaseOutputStream.Position;
            }

            set
            {
                throw new NotSupportedException("Position property not supported");
            }
        }

        /// <summary>
        /// Sets the current position of this stream to the given value. Not supported by this class!.
        /// </summary>
        /// <param name="offset">The offset relative to the <paramref name="origin"/> to seek.</param>
        /// <param name="origin">The <see cref="SeekOrigin"/> to seek from.</param>
        /// <returns>The new position in the stream.</returns>
        /// <exception cref="NotSupportedException">Any access.</exception>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("DeflaterOutputStream Seek not supported");
        }

        /// <summary>
        /// Sets the length of this stream to the given value. Not supported by this class!.
        /// </summary>
        /// <param name="value">The new stream length.</param>
        /// <exception cref="NotSupportedException">Any access.</exception>
        public override void SetLength(long value)
        {
            throw new NotSupportedException("DeflaterOutputStream SetLength not supported");
        }

        /// <summary>
        /// Read a byte from stream advancing position by one.
        /// </summary>
        /// <returns>The byte read cast to an int.  THe value is -1 if at the end of the stream.</returns>
        /// <exception cref="NotSupportedException">Any access.</exception>
        public override int ReadByte()
        {
            throw new NotSupportedException("DeflaterOutputStream ReadByte not supported");
        }

        /// <summary>
        /// Read a block of bytes from stream.
        /// </summary>
        /// <param name="buffer">The buffer to store read data in.</param>
        /// <param name="offset">The offset to start storing at.</param>
        /// <param name="count">The maximum number of bytes to read.</param>
        /// <returns>The actual number of bytes read.  Zero if end of stream is detected.</returns>
        /// <exception cref="NotSupportedException">Any access.</exception>
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("DeflaterOutputStream Read not supported");
        }

        /// <summary>
        /// Flushes the stream by calling <see cref="Flush">Flush</see> on the deflater and then
        /// on the underlying stream.  This ensures that all bytes are flushed.
        /// </summary>
        public override void Flush()
        {
            Deflater.Flush();
            Deflate();
            BaseOutputStream.Flush();
        }

        /// <summary>
        /// Calls <see cref="Finish"/> and closes the underlying
        /// stream when <see cref="IsStreamOwner"></see> is true.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!isClosed)
            {
                isClosed = true;

                try
                {
                    Finish();
                    if (CryptoTransform != null)
                    {
                        CryptoTransform.Dispose();
                        CryptoTransform = null;
                    }
                }
                finally
                {
                    if (IsStreamOwner)
                    {
                        BaseOutputStream.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Writes a single byte to the compressed output stream.
        /// </summary>
        /// <param name="value">
        /// The byte value.
        /// </param>
        public override void WriteByte(byte value)
        {
            var b = new byte[1];
            b[0] = value;
            Write(b, 0, 1);
        }

        /// <summary>
        /// Writes bytes from an array to the compressed stream.
        /// </summary>
        /// <param name="buffer">
        /// The byte array.
        /// </param>
        /// <param name="offset">
        /// The offset into the byte array where to start.
        /// </param>
        /// <param name="count">
        /// The number of bytes to write.
        /// </param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            Deflater.SetInput(buffer, offset, count);
            Deflate();
        }
        #endregion
    }
}
