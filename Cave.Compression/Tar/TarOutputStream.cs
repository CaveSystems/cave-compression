using System;
using System.IO;
using System.Text;
using Cave.IO;

namespace Cave.Compression.Tar;

/// <summary>
/// The TarOutputStream writes a UNIX tar archive as an OutputStream. Methods are provided to put entries, and then write their contents by writing to this
/// stream using write().
/// </summary>
public class TarOutputStream : Stream
{
    #region Private Fields

    /// <summary>'Assembly' buffer used to assemble data before writing.</summary>
    readonly byte[] assemblyBuffer;

    /// <summary>single block working buffer.</summary>
    readonly byte[] blockBuffer;

    /// <summary>TarBuffer used to provide correct blocking factor.</summary>
    readonly TarBuffer buffer;

    /// <summary>String encoding for names</summary>
    readonly StringEncoding encoding;

    /// <summary>the destination stream for the archive contents.</summary>
    readonly Stream outputStream;

    /// <summary>current 'Assembly' buffer length.</summary>
    int assemblyBufferLength;

    /// <summary>bytes written for this entry so far.</summary>
    long currBytes;

    /// <summary>Size for the current entry.</summary>
    long currSize;

    /// <summary>Flag indicating wether this instance has been closed or not.</summary>
    bool isClosed;

    #endregion Private Fields

    #region Private Properties

    /// <summary>Gets a value indicating whether an entry is open, requiring more data to be written.</summary>
    bool IsEntryOpen => currBytes < currSize;

    #endregion Private Properties

    #region Private Methods

    /// <summary>Write an EOF (end of archive) block to the tar archive. The end of the archive is indicated by two blocks consisting entirely of zero bytes.</summary>
    void WriteEofBlock()
    {
        Array.Clear(blockBuffer, 0, blockBuffer.Length);
        buffer.WriteBlock(blockBuffer);
        buffer.WriteBlock(blockBuffer);
    }

    #endregion Private Methods

    #region Protected Methods

