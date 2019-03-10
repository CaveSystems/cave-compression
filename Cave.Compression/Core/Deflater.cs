using System;

namespace Cave.Compression.Core
{
    /// <summary>
    /// This is the Deflater class.  The deflater class compresses input
    /// with the deflate algorithm described in RFC 1951.  It has several
    /// compression levels and three different strategies described below.
    ///
    /// This class is <i>not</i> thread safe.  This is inherent in the API, due
    /// to the split of deflate and setInput.
    ///
    /// author of the original java version : Jochen Hoenicke.
    /// </summary>
    public class Deflater
    {
        /// <summary>
        /// The compression method.  This is the only method supported so far.
        /// There is no need to use this constant at all.
        /// </summary>
        public const int DEFLATED = 8;

        #region Deflater Documentation
        /*
        * The Deflater can do the following state transitions:
        *
        * (1) -> INIT_STATE   ----> INIT_FINISHING_STATE ---.
        *        /  | (2)      (5)                          |
        *       /   v          (5)                          |
        *   (3)| SETDICT_STATE ---> SETDICT_FINISHING_STATE |(3)
        *       \   | (3)                 |        ,--------'
        *        |  |                     | (3)   /
        *        v  v          (5)        v      v
        * (1) -> BUSY_STATE   ----> FINISHING_STATE
        *                                | (6)
        *                                v
        *                           FINISHED_STATE
        *    \_____________________________________/
        *                    | (7)
        *                    v
        *               CLOSED_STATE
        *
        * (1) If we should produce a header we start in INIT_STATE, otherwise
        *     we start in BUSY_STATE.
        * (2) A dictionary may be set only when we are in INIT_STATE, then
        *     we change the state as indicated.
        * (3) Whether a dictionary is set or not, on the first call of deflate
        *     we change to BUSY_STATE.
        * (4) -- intentionally left blank -- :)
        * (5) FINISHING_STATE is entered, when flush() is called to indicate that
        *     there is no more INPUT.  There are also states indicating, that
        *     the header wasn't written yet.
        * (6) FINISHED_STATE is entered, when everything has been flushed to the
        *     internal pending output buffer.
        * (7) At any time (7)
        *
        */
        #endregion

        #region Local Constants

