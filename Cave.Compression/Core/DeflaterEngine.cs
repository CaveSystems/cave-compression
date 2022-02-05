using System;
using Cave.Compression.Checksum;

namespace Cave.Compression.Core
{
    /// <summary>
    /// Low level compression engine for deflate algorithm which uses a 32K sliding window
    /// with secondary compression from Huffman/Shannon-Fano codes.
    /// </summary>
    /// <remarks>
    /// DEFLATE ALGORITHM:
    ///
    /// The uncompressed stream is inserted into the window array.  When
    /// the window array is full the first half is thrown away and the
    /// second half is copied to the beginning.
    ///
    /// The head array is a hash table.  Three characters build a hash value
    /// and they the value points to the corresponding index in window of
    /// the last string with this hash.  The prev array implements a
    /// linked list of matches with the same hash: prev[index &amp; WMASK] points
    /// to the previous index with the same hash.
    ///
    /// </remarks>
    class DeflaterEngine
    {
        #region Constants
        const int TooFar = 4096;
        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflaterEngine"/> class.
        /// </summary>
        /// <param name="pending">
        /// Pending buffer to use.
        /// </param>>
        public DeflaterEngine(DeflaterPending pending)
        {
            this.pending = pending;
            huffman = new DeflaterHuffman(pending);
            adler = new Adler32();

            window = new byte[2 * DeflaterConstants.WindowSize];
            head = new short[DeflaterConstants.HashSize];
            prev = new short[DeflaterConstants.WindowSize];

            // We start at index 1, to avoid an implementation deficiency, that
            // we cannot build a repeat pattern at index 0.
            blockStart = strStart = 1;
        }

        #endregion

        /// <summary>
        /// Deflate drives actual compression of data.
        /// </summary>
        /// <param name="flush">True to flush input buffers.</param>
        /// <param name="finish">Finish deflation with the current input.</param>
        /// <returns>Returns true if progress has been made.</returns>
        public bool Deflate(bool flush, bool finish)
        {
            bool progress;
            do
            {
                FillWindow();
                var canFlush = flush && (inputOff == inputEnd);

#if DebugDeflation
				if (DeflaterConstants.DEBUGGING) {
					Console.WriteLine("window: [" + blockStart + "," + strstart + ","
								+ lookahead + "], " + compressionFunction + "," + canFlush);
				}
#endif
                switch (compressionFunction)
                {
                    case DeflaterConstants.DeflateStored:
                        progress = DeflateStored(canFlush, finish);
                        break;
                    case DeflaterConstants.DeflateFast:
                        progress = DeflateFast(canFlush, finish);
                        break;
                    case DeflaterConstants.DeflateSlow:
                        progress = DeflateSlow(canFlush, finish);
                        break;
                    default:
                        throw new InvalidOperationException("unknown compressionFunction");
                }
            }
            while (pending.IsFlushed && progress); // repeat while we have no pending output and progress was made
            return progress;
        }