    /// <summary>Ends the TAR archive and closes the underlying OutputStream.</summary>
    /// <remarks>This means that Finish() is called followed by calling the TarBuffer's Close().</remarks>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (!isClosed)
        {
            isClosed = true;
            Finish();
            buffer.Close();
        }
        base.Dispose(disposing);
    }

    #endregion Protected Methods

    #region Public Constructors

    /// <summary>Initializes a new instance of the <see cref="TarOutputStream"/> class.</summary>
    /// <param name="outputStream">Stream to write to.</param>
    public TarOutputStream(Stream outputStream)
        : this(outputStream, StringEncoding.UTF_8, TarBuffer.DefaultBlockFactor)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TarOutputStream"/> class.</summary>
    /// <param name="outputStream">Stream to write to.</param>
    /// <param name="blockFactor">blocking factor.</param>
    public TarOutputStream(Stream outputStream, int blockFactor) : this(outputStream, StringEncoding.UTF_8, blockFactor)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TarOutputStream"/> class.</summary>
    /// <param name="outputStream">Stream to write to.</param>
    /// <param name="encoding">Encoding used for names</param>
    public TarOutputStream(Stream outputStream, StringEncoding encoding) : this(outputStream, encoding, TarBuffer.DefaultBlockFactor)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TarOutputStream"/> class.</summary>
    /// <param name="outputStream">Stream to write to.</param>
    /// <param name="encoding">Encoding used for names</param>
    /// <param name="blockFactor">Blocking factor.</param>
    public TarOutputStream(Stream outputStream, StringEncoding encoding, int blockFactor)
    {
        this.outputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
        buffer = TarBuffer.CreateOutputTarBuffer(outputStream, blockFactor);
        this.encoding = encoding;
        assemblyBuffer = new byte[TarBuffer.BlockSize];
        blockBuffer = new byte[TarBuffer.BlockSize];
    }

    #endregion Public Constructors

    #region Public Properties

    /// <summary>Gets a value indicating whether the stream supports reading; otherwise, false.</summary>
    public override bool CanRead => outputStream.CanRead;

    /// <summary>Gets a value indicating whether the stream supports seeking; otherwise, false.</summary>
    public override bool CanSeek => outputStream.CanSeek;

    /// <summary>Gets a value indicating whether the stream supports writing; otherwise, false.</summary>
    public override bool CanWrite => outputStream.CanWrite;

    /// <summary>Gets or sets a value indicating whether the underlying stream shall be closed by this instance.</summary>
    /// <remarks>The default value is true.</remarks>
    public bool IsStreamOwner
    {
        get => buffer.IsStreamOwner;
        set => buffer.IsStreamOwner = value;
    }

    /// <summary>Gets length of stream in bytes.</summary>
    public override long Length => outputStream.Length;

    /// <summary>Gets or sets the position within the current stream.</summary>
    public override long Position
    {
        get => outputStream.Position;
        set => outputStream.Position = value;
    }

    /// <summary>Gets the record size being used by this stream's TarBuffer.</summary>
    public int RecordSize => buffer.RecordSize;

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Close an entry. This method MUST be called for all file entries that contain data. The reason is that we must buffer data written to the stream in order
    /// to satisfy the buffer's block based writes. Thus, there may be data fragments still being assembled that must be written to the output stream before
    /// this entry is closed and the next entry written.
    /// </summary>
    public void CloseEntry()
    {
        if (assemblyBufferLength > 0)
        {
            Array.Clear(assemblyBuffer, assemblyBufferLength, assemblyBuffer.Length - assemblyBufferLength);

            buffer.WriteBlock(assemblyBuffer);

            currBytes += assemblyBufferLength;
            assemblyBufferLength = 0;
        }

        if (currBytes < currSize)
        {
            var errorText = string.Format("Entry closed at '{0}' before the '{1}' bytes specified in the header were written", currBytes, currSize);
            throw new InvalidOperationException(errorText);
        }
    }

    /// <summary>Ends the TAR archive without closing the underlying OutputStream. The result is that the EOF block of nulls is written.</summary>
    public void Finish()
    {
        if (IsEntryOpen)
        {
            CloseEntry();
        }

        WriteEofBlock();
    }

    /// <summary>All buffered data is written to destination.</summary>
    public override void Flush() => outputStream.Flush();

    /// <summary>
    /// Put an entry on the output stream. This writes the entry's header and positions the output stream for writing the contents of the entry. Once this
    /// method is called, the stream is ready for calls to write() to write the entry's contents. Once the contents are written, closeEntry() <B>MUST</B> be
    /// called to ensure that all buffered data is completely written to the output stream.
    /// </summary>
    /// <param name="entry">The TarEntry to be written to the archive.</param>
    public void PutNextEntry(TarEntry entry)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var nameBytes = encoding.Encode(entry.TarHeader.Name);
        if (nameBytes.Length > TarHeader.NameLength)
        {
            var longHeader = new TarHeader
            {
                TypeFlag = TarEntryType.LongName,
            };
            longHeader.Name += "././@LongLink";
            longHeader.Mode = 420; // 644 by default
            longHeader.UserId = entry.UserId;
            longHeader.GroupId = entry.GroupId;
            longHeader.GroupName = entry.GroupName;
            longHeader.UserName = entry.UserName;
            longHeader.LinkName = string.Empty;
            longHeader.Size = nameBytes.Length + 1;  // Plus one to avoid dropping last char

            longHeader.WriteHeader(encoding, blockBuffer);
            buffer.WriteBlock(blockBuffer);  // Add special long filename header block

            var nameLength = nameBytes.Length + 1;
            var nameIndex = 0;
            while (nameIndex < nameLength)
            {
                Array.Clear(blockBuffer, 0, blockBuffer.Length);
                nameIndex += TarHeader.WriteBytes(nameBytes, nameIndex, blockBuffer, 0, TarBuffer.BlockSize, true);
                buffer.WriteBlock(blockBuffer);
            }

            entry.WriteEntryHeader(encoding, blockBuffer, $"{Guid.NewGuid()}");
            buffer.WriteBlock(blockBuffer);
        }
        else
        {
            entry.WriteEntryHeader(encoding, blockBuffer);
            buffer.WriteBlock(blockBuffer);
        }
        currBytes = 0;
        currSize = entry.IsDirectory ? 0 : entry.Size;
    }

    /// <summary>read bytes from the current stream and advance the position within the stream by the number of bytes read.</summary>
    /// <param name="buffer">The buffer to store read bytes in.</param>
    /// <param name="offset">The index into the buffer to being storing bytes at.</param>
    /// <param name="count">The desired number of bytes to read.</param>
    /// <returns>
    /// The total number of bytes read, or zero if at the end of the stream. The number of bytes may be less than the <paramref name="count">count</paramref>
    /// requested if data is not avialable.
    /// </returns>
    public override int Read(byte[] buffer, int offset, int count) => outputStream.Read(buffer, offset, count);

    /// <summary>Read a byte from the stream and advance the position within the stream by one byte or returns -1 if at the end of the stream.</summary>
    /// <returns>The byte value or -1 if at end of stream.</returns>
    public override int ReadByte() => outputStream.ReadByte();

    /// <summary>set the position within the current stream.</summary>
    /// <param name="offset">The offset relative to the <paramref name="origin"/> to seek to.</param>
    /// <param name="origin">The <see cref="SeekOrigin"/> to seek from.</param>
    /// <returns>The new position in the stream.</returns>
    public override long Seek(long offset, SeekOrigin origin) => outputStream.Seek(offset, origin);

    /// <summary>Set the length of the current stream.</summary>
    /// <param name="value">The new stream length.</param>
    public override void SetLength(long value) => outputStream.SetLength(value);

    /// <summary>
    /// Writes bytes to the current tar archive entry. This method is aware of the current entry and will throw an exception if you attempt to write bytes past
    /// the length specified for the current entry. The method is also (painfully) aware of the record buffering required by TarBuffer, and manages buffers that
    /// are not a multiple of recordsize in length, including assembling records from small buffers.
    /// </summary>
    /// <param name="buffer">The buffer to write to the archive.</param>
    /// <param name="offset">The offset in the buffer from which to get bytes.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Cannot be negative");
        }

        if (buffer.Length - offset < count)
        {
            throw new ArgumentException("offset and count combination is invalid");
        }

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Cannot be negative");
        }

        if ((currBytes + count) > currSize)
        {
            var errorText = string.Format("request to write '{0}' bytes exceeds size in header of '{1}' bytes", count, currSize);
            throw new ArgumentOutOfRangeException(nameof(count), errorText);
        }

        // We have to deal with assembly!!! The programmer can be writing little 32 byte chunks for all we know, and we must assemble complete blocks for
        // writing. TODO REVIEW Maybe this should be in TarBuffer? Could that help to eliminate some of the buffer copying.
        if (assemblyBufferLength > 0)
        {
            if ((assemblyBufferLength + count) >= blockBuffer.Length)
            {
                var aLen = blockBuffer.Length - assemblyBufferLength;

                Array.Copy(assemblyBuffer, 0, blockBuffer, 0, assemblyBufferLength);
                Array.Copy(buffer, offset, blockBuffer, assemblyBufferLength, aLen);

                this.buffer.WriteBlock(blockBuffer);

                currBytes += blockBuffer.Length;

                offset += aLen;
                count -= aLen;

                assemblyBufferLength = 0;
            }
            else
            {
                Array.Copy(buffer, offset, assemblyBuffer, assemblyBufferLength, count);
                offset += count;
                assemblyBufferLength += count;
                count -= count;
            }
        }

        // When we get here we have EITHER: o An empty "assembly" buffer. o No bytes to write (count == 0)
        while (count > 0)
        {
            if (count < blockBuffer.Length)
            {
                Array.Copy(buffer, offset, assemblyBuffer, assemblyBufferLength, count);
                assemblyBufferLength += count;
                break;
            }

            this.buffer.WriteBlock(buffer, offset);

            var bufferLength = blockBuffer.Length;
            currBytes += bufferLength;
            count -= bufferLength;
            offset += bufferLength;
        }
    }

    /// <summary>Writes a byte to the current tar archive entry. This method simply calls Write(byte[], int, int).</summary>
    /// <param name="value">The byte to be written.</param>
    public override void WriteByte(byte value) => Write([value], 0, 1);

    #endregion Public Methods
}