        // TODO clean this mess up
        const int IsSetDict = 0x01;
        const int IsFlushing = 0x04;
        const int IsFinishing = 0x08;
        const int InitState = 0x00;
        const int SetDictState = 0x01;
        const int BusyState = 0x10;
        const int FlushingState = 0x14;
        const int FinishingState = 0x1c;
        const int FinishedState = 0x1e;
        const int ClosedState = 0x7f;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Deflater"/> class.
        /// </summary>
        public Deflater()
            : this(CompressionStrength.Best, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deflater"/> class.
        /// </summary>
        /// <param name="level">
        /// the compression level, a value between 0..9.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">if level is out of range.</exception>
        public Deflater(CompressionStrength level)
            : this(level, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Deflater"/> class.
        /// </summary>
        /// <param name="level">
        /// the compression level, a value between NO_COMPRESSION
        /// and BEST_COMPRESSION.
        /// </param>
        /// <param name="noZlibHeaderOrFooter">
        /// true, if we should suppress the Zlib/RFC1950 header at the
        /// beginning and the adler checksum at the end of the output.  This is
        /// useful for the GZIP/PKZIP formats.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">if lvl is out of range.</exception>
        public Deflater(CompressionStrength level, bool noZlibHeaderOrFooter)
        {
            if (level < 0 || (int)level > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            pending = new DeflaterPending();
            engine = new DeflaterEngine(pending);
            this.noZlibHeaderOrFooter = noZlibHeaderOrFooter;
            Strategy = DeflateStrategy.Default;
            Strength = level;
            Reset();
        }
        #endregion

        /// <summary>
        /// Resets the deflater.  The deflater acts afterwards as if it was
        /// just created with the same compression level and strategy as it
        /// had before.
        /// </summary>
        public void Reset()
        {
            state = noZlibHeaderOrFooter ? BusyState : InitState;
            TotalOut = 0;
            pending.Reset();
            engine.Reset();
        }

        /// <summary>
        /// Gets the current adler checksum of the data that was processed so far.
        /// </summary>
        public int Adler
        {
            get
            {
                return engine.Adler;
            }
        }

        /// <summary>
        /// Gets the number of input bytes processed so far.
        /// </summary>
        public long TotalIn
        {
            get
            {
                return engine.TotalIn;
            }
        }

        /// <summary>
        /// Gets or sets the number of output bytes so far.
        /// </summary>
        public long TotalOut { get; set; }

        /// <summary>
        /// Flushes the current input block.  Further calls to deflate() will
        /// produce enough output to inflate everything in the current input
        /// block.  This is not part of Sun's JDK so I have made it package
        /// private.  It is used by DeflaterOutputStream to implement
        /// flush().
        /// </summary>
        public void Flush()
        {
            state |= IsFlushing;
        }

        /// <summary>
        /// Finishes the deflater with the current input block.  It is an error
        /// to give more input after this method was called.  This method must
        /// be called to force all bytes to be flushed.
        /// </summary>
        public void Finish()
        {
            state |= IsFlushing | IsFinishing;
        }

        /// <summary>
        /// Gets a value indicating whether the stream was finished and no more output bytes
        /// are available.
        /// </summary>
        public bool IsFinished
        {
            get
            {
                return (state == FinishedState) && pending.IsFlushed;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the input buffer is empty.
        /// You should then call setInput().
        /// NOTE: This method can also return true when the stream
        /// was finished.
        /// </summary>
        public bool IsNeedingInput
        {
            get
            {
                return engine.NeedsInput();
            }
        }

        /// <summary>
        /// Sets the data which should be compressed next.  This should be only
        /// called when needsInput indicates that more input is needed.
        /// If you call setInput when needsInput() returns false, the
        /// previous input that is still pending will be thrown away.
        /// The given byte array should not be changed, before needsInput() returns
        /// true again.
        /// This call is equivalent to. <code>setInput(input, 0, input.length)</code>.
        /// </summary>
        /// <param name="input">
        /// the buffer containing the input data.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// if the buffer was finished() or ended().
        /// </exception>
        public void SetInput(byte[] input)
        {
            SetInput(input, 0, input.Length);
        }

        /// <summary>
        /// Sets the data which should be compressed next.  This should be
        /// only called when needsInput indicates that more input is needed.
        /// The given byte array should not be changed, before needsInput() returns
        /// true again.
        /// </summary>
        /// <param name="input">
        /// the buffer containing the input data.
        /// </param>
        /// <param name="offset">
        /// the start of the data.
        /// </param>
        /// <param name="count">
        /// the number of data bytes of input.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// if the buffer was Finish()ed or if previous input is still pending.
        /// </exception>
        public void SetInput(byte[] input, int offset, int count)
        {
            if ((state & IsFinishing) != 0)
            {
                throw new InvalidOperationException("Finish() already called");
            }

            engine.SetInput(input, offset, count);
        }

        /// <summary>
        /// Gets or sets the compression level.  There is no guarantee of the exact
        /// position of the change, but if you call this when needsInput is
        /// true the change of compression level will occur somewhere near
        /// before the end of the so far given input.
        /// </summary>
        public CompressionStrength Strength
        {
            get => engine.Strength;
            set => engine.Strength = value;
        }

        /// <summary>
        /// Gets or sets the compression strategy. Strategy is one of
        /// DEFAULT_STRATEGY, HUFFMAN_ONLY and FILTERED.  For the exact
        /// position where the strategy is changed, the same as for
        /// SetLevel() applies.
        /// </summary>
        public DeflateStrategy Strategy { get => engine.Strategy; set => engine.Strategy = value; }

        /// <summary>
        /// Deflates the current input block with to the given array.
        /// </summary>
        /// <param name="output">
        /// The buffer where compressed data is stored.
        /// </param>
        /// <returns>
        /// The number of compressed bytes added to the output, or 0 if either
        /// IsNeedingInput() or IsFinished returns true or length is zero.
        /// </returns>
        public int Deflate(byte[] output)
        {
            return Deflate(output, 0, output.Length);
        }

        /// <summary>
        /// Deflates the current input block to the given array.
        /// </summary>
        /// <param name="output">
        /// Buffer to store the compressed data.
        /// </param>
        /// <param name="offset">
        /// Offset into the output array.
        /// </param>
        /// <param name="length">
        /// The maximum number of bytes that may be stored.
        /// </param>
        /// <returns>
        /// The number of compressed bytes added to the output, or 0 if either
        /// needsInput() or finished() returns true or length is zero.
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// If Finish() was previously called.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// If offset or length don't match the array length.
        /// </exception>
        public int Deflate(byte[] output, int offset, int length)
        {
            int origLength = length;

            if (state == ClosedState)
            {
                throw new InvalidOperationException("Deflater closed");
            }

            if (state < BusyState)
            {
                // output header
                int header = (DEFLATED +
                    ((DeflaterConstants.MaxWindowBits - 8) << 4)) << 8;
                int level_flags = ((int)Strength - 1) >> 1;
                if (level_flags < 0 || level_flags > 3)
                {
                    level_flags = 3;
                }

                header |= level_flags << 6;
                if ((state & IsSetDict) != 0)
                {
                    // Dictionary was set
                    header |= DeflaterConstants.PresetDictionary;
                }

                header += 31 - (header % 31);

                pending.WriteShortMSB(header);
                if ((state & IsSetDict) != 0)
                {
                    int chksum = engine.Adler;
                    engine.ResetAdler();
                    pending.WriteShortMSB(chksum >> 16);
                    pending.WriteShortMSB(chksum & 0xffff);
                }

                state = BusyState | (state & (IsFlushing | IsFinishing));
            }

            for (; ;)
            {
                int count = pending.Flush(output, offset, length);
                offset += count;
                TotalOut += count;
                length -= count;

                if (length == 0 || state == FinishedState)
                {
                    break;
                }

                if (!engine.Deflate((state & IsFlushing) != 0, (state & IsFinishing) != 0))
                {
                    switch (state)
                    {
                        case BusyState:
                            // We need more input now
                            return origLength - length;
                        case FlushingState:
                            if (Strength != CompressionStrength.None)
                            {
                                /* We have to supply some lookahead.  8 bit lookahead
                                 * is needed by the zlib inflater, and we must fill
                                 * the next byte, so that all bits are flushed.
                                 */
                                int neededbits = 8 + ((-pending.BitCount) & 7);
                                while (neededbits > 0)
                                {
                                    /* write a static tree block consisting solely of
                                     * an EOF:
                                     */
                                    pending.WriteBits(2, 10);
                                    neededbits -= 10;
                                }
                            }

                            state = BusyState;
                            break;
                        case FinishingState:
                            pending.AlignToByte();

                            // Compressed data is complete.  Write footer information if required.
                            if (!noZlibHeaderOrFooter)
                            {
                                int adler = engine.Adler;
                                pending.WriteShortMSB(adler >> 16);
                                pending.WriteShortMSB(adler & 0xffff);
                            }

                            state = FinishedState;
                            break;
                    }
                }
            }

            return origLength - length;
        }

        /// <summary>
        /// Sets the dictionary which should be used in the deflate process.
        /// This call is equivalent to. <code>setDictionary(dict, 0, dict.Length)</code>.
        /// </summary>
        /// <param name="dictionary">
        /// the dictionary.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// if SetInput () or Deflate () were already called or another dictionary was already set.
        /// </exception>
        public void SetDictionary(byte[] dictionary)
        {
            SetDictionary(dictionary, 0, dictionary.Length);
        }

        /// <summary>
        /// Sets the dictionary which should be used in the deflate process.
        /// The dictionary is a byte array containing strings that are
        /// likely to occur in the data which should be compressed.  The
        /// dictionary is not stored in the compressed output, only a
        /// checksum.  To decompress the output you need to supply the same
        /// dictionary again.
        /// </summary>
        /// <param name="dictionary">
        /// The dictionary data.
        /// </param>
        /// <param name="index">
        /// The index where dictionary information commences.
        /// </param>
        /// <param name="count">
        /// The number of bytes in the dictionary.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// If SetInput () or Deflate() were already called or another dictionary was already set.
        /// </exception>
        public void SetDictionary(byte[] dictionary, int index, int count)
        {
            if (state != InitState)
            {
                throw new InvalidOperationException();
            }

            state = SetDictState;
            engine.SetDictionary(dictionary, index, count);
        }

        #region Instance Fields

        /// <summary>
        /// If true no Zlib/RFC1950 headers or footers are generated.
        /// </summary>
        readonly bool noZlibHeaderOrFooter;

        /// <summary>
        /// The current state.
        /// </summary>
        int state;

        /// <summary>
        /// The pending output.
        /// </summary>
        DeflaterPending pending;

        /// <summary>
        /// The deflater engine.
        /// </summary>
        DeflaterEngine engine;
        #endregion
    }
}
