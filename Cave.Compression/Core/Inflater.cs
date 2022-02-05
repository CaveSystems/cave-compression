using System;
using System.IO;
using Cave.Compression.Checksum;
using Cave.Compression.Streams;

namespace Cave.Compression.Core
{
    /// <summary>
    /// Inflater is used to decompress data that has been compressed according
    /// to the "deflate" standard described in rfc1951.
    ///
    /// By default Zlib (rfc1950) headers and footers are expected in the input.
    /// You can use constructor. <code> public Inflater(bool noHeader)</code> passing true
    /// if there is no Zlib header information
    ///
    /// The usage is as following.  First you have to set some input with.
    /// <code>SetInput()</code>, then Inflate() it.  If inflate doesn't
    /// inflate any bytes there may be three reasons:
    /// <ul>
    /// <li>IsNeedingInput() returns true because the input buffer is empty.
    /// You have to provide more input with <code>SetInput()</code>.
    /// NOTE: IsNeedingInput() also returns true when, the stream is State.Done.
    /// </li>
    /// <li>IsNeedingDictionary() returns true, you have to provide a preset
    ///    dictionary with <code>SetDictionary()</code>.</li>
    /// <li>IsState.Done returns true, the inflater has State.Done.</li>
    /// </ul>
    /// Once the first output byte is produced, a dictionary will not be
    /// needed at a later stage.
    ///
    /// author of the original java version : John Leuner, Jochen Hoenicke.
    /// </summary>
    public class Inflater
    {
        #region Constants/Readonly

        /// <summary>
        /// Copy lengths for literal codes 257..285.
        /// </summary>
        static readonly int[] CPLENS = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };

