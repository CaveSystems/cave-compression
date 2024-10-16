﻿using System;
using System.IO;
using System.Security.Cryptography;
using Cave.Compression.Core;

namespace Cave.Compression.Streams;

/// <summary>An input buffer customised for use by <see cref="InflaterInputStream"/>.</summary>
/// <remarks>The buffer supports decryption of incoming data.</remarks>
public class InflaterInputBuffer
{
    #region Private Fields

    readonly Stream inputStream;
    ICryptoTransform? cryptoTransform;
    byte[]? internalClearText;

    #endregion Private Fields

    #region Public Constructors

    /// <summary>Initializes a new instance of the <see cref="InflaterInputBuffer"/> class.</summary>
    /// <param name="stream">The stream to buffer.</param>
    public InflaterInputBuffer(Stream stream)
        : this(stream, 4096)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InflaterInputBuffer"/> class.</summary>
    /// <param name="stream">The stream to buffer.</param>
    /// <param name="bufferSize">The size to use for the buffer.</param>
    /// <remarks>A minimum buffer size of 1KB is permitted. Lower sizes are treated as 1KB.</remarks>
    public InflaterInputBuffer(Stream stream, int bufferSize)
    {
        inputStream = stream;
        if (bufferSize < 1024)
        {
            bufferSize = 1024;
        }

        RawData = new byte[bufferSize];
        ClearText = RawData;
    }

    #endregion Public Constructors

    #region Public Properties

    /// <summary>Gets or sets the number of bytes available.</summary>
    public int Available { get; set; }

    /// <summary>Gets or sets the contents of the clear text buffer.</summary>
    public byte[] ClearText { get; set; }

    /// <summary>Gets the number of useable bytes in <see cref="ClearText"/>.</summary>
    public int ClearTextLength { get; private set; }

    /// <summary>Gets or sets the <see cref="ICryptoTransform"/> to apply to any data.</summary>
    /// <remarks>Set this value to null to have no transform applied.</remarks>
    public ICryptoTransform? CryptoTransform
    {
        get => cryptoTransform;
        set
        {
            cryptoTransform = value;
            if (cryptoTransform != null)
            {
                if (RawData == ClearText)
                {
                    internalClearText ??= new byte[RawData.Length];
                    ClearText = internalClearText;
                }

                ClearTextLength = RawLength;
                if (Available > 0)
                {
                    cryptoTransform.TransformBlock(RawData, RawLength - Available, Available, ClearText, RawLength - Available);
                }
            }
            else
            {
                ClearText = RawData;
                ClearTextLength = RawLength;
            }
        }
    }

    /// <summary>Gets or sets the contents of the raw data buffer.</summary>
    /// <remarks>This may contain encrypted data.</remarks>
    public byte[] RawData { get; set; }

    /// <summary>Gets or sets the length of bytes bytes in the <see cref="RawData"/>.</summary>
    public int RawLength { get; set; }

    #endregion Public Properties

    #region Public Methods

    /// <summary>Fill the buffer from the underlying input stream.</summary>
    public void Fill()
    {
        RawLength = 0;
        var toRead = RawData.Length;

        while (toRead > 0)
        {
            var count = inputStream.Read(RawData, RawLength, toRead);
            if (count <= 0)
            {
                break;
            }

            RawLength += count;
            toRead -= count;
        }

        if (cryptoTransform != null)
        {
            ClearTextLength = cryptoTransform.TransformBlock(RawData, 0, RawLength, ClearText, 0);
        }
        else
        {
            ClearTextLength = RawLength;
        }

        Available = ClearTextLength;
    }

    /// <summary>Read clear text data from the input stream.</summary>
    /// <param name="outBuffer">The buffer to add data to.</param>
    /// <param name="offset">The offset to start adding data at.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>Returns the number of bytes actually read.</returns>
    public int ReadClearTextBuffer(byte[] outBuffer, int offset, int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var currentOffset = offset;
        var currentLength = length;

        while (currentLength > 0)
        {
            if (Available <= 0)
            {
                Fill();
                if (Available <= 0)
                {
                    return 0;
                }
            }

            var toCopy = Math.Min(currentLength, Available);
            Array.Copy(ClearText, ClearTextLength - Available, outBuffer, currentOffset, toCopy);
            currentOffset += toCopy;
            currentLength -= toCopy;
            Available -= toCopy;
        }

        return length;
    }

    /// <summary>Read a <see cref="byte"/> from the input stream.</summary>
    /// <returns>Returns the byte read.</returns>
    public int ReadLeByte()
    {
        if (Available <= 0)
        {
            Fill();
            if (Available <= 0)
            {
                throw new InvalidDataException("EOF in header");
            }
        }

        var result = RawData[RawLength - Available];
        Available -= 1;
        return result;
    }

    /// <summary>Read an <see cref="int"/> in little endian byte order.</summary>
    /// <returns>The int value read.</returns>
    public int ReadLeInt() => ReadLeShort() | (ReadLeShort() << 16);

    /// <summary>Read a <see cref="long"/> in little endian byte order.</summary>
    /// <returns>The long value read.</returns>
    public long ReadLeLong() => (uint)ReadLeInt() | ((long)ReadLeInt() << 32);

    /// <summary>Read an <see cref="short"/> in little endian byte order.</summary>
    /// <returns>The short value read case to an int.</returns>
    public int ReadLeShort() => ReadLeByte() | (ReadLeByte() << 8);

    /// <summary>Read a buffer directly from the input stream.</summary>
    /// <param name="buffer">The buffer to fill.</param>
    /// <returns>Returns the number of bytes read.</returns>
    public int ReadRawBuffer(byte[] buffer) => ReadRawBuffer(buffer, 0, buffer.Length);

    /// <summary>Read a buffer directly from the input stream.</summary>
    /// <param name="outBuffer">The buffer to read into.</param>
    /// <param name="offset">The offset to start reading data into.</param>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>Returns the number of bytes read.</returns>
    public int ReadRawBuffer(byte[] outBuffer, int offset, int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var currentOffset = offset;
        var currentLength = length;

        while (currentLength > 0)
        {
            if (Available <= 0)
            {
                Fill();
                if (Available <= 0)
                {
                    return 0;
                }
            }

            var toCopy = Math.Min(currentLength, Available);
            Array.Copy(RawData, RawLength - Available, outBuffer, currentOffset, toCopy);
            currentOffset += toCopy;
            currentLength -= toCopy;
            Available -= toCopy;
        }

        return length;
    }

    /// <summary>Call <see cref="Inflater.SetInput(byte[], int, int)"/> passing the current clear text buffer contents.</summary>
    /// <param name="inflater">The inflater to set input for.</param>
    public void SetInflaterInput(Inflater inflater)
    {
        if (Available > 0)
        {
            inflater.SetInput(ClearText, ClearTextLength - Available, Available);
            Available = 0;
        }
    }

    #endregion Public Methods
}