        /// <summary>
        /// Sets input data to be deflated.  Should only be called when. <code>NeedsInput()</code>
        /// returns true.
        /// </summary>
        /// <param name="buffer">The buffer containing input data.</param>
        /// <param name="offset">The offset of the first byte of data.</param>
        /// <param name="count">The number of bytes of data to use as input.</param>
        public void SetInput(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (inputOff < inputEnd)
            {
                throw new InvalidOperationException("Old input was not completely processed");
            }

            var end = offset + count;

            /* We want to throw an ArrayIndexOutOfBoundsException early.  The
            * check is very tricky: it also handles integer wrap around.
            */
            if ((offset > end) || (end > buffer.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            inputBuf = buffer;
            inputOff = offset;
            inputEnd = end;
        }

        /// <summary>
        /// Determines if more <see cref="SetInput">input</see> is needed.
        /// </summary>
        /// <returns>Return true if input is needed via <see cref="SetInput">SetInput</see>.</returns>
        public bool NeedsInput()
        {
            return inputEnd == inputOff;
        }

        /// <summary>
        /// Set compression dictionary.
        /// </summary>
        /// <param name="buffer">The buffer containing the dictionary data.</param>
        /// <param name="offset">The offset in the buffer for the first byte of data.</param>
        /// <param name="length">The length of the dictionary data.</param>
        public void SetDictionary(byte[] buffer, int offset, int length)
        {
#if DebugDeflation
			if (DeflaterConstants.DEBUGGING && (strstart != 1) ) 
			{
				throw new InvalidOperationException("strstart not 1");
			}
#endif
            adler.Update(buffer, offset, length);
            if (length < DeflaterConstants.MinMatch)
            {
                return;
            }

            if (length > DeflaterConstants.MaxDist)
            {
                offset += length - DeflaterConstants.MaxDist;
                length = DeflaterConstants.MaxDist;
            }

            Array.Copy(buffer, offset, window, strStart, length);

            UpdateHash();
            --length;
            while (--length > 0)
            {
                InsertString();
                strStart++;
            }

            strStart += 2;
            blockStart = strStart;
        }

        /// <summary>
        /// Reset internal state.
        /// </summary>
        public void Reset()
        {
            huffman.Reset();
            adler.Reset();
            blockStart = strStart = 1;
            lookahead = 0;
            TotalIn = 0;
            prevAvailable = false;
            matchLen = DeflaterConstants.MinMatch - 1;

            for (var i = 0; i < DeflaterConstants.HashSize; i++)
            {
                head[i] = 0;
            }

            for (var i = 0; i < DeflaterConstants.WindowSize; i++)
            {
                prev[i] = 0;
            }
        }

        /// <summary>
        /// Reset Adler checksum.
        /// </summary>
        public void ResetAdler()
        {
            adler.Reset();
        }

        /// <summary>
        /// Gets current value of Adler checksum.
        /// </summary>
        public int Adler
        {
            get
            {
                return unchecked((int)adler.Value);
            }
        }

        /// <summary>
        /// Gets or sets total data processed.
        /// </summary>
        public long TotalIn { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="DeflateStrategy">deflate strategy</see>.
        /// </summary>
        public DeflateStrategy Strategy { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="CompressionStrength"/>.
        /// </summary>
        public CompressionStrength Strength
        {
            get => strength;
            set
            {
                var strength = (int)value;
                if ((strength < 0) || (strength > 9))
                {
                    throw new ArgumentOutOfRangeException(nameof(strength));
                }

                this.strength = value;

                goodLength = DeflaterConstants.GoodLength[strength];
                maxLazy = DeflaterConstants.MaxLazy[strength];
                niceLength = DeflaterConstants.NiceLength[strength];
                maxChain = DeflaterConstants.MaxChain[strength];

                if (DeflaterConstants.CompressionFunction[strength] != compressionFunction)
                {
#if DebugDeflation
				    if (DeflaterConstants.DEBUGGING) 
                    {
				       Console.WriteLine("Change from " + compressionFunction + " to " + DeflaterConstants.COMPR_FUNC[level]);
				    }
#endif
                    switch (compressionFunction)
                    {
                        case DeflaterConstants.DeflateStored:
                            if (strStart > blockStart)
                            {
                                huffman.FlushStoredBlock(window, blockStart, strStart - blockStart, false);
                                blockStart = strStart;
                            }

                            UpdateHash();
                            break;

                        case DeflaterConstants.DeflateFast:
                            if (strStart > blockStart)
                            {
                                huffman.FlushBlock(window, blockStart, strStart - blockStart, false);
                                blockStart = strStart;
                            }

                            break;

                        case DeflaterConstants.DeflateSlow:
                            if (prevAvailable)
                            {
                                huffman.TallyLit(window[strStart - 1] & 0xff);
                            }

                            if (strStart > blockStart)
                            {
                                huffman.FlushBlock(window, blockStart, strStart - blockStart, false);
                                blockStart = strStart;
                            }

                            prevAvailable = false;
                            matchLen = DeflaterConstants.MinMatch - 1;
                            break;
                    }

                    compressionFunction = DeflaterConstants.CompressionFunction[strength];
                }
            }
        }

        /// <summary>
        /// Fill the window.
        /// </summary>
        public void FillWindow()
        {
            /* If the window is almost full and there is insufficient lookahead,
             * move the upper half to the lower one to make room in the upper half.
             */
            if (strStart >= DeflaterConstants.WindowSize + DeflaterConstants.MaxDist)
            {
                SlideWindow();
            }

            /* If there is not enough lookahead, but still some input left,
             * read in the input
             */
            if (lookahead < DeflaterConstants.MinLookahead && inputOff < inputEnd)
            {
                var more = (2 * DeflaterConstants.WindowSize) - lookahead - strStart;

                if (more > inputEnd - inputOff)
                {
                    more = inputEnd - inputOff;
                }

                Array.Copy(inputBuf, inputOff, window, strStart + lookahead, more);
                adler.Update(inputBuf, inputOff, more);

                inputOff += more;
                TotalIn += more;
                lookahead += more;
            }

            if (lookahead >= DeflaterConstants.MinMatch)
            {
                UpdateHash();
            }
        }

        void UpdateHash()
        {
            /*
                        if (DEBUGGING) {
                            Console.WriteLine("updateHash: "+strstart);
                        }
            */
            hashIndex = (window[strStart] << DeflaterConstants.HashShift) ^ window[strStart + 1];
        }

        /// <summary>
        /// Inserts the current string in the head hash and returns the previous
        /// value for this hash.
        /// </summary>
        /// <returns>The previous hash value.</returns>
        int InsertString()
        {
            short match;
            var hash = ((hashIndex << DeflaterConstants.HashShift) ^ window[strStart + (DeflaterConstants.MinMatch - 1)]) & DeflaterConstants.HashMask;

#if DebugDeflation
			if (DeflaterConstants.DEBUGGING) 
			{
				if (hash != (((window[strstart] << (2*HASH_SHIFT)) ^ 
								  (window[strstart + 1] << HASH_SHIFT) ^ 
								  (window[strstart + 2])) & HASH_MASK)) {
						throw new SharpZipBaseException("hash inconsistent: " + hash + "/"
												+window[strstart] + ","
												+window[strstart + 1] + ","
												+window[strstart + 2] + "," + HASH_SHIFT);
					}
			}
#endif
            prev[strStart & DeflaterConstants.WindowMask] = match = head[hash];
            head[hash] = unchecked((short)strStart);
            hashIndex = hash;
            return match & 0xffff;
        }

        void SlideWindow()
        {
            Array.Copy(window, DeflaterConstants.WindowSize, window, 0, DeflaterConstants.WindowSize);
            matchStart -= DeflaterConstants.WindowSize;
            strStart -= DeflaterConstants.WindowSize;
            blockStart -= DeflaterConstants.WindowSize;

            // Slide the hash table (could be avoided with 32 bit values
            // at the expense of memory usage).
            for (var i = 0; i < DeflaterConstants.HashSize; ++i)
            {
                var m = head[i] & 0xffff;
                head[i] = (short)(m >= DeflaterConstants.WindowSize ? (m - DeflaterConstants.WindowSize) : 0);
            }

            // Slide the prev table.
            for (var i = 0; i < DeflaterConstants.WindowSize; i++)
            {
                var m = prev[i] & 0xffff;
                prev[i] = (short)(m >= DeflaterConstants.WindowSize ? (m - DeflaterConstants.WindowSize) : 0);
            }
        }

        /// <summary>
        /// Find the best (longest) string in the window matching the
        /// string starting at strstart.
        ///
        /// Preconditions:
        /// <code>
        /// strstart + DeflaterConstants.MAX_MATCH &lt;= window.length.</code>
        /// </summary>
        /// <param name="curMatch">current match.</param>
        /// <returns>True if a match greater than the minimum length is found.</returns>
        bool FindLongestMatch(int curMatch)
        {
            int match;
            var scan = strStart;

            // scanMax is the highest position that we can look at
            var scanMax = scan + Math.Min(DeflaterConstants.MaxMatch, lookahead) - 1;
            var limit = Math.Max(scan - DeflaterConstants.MaxDist, 0);

            var window = this.window;
            var prev = this.prev;
            var chainLength = maxChain;
            var niceLength = Math.Min(this.niceLength, lookahead);

            matchLen = Math.Max(matchLen, DeflaterConstants.MinMatch - 1);

            if (scan + matchLen > scanMax)
            {
                return false;
            }

            var scan_end1 = window[scan + matchLen - 1];
            var scan_end = window[scan + matchLen];

            // Do not waste too much time if we already have a good match:
            if (matchLen >= goodLength)
            {
                chainLength >>= 2;
            }

            do
            {
                match = curMatch;
                scan = strStart;

                if (window[match + matchLen] != scan_end
                 || window[match + matchLen - 1] != scan_end1
                 || window[match] != window[scan]
                 || window[++match] != window[++scan])
                {
                    continue;
                }

                // scan is set to strstart+1 and the comparison passed, so
                // scanMax - scan is the maximum number of bytes we can compare.
                // below we compare 8 bytes at a time, so first we compare
                // (scanMax - scan) % 8 bytes, so the remainder is a multiple of 8
                switch ((scanMax - scan) % 8)
                {
                    case 1:
                        if (window[++scan] == window[++match])
                        {
                            break;
                        }

                        break;
                    case 2:
                        if (window[++scan] == window[++match]
                  && window[++scan] == window[++match])
                        {
                            break;
                        }

                        break;
                    case 3:
                        if (window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match])
                        {
                            break;
                        }

                        break;
                    case 4:
                        if (window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match])
                        {
                            break;
                        }

                        break;
                    case 5:
                        if (window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match])
                        {
                            break;
                        }

                        break;
                    case 6:
                        if (window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match])
                        {
                            break;
                        }

                        break;
                    case 7:
                        if (window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match]
                  && window[++scan] == window[++match])
                        {
                            break;
                        }

                        break;
                }

                if (window[scan] == window[match])
                {
                    /* We check for insufficient lookahead only every 8th comparison;
                     * the 256th check will be made at strstart + 258 unless lookahead is
                     * exhausted first.
                     */
                    do
                    {
                        if (scan == scanMax)
                        {
                            ++scan;     // advance to first position not matched
                            ++match;

                            break;
                        }
                    }
                    while (window[++scan] == window[++match]
                        && window[++scan] == window[++match]
                        && window[++scan] == window[++match]
                        && window[++scan] == window[++match]
                        && window[++scan] == window[++match]
                        && window[++scan] == window[++match]
                        && window[++scan] == window[++match]
                        && window[++scan] == window[++match]);
                }

                if (scan - strStart > matchLen)
                {
#if DebugDeflation
              if (DeflaterConstants.DEBUGGING && (ins_h == 0) )
              Console.Error.WriteLine("Found match: " + curMatch + "-" + (scan - strstart));
#endif

                    matchStart = curMatch;
                    matchLen = scan - strStart;

                    if (matchLen >= niceLength)
                    {
                        break;
                    }

                    scan_end1 = window[scan - 1];
                    scan_end = window[scan];
                }
            }
            while ((curMatch = prev[curMatch & DeflaterConstants.WindowMask] & 0xffff) > limit && --chainLength != 0);

