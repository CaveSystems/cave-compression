using System;
using System.IO;
using System.Text;
using Cave.IO;

namespace Cave.Compression.Tar;

/// <summary>
/// The TarInputStream reads a UNIX tar archive as an InputStream. methods are provided to position at each successive entry in the archive, and the read each
/// entry as a normal input stream using read().
/// </summary>
public class TarInputStream : Stream
{
    #region Private Fields

    /// <summary>String encoding for names</summary>
    readonly StringEncoding encoding;

    /// <summary>Stream used as the source of input data.</summary>
    readonly Stream inputStream;

    /// <summary>Working buffer.</summary>
    readonly TarBuffer tarBuffer;

    /// <summary>Current entry being read.</summary>
    TarEntry? currentEntry;

    /// <summary>Factory used to create TarEntry or descendant class instance.</summary>
    IEntryFactory? entryFactory;

    /// <summary>Number of bytes read for this entry so far.</summary>
    long entryOffset;

    /// <summary>Size of this entry as recorded in header.</summary>
    long entrySize;

    /// <summary>Eof block counter, needs 2 eof blocks in a row for eof mark.</summary>
    int eofBlockNumber;

    /// <summary>
    /// Buffer used with calls to.
    /// <code>Read()</code>
    /// </summary>
    byte[]? readBuffer;

    #endregion Private Fields

    #region Private Methods

    string GetExtendedHeaderName()
    {
        var data = ReadStringData();
        var header = TarExtendedHeader.Parse(data);
        return header.Path;
    }

    string ReadStringData()
    {
        var sb = new StringBuilder();
        var nameBuffer = new byte[TarBuffer.BlockSize];
        var numToRead = entrySize;
        while (numToRead > 0)
        {
            var numRead = Read(nameBuffer, 0, numToRead > nameBuffer.Length ? nameBuffer.Length : (int)numToRead);
            if (numRead == -1)
            {
                throw new InvalidDataException("Failed to read long name entry");
            }

            sb.Append(TarHeader.ParseName(encoding, nameBuffer, 0, numRead));
            numToRead -= numRead;
        }

        SkipToNextEntry();
        return sb.ToString();
    }

    void SkipToNextEntry()
    {
        var numToSkip = entrySize - entryOffset;

        if (numToSkip > 0)
        {
            Skip(numToSkip);
        }

        readBuffer = null;
    }

    #endregion Private Methods

    #region Protected Methods