        /// <summary>
        /// Extra bits for literal codes 257..285.
        /// </summary>
        static readonly int[] CPLEXT = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };

        /// <summary>
        /// Copy offsets for distance codes 0..29.
        /// </summary>
        static readonly int[] CPDIST = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };

        /// <summary>
        /// Extra bits for distance codes.
        /// </summary>
        static readonly int[] CPDEXT = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };

        /// <summary>
        /// These are the possible states for an inflater.
        /// </summary>
        enum State
        {
            Header,
            Dictionary,
            Blocks,
            StoredLen1,
            StoredLen2,
            Stored,
            DynamicHeader,
            Huffman,
            HuffmanLenBits,
            HuffmanDist,
            HuffmanDistBits,
            Checksum,
            Done,
        }

        #endregion

        #region Instance Fields

        /// <summary>
        /// This variable stores the noHeader flag that was given to the constructor.
        /// True means, that the inflated stream doesn't contain a Zlib header or
        /// footer.
        /// </summary>
        readonly bool noHeader;

        readonly StreamManipulator input;

        /// <summary>
        /// This variable contains the current state.
        /// </summary>
        State mode;

        /// <summary>
        /// The adler checksum of the dictionary or of the decompressed
        /// stream, as it is written in the header resp. footer of the
        /// compressed stream.
        /// Only valid if mode is State.Dictionary or State.Checksum.
        /// </summary>
        int readAdler;

        /// <summary>
        /// The number of bits needed to complete the current state.  This
        /// is valid, if mode is State.Dictionary, State.Checksum,
        /// State.HuffmanLenBits or State.HuffmanDistBits.
        /// </summary>
        int neededBits;
        int repLength;
        int repDist;
        int uncomprLen;

        /// <summary>
        /// True, if the last block flag was set in the last block of the
        /// inflated stream.  This means that the stream ends after the
        /// current block.
        /// </summary>
        bool isLastBlock;

        /// <summary>
        /// The total number of bytes set with setInput().  This is not the
        /// value returned by the TotalIn property, since this also includes the
        /// unprocessed input.
        /// </summary>
        long totalIn;

        OutputWindow outputWindow;
        InflaterDynHeader dynHeader;
        InflaterHuffmanTree litlenTree;
        InflaterHuffmanTree distTree;
        Adler32 adler;
        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Inflater"/> class.
        /// </summary>
        public Inflater()
            : this(false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Inflater"/> class.
        /// </summary>
        /// <param name="noHeader">
        /// True if no RFC1950/Zlib header and footer fields are expected in the input data
        ///
        /// This is used for GZIPed/Zipped input.
        ///
        /// For compatibility with
        /// Sun JDK you should provide one byte of input more than needed in
        /// this case.
        /// </param>
        public Inflater(bool noHeader)
        {
            this.noHeader = noHeader;
            adler = new Adler32();
            input = new StreamManipulator();
            outputWindow = new OutputWindow();
            mode = noHeader ? State.Blocks : State.Header;
        }
        #endregion

        /// <summary>
        /// Resets the inflater so that a new stream can be decompressed.  All
        /// pending input and output will be discarded.
        /// </summary>
        public void Reset()
        {
            mode = noHeader ? State.Blocks : State.Header;
            totalIn = 0;
            TotalOut = 0;
            input.Reset();
            outputWindow.Reset();
            dynHeader = null;
            litlenTree = null;
            distTree = null;
            isLastBlock = false;
            adler.Reset();
        }

        /// <summary>
        /// Decodes a zlib/RFC1950 header.
        /// </summary>
        /// <returns>
        /// False if more input is needed.
        /// </returns>
        /// <exception cref="InvalidDataException">
        /// The header is invalid.
        /// </exception>
        bool DecodeHeader()
        {
            var header = input.PeekBits(16);
            if (header < 0)
            {
                return false;
            }

            input.DropBits(16);

            // The header is written in "wrong" byte order
            header = ((header << 8) | (header >> 8)) & 0xffff;
            if (header % 31 != 0)
            {
                throw new InvalidDataException("Header checksum illegal");
            }

            if ((header & 0x0f00) != (Deflater.DEFLATED << 8))
            {
                throw new InvalidDataException("Compression Method unknown");
            }

            /* Maximum size of the backwards window in bits.
            * We currently ignore this, but we could use it to make the
            * inflater window more space efficient. On the other hand the
            * full window (15 bits) is needed most times, anyway.
            int max_wbits = ((header & 0x7000) >> 12) + 8;
            */

            if ((header & 0x0020) == 0)
            { // Dictionary flag?
                mode = State.Blocks;
            }
            else
            {
                mode = State.Dictionary;
                neededBits = 32;
            }

            return true;
        }

        /// <summary>
        /// Decodes the dictionary checksum after the deflate header.
        /// </summary>
        /// <returns>
        /// False if more input is needed.
        /// </returns>
        bool DecodeDict()
        {
            while (neededBits > 0)
            {
                var dictByte = input.PeekBits(8);
                if (dictByte < 0)
                {
                    return false;
                }

                input.DropBits(8);
                readAdler = (readAdler << 8) | dictByte;
                neededBits -= 8;
            }

            return false;
        }

        /// <summary>
        /// Decodes the huffman encoded symbols in the input stream.
        /// </summary>
        /// <returns>
        /// false if more input is needed, true if output window is
        /// full or the current block ends.
        /// </returns>
        /// <exception cref="InvalidDataException">
        /// if deflated stream is invalid.
        /// </exception>
        bool DecodeHuffman()
        {
            var free = outputWindow.GetFreeSpace();
            while (free >= 258)
            {
                int symbol;
                switch (mode)
                {
                    case State.Huffman:
                        // This is the inner loop so it is optimized a bit
                        while (((symbol = litlenTree.GetSymbol(input)) & ~0xff) == 0)
                        {
                            outputWindow.Write(symbol);
                            if (--free < 258)
                            {
                                return true;
                            }
                        }

                        if (symbol < 257)
                        {
                            if (symbol < 0)
                            {
                                return false;
                            }
                            else
                            {
                                // symbol == 256: end of block
                                distTree = null;
                                litlenTree = null;
                                mode = State.Blocks;
                                return true;
                            }
                        }

                        try
                        {
                            repLength = CPLENS[symbol - 257];
                            neededBits = CPLEXT[symbol - 257];
                        }
                        catch (Exception)
                        {
                            throw new InvalidDataException("Illegal rep length code");
                        }

                        goto case State.HuffmanLenBits; // fall through

                    case State.HuffmanLenBits:
                        if (neededBits > 0)
                        {
                            mode = State.HuffmanLenBits;
                            var i = input.PeekBits(neededBits);
                            if (i < 0)
                            {
                                return false;
                            }

                            input.DropBits(neededBits);
                            repLength += i;
                        }

                        mode = State.HuffmanDist;
                        goto case State.HuffmanDist; // fall through

                    case State.HuffmanDist:
                        symbol = distTree.GetSymbol(input);
                        if (symbol < 0)
                        {
                            return false;
                        }

                        try
                        {
                            repDist = CPDIST[symbol];
                            neededBits = CPDEXT[symbol];
                        }
                        catch (Exception)
                        {
                            throw new InvalidDataException("Illegal rep dist code");
                        }

                        goto case State.HuffmanDistBits; // fall through

                    case State.HuffmanDistBits:
                        if (neededBits > 0)
                        {
                            mode = State.HuffmanDistBits;
                            var i = input.PeekBits(neededBits);
                            if (i < 0)
                            {
                                return false;
                            }

                            input.DropBits(neededBits);
                            repDist += i;
                        }

                        outputWindow.Repeat(repLength, repDist);
                        free -= repLength;
                        mode = State.Huffman;
                        break;

                    default:
                        throw new InvalidDataException("Inflater unknown mode");
                }
            }

            return true;
        }

        /// <summary>
        /// Decodes the adler checksum after the deflate stream.
        /// </summary>
        /// <returns>
        /// false if more input is needed.
        /// </returns>
        /// <exception cref="InvalidDataException">
        /// If checksum doesn't match.
        /// </exception>
        bool DecodeChksum()
        {
            while (neededBits > 0)
            {
                var chkByte = input.PeekBits(8);
                if (chkByte < 0)
                {
                    return false;
                }

                input.DropBits(8);
                readAdler = (readAdler << 8) | chkByte;
                neededBits -= 8;
            }

            if ((int)adler.Value != readAdler)
            {
                throw new InvalidDataException("Adler chksum doesn't match: " + (int)adler.Value + " vs. " + readAdler);
            }

            mode = State.Done;
            return false;
        }

        /// <summary>
        /// Decodes the deflated stream.
        /// </summary>
        /// <returns>
        /// false if more input is needed, or if State.Done.
        /// </returns>
        /// <exception cref="InvalidDataException">
        /// if deflated stream is invalid.
        /// </exception>
        bool Decode()
        {
            switch (mode)
            {
                case State.Header:
                    return DecodeHeader();

                case State.Dictionary:
                    return DecodeDict();

                case State.Checksum:
                    return DecodeChksum();

                case State.Blocks:
                    if (isLastBlock)
                    {
                        if (noHeader)
                        {
                            mode = State.Done;
                            return false;
                        }
                        else
                        {
                            input.SkipToByteBoundary();
                            neededBits = 32;
                            mode = State.Checksum;
                            return true;
                        }
                    }

                    var type = input.PeekBits(3);
                    if (type < 0)
                    {
                        return false;
                    }

                    input.DropBits(3);

                    isLastBlock |= (type & 1) != 0;
                    switch (type >> 1)
                    {
                        case DeflaterConstants.StoredBlock:
                            input.SkipToByteBoundary();
                            mode = State.StoredLen1;
                            break;
                        case DeflaterConstants.StaticTree:
                            litlenTree = InflaterHuffmanTree.DefLitLenTree;
                            distTree = InflaterHuffmanTree.DefDistTree;
                            mode = State.Huffman;
                            break;
                        case DeflaterConstants.DynamicTree:
                            dynHeader = new InflaterDynHeader();
                            mode = State.DynamicHeader;
                            break;
                        default:
                            throw new NotSupportedException("Unknown block type " + type);
                    }

                    return true;

                case State.StoredLen1:
                {
                    if ((uncomprLen = input.PeekBits(16)) < 0)
                    {
                        return false;
                    }

                    input.DropBits(16);
                    mode = State.StoredLen2;
                }

                goto case State.StoredLen2; // fall through

                case State.StoredLen2:
                {
                    var nlen = input.PeekBits(16);
                    if (nlen < 0)
                    {
                        return false;
                    }

                    input.DropBits(16);
                    if (nlen != (uncomprLen ^ 0xffff))
                    {
                        throw new InvalidDataException("broken uncompressed block");
                    }

                    mode = State.Stored;
                    goto case State.Stored; // fall through
                }

                case State.Stored:
                {
                    var more = outputWindow.CopyStored(input, uncomprLen);
                    uncomprLen -= more;
                    if (uncomprLen == 0)
                    {
                        mode = State.Blocks;
                        return true;
                    }

                    return !input.IsNeedingInput;
                }

                case State.DynamicHeader:
                    if (!dynHeader.Decode(input))
                    {
                        return false;
                    }

                    litlenTree = dynHeader.BuildLitLenTree();
                    distTree = dynHeader.BuildDistTree();
                    mode = State.Huffman;
                    goto case State.Huffman; // fall through

                case State.Huffman:
                case State.HuffmanLenBits:
                case State.HuffmanDist:
                case State.HuffmanDistBits:
                    return DecodeHuffman();

                case State.Done:
                    return false;

                default:
                    throw new InvalidDataException("Inflater.Decode unknown mode");
            }
        }

        /// <summary>
        /// Sets the preset dictionary.  This should only be called, if
        /// needsDictionary() returns true and it should set the same
        /// dictionary, that was used for deflating.  The getAdler()
        /// function returns the checksum of the dictionary needed.
        /// </summary>
        /// <param name="buffer">
        /// The dictionary.
        /// </param>
        public void SetDictionary(byte[] buffer)
        {
            SetDictionary(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Sets the preset dictionary.  This should only be called, if
        /// needsDictionary() returns true and it should set the same
        /// dictionary, that was used for deflating.  The getAdler()
        /// function returns the checksum of the dictionary needed.
        /// </summary>
        /// <param name="buffer">
        /// The dictionary.
        /// </param>
        /// <param name="index">
        /// The index into buffer where the dictionary starts.
        /// </param>
        /// <param name="count">
        /// The number of bytes in the dictionary.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// No dictionary is needed.
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// The adler checksum for the buffer is invalid.
        /// </exception>
        public void SetDictionary(byte[] buffer, int index, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (!IsNeedingDictionary)
            {
                throw new InvalidOperationException("Dictionary is not needed");
            }

            adler.Update(buffer, index, count);

            if ((int)adler.Value != readAdler)
            {
                throw new InvalidDataException("Wrong adler checksum");
            }

            adler.Reset();
            outputWindow.CopyDict(buffer, index, count);
            mode = State.Blocks;
        }

        /// <summary>
        /// Sets the input.  This should only be called, if needsInput()
        /// returns true.
        /// </summary>
        /// <param name="buffer">
        /// the input.
        /// </param>
        public void SetInput(byte[] buffer)
        {
            SetInput(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Sets the input.  This should only be called, if needsInput()
        /// returns true.
        /// </summary>
        /// <param name="buffer">
        /// The source of input data.
        /// </param>
        /// <param name="index">
        /// The index into buffer where the input starts.
        /// </param>
        /// <param name="count">
        /// The number of bytes of input to use.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// No input is needed.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The index and/or count are wrong.
        /// </exception>
        public void SetInput(byte[] buffer, int index, int count)
        {
            input.SetInput(buffer, index, count);
            totalIn += count;
        }

        /// <summary>
        /// Inflates the compressed stream to the output buffer.  If this
        /// returns 0, you should check, whether IsNeedingDictionary(),
        /// IsNeedingInput() or IsState.Done() returns true, to determine why no
        /// further output is produced.
        /// </summary>
        /// <param name="buffer">
        /// the output buffer.
        /// </param>
        /// <returns>
        /// The number of bytes written to the buffer, 0 if no further
        /// output can be produced.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// if buffer has length 0.
        /// </exception>
        /// <exception cref="FormatException">
        /// if deflated stream is invalid.
        /// </exception>
        public int Inflate(byte[] buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            return Inflate(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Inflates the compressed stream to the output buffer.  If this
        /// returns 0, you should check, whether needsDictionary(),
        /// needsInput() or State.Done() returns true, to determine why no
        /// further output is produced.
        /// </summary>
        /// <param name="buffer">
        /// the output buffer.
        /// </param>
        /// <param name="offset">
        /// the offset in buffer where storing starts.
        /// </param>
        /// <param name="count">
        /// the maximum number of bytes to output.
        /// </param>
        /// <returns>
        /// the number of bytes written to the buffer, 0 if no further output can be produced.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// if count is less than 0.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// if the index and / or count are wrong.
        /// </exception>
        /// <exception cref="FormatException">
        /// if deflated stream is invalid.
        /// </exception>
        public int Inflate(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "count cannot be negative");
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "offset cannot be negative");
            }

            if (offset + count > buffer.Length)
            {
                throw new ArgumentException("count exceeds buffer bounds");
            }

            // Special case: count may be zero
            if (count == 0)
            {
                if (!IsDone)
                { // -jr- 08-Nov-2003 INFLATE_BUG fix..
                    Decode();
                }

                return 0;
            }

            var bytesCopied = 0;

            do
            {
                if (mode != State.Checksum)
                {
                    /* Don't give away any output, if we are waiting for the
                    * checksum in the input stream.
                    *
                    * With this trick we have always:
                    *   IsNeedingInput() and not IsState.Done()
                    *   implies more output can be produced.
                    */
                    var more = outputWindow.CopyOutput(buffer, offset, count);
                    if (more > 0)
                    {
                        adler.Update(buffer, offset, more);
                        offset += more;
                        bytesCopied += more;
                        TotalOut += more;
                        count -= more;
                        if (count == 0)
                        {
                            return bytesCopied;
                        }
                    }
                }
            }
            while (Decode() || ((outputWindow.GetAvailable() > 0) && (mode != State.Checksum)));
            return bytesCopied;
        }

        /// <summary>
        /// Gets a value indicating whether the input buffer is empty.
        /// You should then call setInput().
        /// NOTE: This method also returns true when the stream is State.Done.
        /// </summary>
        public bool IsNeedingInput
        {
            get
            {
                return input.IsNeedingInput;
            }
        }

        /// <summary>
        /// Gets a value indicating whether a preset dictionary is needed to inflate the input.
        /// </summary>
        public bool IsNeedingDictionary
        {
            get
            {
                return mode == State.Dictionary && neededBits == 0;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the inflater has State.Done.
        /// This means, that no input is needed and no output can be produced.
        /// </summary>
        public bool IsDone
        {
            get
            {
                return mode == State.Done && outputWindow.GetAvailable() == 0;
            }
        }

        /// <summary>
        /// Gets the adler checksum.  This is either the checksum of all
        /// uncompressed bytes returned by inflate(), or if needsDictionary()
        /// returns true (and thus no output was yet produced) this is the
        /// adler checksum of the expected dictionary.
        /// </summary>
        /// <returns>
        /// the adler checksum.
        /// </returns>
        public int Adler
        {
            get
            {
                return IsNeedingDictionary ? readAdler : (int)adler.Value;
            }
        }

        /// <summary>
        /// Gets or sets the total number of output bytes returned by Inflate().
        /// </summary>
        /// <returns>
        /// the total number of output bytes.
        /// </returns>
        public long TotalOut { get; set; }

        /// <summary>
        /// Gets the total number of processed compressed input bytes.
        /// </summary>
        /// <returns>
        /// The total number of bytes of processed input bytes.
        /// </returns>
        public long TotalIn
        {
            get
            {
                return totalIn - RemainingInput;
            }
        }

        /// <summary>
        /// Gets the number of unprocessed input bytes.  Useful, if the end of the
        /// stream is reached and you want to further process the bytes after
        /// the deflate stream.
        /// </summary>
        /// <returns>
        /// The number of bytes of the input which have not been processed.
        /// </returns>
        public int RemainingInput
        {
            // TODO: This should be a long?
            get
            {
                return input.AvailableBytes;
            }
        }
    }
}