            return matchLen >= DeflaterConstants.MinMatch;
        }

        bool DeflateStored(bool flush, bool finish)
        {
            if (!flush && (lookahead == 0))
            {
                return false;
            }

            strStart += lookahead;
            lookahead = 0;

            var storedLength = strStart - blockStart;

            if ((storedLength >= DeflaterConstants.MaxBlockSize) || // Block is full
                (blockStart < DeflaterConstants.WindowSize && storedLength >= DeflaterConstants.MaxDist) || // Block may move out of window
                flush)
            {
                var lastBlock = finish;
                if (storedLength > DeflaterConstants.MaxBlockSize)
                {
                    storedLength = DeflaterConstants.MaxBlockSize;
                    lastBlock = false;
                }

#if DebugDeflation
				if (DeflaterConstants.DEBUGGING) 
				{
				   Console.WriteLine("storedBlock[" + storedLength + "," + lastBlock + "]");
				}
#endif

                huffman.FlushStoredBlock(window, blockStart, storedLength, lastBlock);
                blockStart += storedLength;
                return !lastBlock;
            }

            return true;
        }

        bool DeflateFast(bool flush, bool finish)
        {
            if (lookahead < DeflaterConstants.MinLookahead && !flush)
            {
                return false;
            }

            while (lookahead >= DeflaterConstants.MinLookahead || flush)
            {
                if (lookahead == 0)
                {
                    // We are flushing everything
                    huffman.FlushBlock(window, blockStart, strStart - blockStart, finish);
                    blockStart = strStart;
                    return false;
                }

                if (strStart > (2 * DeflaterConstants.WindowSize) - DeflaterConstants.MinLookahead)
                {
                    /* slide window, as FindLongestMatch needs this.
                     * This should only happen when flushing and the window
                     * is almost full.
                     */
                    SlideWindow();
                }

                int hashHead;
                if (lookahead >= DeflaterConstants.MinMatch &&
                    (hashHead = InsertString()) != 0 &&
                    Strategy != DeflateStrategy.HuffmanOnly &&
                    strStart - hashHead <= DeflaterConstants.MaxDist &&
                    FindLongestMatch(hashHead))
                {
                    // longestMatch sets matchStart and matchLen
#if DebugDeflation
					if (DeflaterConstants.DEBUGGING) 
					{
						for (int i = 0 ; i < matchLen; i++) {
							if (window[strstart + i] != window[matchStart + i]) {
								throw new SharpZipBaseException("Match failure");
							}
						}
					}
#endif

                    var full = huffman.TallyDist(strStart - matchStart, matchLen);

                    lookahead -= matchLen;
                    if (matchLen <= maxLazy && lookahead >= DeflaterConstants.MinMatch)
                    {
                        while (--matchLen > 0)
                        {
                            ++strStart;
                            InsertString();
                        }

                        ++strStart;
                    }
                    else
                    {
                        strStart += matchLen;
                        if (lookahead >= DeflaterConstants.MinMatch - 1)
                        {
                            UpdateHash();
                        }
                    }

                    matchLen = DeflaterConstants.MinMatch - 1;
                    if (!full)
                    {
                        continue;
                    }
                }
                else
                {
                    // No match found
                    huffman.TallyLit(window[strStart] & 0xff);
                    ++strStart;
                    --lookahead;
                }

                if (huffman.IsFull())
                {
                    var lastBlock = finish && (lookahead == 0);
                    huffman.FlushBlock(window, blockStart, strStart - blockStart, lastBlock);
                    blockStart = strStart;
                    return !lastBlock;
                }
            }

            return true;
        }