    /// <summary>Closes this stream. Calls the TarBuffer's close() method. The underlying stream is closed by the TarBuffer.</summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tarBuffer.Close();
        }
        base.Dispose(disposing);
    }

    #endregion Protected Methods

    #region Public Classes

    /// <summary>Standard entry factory class creating instances of the class TarEntry.</summary>
    public class EntryFactoryAdapter : IEntryFactory
    {
        #region Public Methods

        /// <inheritdoc/>
        public TarEntry CreateEntry(StringEncoding encoding, string name) => TarEntry.CreateTarEntry(name);

        /// <inheritdoc/>
        public TarEntry CreateEntry(StringEncoding encoding, byte[] headerBuffer) => new(encoding, headerBuffer);

        /// <inheritdoc/>
        public TarEntry CreateEntryFromFile(StringEncoding encoding, string fileName) => TarEntry.CreateEntryFromFile(fileName);

        #endregion Public Methods
    }

    #endregion Public Classes

    #region Public Constructors

    /// <summary>Initializes a new instance of the <see cref="TarInputStream"/> class.</summary>
    /// <param name="inputStream">stream to source data from.</param>
    public TarInputStream(Stream inputStream)
        : this(inputStream, StringEncoding.UTF_8, TarBuffer.DefaultBlockFactor)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TarInputStream"/> class.</summary>
    /// <param name="inputStream">stream to source data from.</param>
    /// <param name="blockFactor">block factor to apply to archive.</param>
    public TarInputStream(Stream inputStream, int blockFactor)
        : this(inputStream, StringEncoding.UTF_8, blockFactor)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TarInputStream"/> class.</summary>
    /// <param name="inputStream">stream to source data from.</param>
    /// <param name="encoding">Encoding used for names</param>
    public TarInputStream(Stream inputStream, StringEncoding encoding)
        : this(inputStream, encoding, TarBuffer.DefaultBlockFactor)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TarInputStream"/> class.</summary>
    /// <param name="inputStream">stream to source data from.</param>
    /// <param name="encoding">Encoding used for names</param>
    /// <param name="blockFactor">block factor to apply to archive.</param>
    public TarInputStream(Stream inputStream, StringEncoding encoding, int blockFactor)
    {
        this.inputStream = inputStream;
        this.encoding = encoding;
        tarBuffer = TarBuffer.CreateInputTarBuffer(inputStream, blockFactor);
    }

    #endregion Public Constructors

    #region Public Interfaces

    /// <summary>
    /// This interface is provided, along with the method <see cref="SetEntryFactory"/>, to allow the programmer to have their own <see cref="TarEntry"/>
    /// subclass instantiated for the entries return from <see cref="GetNextEntry"/>.
    /// </summary>
    public interface IEntryFactory
    {
        #region Public Methods

        /// <summary>Create an entry based on name alone.</summary>
        /// <param name="encoding">Encoding to be used for names</param>
        /// <param name="name">Name of the new EntryPointNotFoundException to create.</param>
        /// <returns>created TarEntry or descendant class.</returns>
        TarEntry CreateEntry(StringEncoding encoding, string name);

        /// <summary>Create a tar entry based on the header information passed.</summary>
        /// <param name="encoding">Encoding to be used for names</param>
        /// <param name="headerBuffer">Buffer containing header information to create an an entry from.</param>
        /// <returns>Created TarEntry or descendant class.</returns>
        TarEntry CreateEntry(StringEncoding encoding, byte[] headerBuffer);

        /// <summary>Create an instance based on an actual file.</summary>
        /// <param name="encoding">Encoding to be used for names</param>
        /// <param name="fileName">Name of file to represent in the entry.</param>
        /// <returns>Created TarEntry or descendant class.</returns>
        TarEntry CreateEntryFromFile(StringEncoding encoding, string fileName);

        #endregion Public Methods
    }

    #endregion Public Interfaces

    #region Public Properties

    /// <summary>
    /// Gets the available data that can be read from the current entry in the archive. This does not indicate how much data is left in the entire archive, only
    /// in the current entry. This value is determined from the entry's size header field and the amount of data already read from the current entry.
    /// </summary>
    /// <returns>The number of available bytes for the current entry.</returns>
    public long Available => entrySize - entryOffset;

    /// <summary>Gets a value indicating whether the current stream supports reading.</summary>
    public override bool CanRead => inputStream.CanRead;

    /// <summary>Gets a value indicating whether the current stream supports seeking This property always returns false.</summary>
    public override bool CanSeek => false;

    /// <summary>Gets a value indicating whether the stream supports writing. This property always returns false.</summary>
    public override bool CanWrite => false;

    /// <summary>Gets a value indicating whether marking is supported; false otherwise.</summary>
    /// <remarks>Currently marking is not supported, the return value is always false.</remarks>
    public bool IsMarkSupported => false;

    /// <summary>Gets or sets a value indicating whether the underlying stream shall be closed by this instance.</summary>
    /// <remarks>The default value is true.</remarks>
    public bool IsStreamOwner
    {
        get => tarBuffer.IsStreamOwner;
        set => tarBuffer.IsStreamOwner = value;
    }

    /// <summary>Gets the length in bytes of the stream.</summary>
    public override long Length => inputStream.Length;

    /// <summary>Gets or sets the position within the stream. Setting the Position is not supported and throws a NotSupportedExceptionNotSupportedException.</summary>
    /// <exception cref="NotSupportedException">Any attempt to set position.</exception>
    public override long Position
    {
        get => inputStream.Position;

        set => throw new NotSupportedException("TarInputStream Seek not supported");
    }

    /// <summary>Gets the record size being used by this stream's TarBuffer.</summary>
    public int RecordSize => tarBuffer.RecordSize;

    #endregion Public Properties

    #region Public Methods

    /// <summary>Copies the contents of the current tar archive entry directly into an output stream.</summary>
    /// <param name="outputStream">The OutputStream into which to write the entry's data.</param>
    /// <param name="callback">Callback to be called during copy or null.</param>
    /// <param name="userItem">An user item for the callback.</param>
    public void CopyEntryContents(Stream outputStream, ProgressCallback? callback = null, object? userItem = null) => this.CopyBlocksTo(outputStream, entrySize, callback, userItem);

    /// <summary>Flushes the baseInputStream.</summary>
    public override void Flush() => inputStream.Flush();

    /// <summary>
    /// Get the next entry in this tar archive. This will skip over any remaining data in the current entry, if there is one, and place the input stream at the
    /// header of the next entry, and read the header and instantiate a new TarEntry from the header bytes and return that entry. If there are no more entries
    /// in the archive, null will be returned to indicate that the end of the archive has been reached.
    /// </summary>
    /// <returns>The next TarEntry in the archive, or null.</returns>
    public TarEntry? GetNextEntry()
    {
        if (eofBlockNumber >= 2)
        {
            return null;
        }

        if (currentEntry != null)
        {
            SkipToNextEntry();
        }

        var headerBuf = tarBuffer.ReadBlock();

        if (headerBuf == null)
        {
            throw new EndOfStreamException();
        }
        else
        {
            if (TarBuffer.IsEndOfArchiveBlock(headerBuf))
            {
                eofBlockNumber++;
                currentEntry = null;
                return GetNextEntry();
            }
        }

        eofBlockNumber = 0;
        try
        {
            var longName = string.Empty;
            for (; ; )
            {
                var header = new TarHeader();
                header.ParseBuffer(encoding, headerBuf);
                if (!header.IsChecksumValid)
                {
                    throw new InvalidDataException("Header checksum is invalid");
                }

                entryOffset = 0;
                entrySize = header.Size;

                switch (header.TypeFlag)
                {
                    case TarEntryType.LongName:
                    {
                        longName = ReadStringData();
                        headerBuf = tarBuffer.ReadBlock();
                        continue;
                    }

                    case TarEntryType.ExtendedHeader:
                    {
                        longName = GetExtendedHeaderName();
                        headerBuf = tarBuffer.ReadBlock();
                        continue;
                    }

                    case TarEntryType.NormalFile:
                    case TarEntryType.OldNormalFile:
                    case TarEntryType.Link:
                    case TarEntryType.Symlink:
                    case TarEntryType.Directory:
                        break;

                    default:
                    {
                        // Ignore things we dont understand completely for now
                        SkipToNextEntry();
                        headerBuf = tarBuffer.ReadBlock();
                        continue;
                    }
                }

                if (entryFactory == null)
                {
                    currentEntry = new TarEntry(encoding, headerBuf);
                    if (longName.Length > 0)
                    {
                        currentEntry.Name = longName.ToString();
                    }
                }
                else
                {
                    currentEntry = entryFactory.CreateEntry(encoding, headerBuf);
                }

                // Magic was checked here for 'ustar' but there are multiple valid possibilities so this is not done anymore.
                entryOffset = 0;

                // TODO: Review How do we resolve this discrepancy?!
                entrySize = currentEntry.Size;

                break;
            }
        }
        catch (InvalidDataException ex)
        {
            entrySize = 0;
            entryOffset = 0;
            currentEntry = null;
            var errorText = string.Format("Bad header in record {0} block {1} {2}", tarBuffer.CurrentRecord, tarBuffer.CurrentBlock, ex.Message);
            throw new InvalidDataException(errorText);
        }

        return currentEntry;
    }

    /// <summary>Since we do not support marking just yet, we do nothing.</summary>
    /// <param name="markLimit">The limit to mark.</param>
    public void Mark(int markLimit)
    {
    }

    /// <summary>
    /// Reads bytes from the current tar archive entry.
    ///
    /// This method is aware of the boundaries of the current entry in the archive and will deal with them appropriately.
    /// </summary>
    /// <param name="buffer">The buffer into which to place bytes read.</param>
    /// <param name="offset">The offset at which to place bytes read.</param>
    /// <param name="count">The number of bytes to read.</param>
    /// <returns>The number of bytes read, or 0 at end of stream/EOF.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        var totalRead = 0;

        if (entryOffset >= entrySize)
        {
            return 0;
        }

        long numToRead = count;

        if ((numToRead + entryOffset) > entrySize)
        {
            numToRead = entrySize - entryOffset;
        }

        if (readBuffer != null)
        {
            var sz = (numToRead > readBuffer.Length) ? readBuffer.Length : (int)numToRead;

            Array.Copy(readBuffer, 0, buffer, offset, sz);

            if (sz >= readBuffer.Length)
            {
                readBuffer = null;
            }
            else
            {
                var newLen = readBuffer.Length - sz;
                var newBuf = new byte[newLen];
                Array.Copy(readBuffer, sz, newBuf, 0, newLen);
                readBuffer = newBuf;
            }

            totalRead += sz;
            numToRead -= sz;
            offset += sz;
        }

        while (numToRead > 0)
        {
            var rec = tarBuffer.ReadBlock() ?? throw new InvalidDataException("unexpected EOF with " + numToRead + " bytes unread");
            var sz = (int)numToRead;
            var recLen = rec.Length;

            if (recLen > sz)
            {
                Array.Copy(rec, 0, buffer, offset, sz);
                readBuffer = new byte[recLen - sz];
                Array.Copy(rec, sz, readBuffer, 0, recLen - sz);
            }
            else
            {
                sz = recLen;
                Array.Copy(rec, 0, buffer, offset, recLen);
            }

            totalRead += sz;
            numToRead -= sz;
            offset += sz;
        }

        entryOffset += totalRead;

        return totalRead;
    }

    /// <summary>Reads a byte from the current tar archive entry.</summary>
    /// <returns>A byte cast to an int; -1 if the at the end of the stream.</returns>
    public override int ReadByte()
    {
        var oneByteBuffer = new byte[1];
        var num = Read(oneByteBuffer, 0, 1);
        if (num <= 0)
        {
            // return -1 to indicate that no byte was read.
            return -1;
        }

        return oneByteBuffer[0];
    }

    /// <summary>Since we do not support marking just yet, we do nothing.</summary>
    public void Reset()
    {
    }

    /// <summary>Set the streams position. This operation is not supported and will throw a NotSupportedException.</summary>
    /// <param name="offset">The offset relative to the origin to seek to.</param>
    /// <param name="origin">The <see cref="SeekOrigin"/> to start seeking from.</param>
    /// <returns>The new position in the stream.</returns>
    /// <exception cref="NotSupportedException">Any access.</exception>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("TarInputStream Seek not supported");

    /// <summary>Set the entry factory for this instance.</summary>
    /// <param name="factory">The factory for creating new entries.</param>
    public void SetEntryFactory(IEntryFactory factory) => entryFactory = factory;

    /// <summary>Sets the length of the stream This operation is not supported and will throw a NotSupportedException.</summary>
    /// <param name="value">The new stream length.</param>
    /// <exception cref="NotSupportedException">Any access.</exception>
    public override void SetLength(long value) => throw new NotSupportedException("TarInputStream SetLength not supported");

    /// <summary>
    /// Skip bytes in the input buffer. This skips bytes in the current entry's data, not the entire archive, and will stop at the end of the current entry's
    /// data if the number to skip extends beyond that point.
    /// </summary>
    /// <param name="skipCount">The number of bytes to skip.</param>
    public void Skip(long skipCount)
    {
        // TODO: REVIEW efficiency of TarInputStream.Skip This is horribly inefficient, but it ensures that we properly skip over bytes via the TarBuffer...
        var skipBuf = new byte[8 * 1024];

        for (var num = skipCount; num > 0;)
        {
            var toRead = num > skipBuf.Length ? skipBuf.Length : (int)num;
            var numRead = Read(skipBuf, 0, toRead);

            if (numRead == -1)
            {
                break;
            }

            num -= numRead;
        }
    }

    /// <summary>Writes a block of bytes to this stream using data from a buffer. This operation is not supported and will throw a NotSupportedException.</summary>
    /// <param name="buffer">The buffer containing bytes to write.</param>
    /// <param name="offset">The offset in the buffer of the frist byte to write.</param>
    /// <param name="count">The number of bytes to write.</param>
    /// <exception cref="NotSupportedException">Any access.</exception>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("TarInputStream Write not supported");

    /// <summary>Writes a byte to the current position in the file stream. This operation is not supported and will throw a NotSupportedException.</summary>
    /// <param name="value">The byte value to write.</param>
    /// <exception cref="NotSupportedException">Any access.</exception>
    public override void WriteByte(byte value) => throw new NotSupportedException("TarInputStream WriteByte not supported");

    #endregion Public Methods
}