        bool DeflateSlow(bool flush, bool finish)
        {
            if (lookahead < DeflaterConstants.MinLookahead && !flush)
            {
                return false;
            }

            while (lookahead >= DeflaterConstants.MinLookahead || flush)
            {
                if (lookahead == 0)
                {
                    if (prevAvailable)
                    {
                        huffman.TallyLit(window[strStart - 1] & 0xff);
                    }

                    prevAvailable = false;

                    // We are flushing everything
#if DebugDeflation
					if (DeflaterConstants.DEBUGGING && !flush) 
					{
						throw new SharpZipBaseException("Not flushing, but no lookahead");
					}
#endif
                    huffman.FlushBlock(window, blockStart, strStart - blockStart, finish);
                    blockStart = strStart;
                    return false;
                }

                if (strStart >= (2 * DeflaterConstants.WindowSize) - DeflaterConstants.MinLookahead)
                {
                    /* slide window, as FindLongestMatch needs this.
                     * This should only happen when flushing and the window
                     * is almost full.
                     */
                    SlideWindow();
                }

                var prevMatch = matchStart;
                var prevLen = matchLen;
                if (lookahead >= DeflaterConstants.MinMatch)
                {
                    var hashHead = InsertString();

                    if (Strategy != DeflateStrategy.HuffmanOnly &&
                        hashHead != 0 &&
                        strStart - hashHead <= DeflaterConstants.MaxDist &&
                        FindLongestMatch(hashHead))
                    {
                        // longestMatch sets matchStart and matchLen

                        // Discard match if too small and too far away
                        if (matchLen <= 5 && (Strategy == DeflateStrategy.Filtered || (matchLen == DeflaterConstants.MinMatch && strStart - matchStart > TooFar)))
                        {
                            matchLen = DeflaterConstants.MinMatch - 1;
                        }
                    }
                }

                // previous match was better
                if ((prevLen >= DeflaterConstants.MinMatch) && (matchLen <= prevLen))
                {
#if DebugDeflation
					if (DeflaterConstants.DEBUGGING) 
					{
					   for (int i = 0 ; i < matchLen; i++) {
						  if (window[strstart-1+i] != window[prevMatch + i])
							 throw new SharpZipBaseException();
						}
					}
#endif
                    huffman.TallyDist(strStart - 1 - prevMatch, prevLen);
                    prevLen -= 2;
                    do
                    {
                        strStart++;
                        lookahead--;
                        if (lookahead >= DeflaterConstants.MinMatch)
                        {
                            InsertString();
                        }
                    }
                    while (--prevLen > 0);

                    strStart++;
                    lookahead--;
                    prevAvailable = false;
                    matchLen = DeflaterConstants.MinMatch - 1;
                }
                else
                {
                    if (prevAvailable)
                    {
                        huffman.TallyLit(window[strStart - 1] & 0xff);
                    }

                    prevAvailable = true;
                    strStart++;
                    lookahead--;
                }

                if (huffman.IsFull())
                {
                    var len = strStart - blockStart;
                    if (prevAvailable)
                    {
                        len--;
                    }

                    var lastBlock = finish && (lookahead == 0) && !prevAvailable;
                    huffman.FlushBlock(window, blockStart, len, lastBlock);
                    blockStart += len;
                    return !lastBlock;
                }
            }

            return true;
        }

        #region Instance Fields

        /// <summary>
        /// Hashtable, hashing three characters to an index for window, so
        /// that window[index]..window[index+2] have this hash code.
        /// Note that the array should really be unsigned short, so you need
        /// to and the values with 0xffff.
        /// </summary>
        readonly short[] head;

        /// <summary>
        /// <code>prev[index &amp; WMASK]</code> points to the previous index that has the
        /// same hash code as the string starting at index.  This way
        /// entries with the same hash code are in a linked list.
        /// Note that the array should really be unsigned short, so you need
        /// to and the values with 0xffff.
        /// </summary>
        readonly short[] prev;

        /// <summary>
        /// This array contains the part of the uncompressed stream that
        /// is of relevance.  The current character is indexed by strstart.
        /// </summary>
        readonly byte[] window;

        /// <summary>
        /// ins_h: Hash index of string to be inserted.
        /// </summary>
        int hashIndex;

        int matchStart;

        /// <summary>
        /// Length of best match.
        /// </summary>
        int matchLen;

        /// <summary>
        /// Set if previous match exists.
        /// </summary>
        bool prevAvailable;

        int blockStart;

        /// <summary>
        /// Points to the current character in the window.
        /// </summary>
        int strStart;

        /// <summary>
        /// lookahead is the number of characters starting at strstart in
        /// window that are valid.
        /// So window[strstart] until window[strstart+lookahead-1] are valid
        /// characters.
        /// </summary>
        int lookahead;

        int maxChain;
        int maxLazy;
        int niceLength;
        int goodLength;

        /// <summary>
        /// The current compression function.
        /// </summary>
        int compressionFunction;

        /// <summary>
        /// The input data for compression.
        /// </summary>
        byte[] inputBuf;

        /// <summary>
        /// The offset into inputBuf, where input data starts.
        /// </summary>
        int inputOff;

        /// <summary>
        /// The end offset of the input data.
        /// </summary>
        int inputEnd;

        DeflaterPending pending;
        DeflaterHuffman huffman;

        /// <summary>
        /// The adler checksum.
        /// </summary>
        Adler32 adler;

        CompressionStrength strength;
        #endregion
    }
}
