using System;

namespace Cave.Compression.Streams
{
    /// <summary>
    /// This class allows us to retrieve a specified number of bits from
    /// the input buffer, as well as copy big byte blocks.
    ///
    /// It uses an int buffer to store up to 31 bits for direct
    /// manipulation.  This guarantees that we can get at least 16 bits,
    /// but we only need at most 15, so this is all safe.
    ///
    /// There are some optimizations in this class, for example, you must
    /// never peek more than 8 bits more than needed, and you must first
    /// peek bits before you may drop them.  This is not a general purpose
    /// class but optimized for the behaviour of the Inflater.
    ///
    /// authors of the original java version : John Leuner, Jochen Hoenicke.
    /// </summary>
    class StreamManipulator
    {
        #region Instance Fields
        byte[] window;
        int windowStart;
        int windowEnd;
        uint buffer;
        #endregion

        /// <summary>
        /// Get the next sequence of bits but don't increase input pointer.  bitCount must be
        /// less or equal 16 and if this call succeeds, you must drop
        /// at least n - 8 bits in the next call.
        /// </summary>
        /// <param name="bitCount">The number of bits to peek.</param>
        /// <returns>
        /// the value of the bits, or -1 if not enough bits available.  */.
        /// </returns>
        public int PeekBits(int bitCount)
        {
            if (AvailableBits < bitCount)
            {
                if (windowStart == windowEnd)
                {
                    return -1; // ok
                }

                buffer |= (uint)((window[windowStart++] & 0xff |
                                 (window[windowStart++] & 0xff) << 8) << AvailableBits);
                AvailableBits += 16;
            }

            return (int)(buffer & ((1 << bitCount) - 1));
        }

        /// <summary>
        /// Drops the next n bits from the input.  You should have called PeekBits
        /// with a bigger or equal n before, to make sure that enough bits are in
        /// the bit buffer.
        /// </summary>
        /// <param name="bitCount">The number of bits to drop.</param>
        public void DropBits(int bitCount)
        {
            buffer >>= bitCount;
            AvailableBits -= bitCount;
        }

        /// <summary>
        /// Gets the next n bits and increases input pointer.  This is equivalent
        /// to <see cref="PeekBits"/> followed by <see cref="DropBits"/>, except for correct error handling.
        /// </summary>
        /// <param name="bitCount">The number of bits to retrieve.</param>
        /// <returns>
        /// the value of the bits, or -1 if not enough bits available.
        /// </returns>
        public int GetBits(int bitCount)
        {
            int bits = PeekBits(bitCount);
            if (bits >= 0)
            {
                DropBits(bitCount);
            }

            return bits;
        }

        /// <summary>
        /// Gets or sets the number of bits available in the bit buffer.
        /// This must be only called when a previous PeekBits() returned -1.
        /// </summary>
        /// <returns>
        /// the number of bits available.
        /// </returns>
        public int AvailableBits { get; set; }

        /// <summary>
        /// Gets the number of bytes available.
        /// </summary>
        /// <returns>
        /// The number of bytes available.
        /// </returns>
        public int AvailableBytes
        {
            get
            {
                return windowEnd - windowStart + (AvailableBits >> 3);
            }
        }

        /// <summary>
        /// Skips to the next byte boundary.
        /// </summary>
        public void SkipToByteBoundary()
        {
            buffer >>= AvailableBits & 7;
            AvailableBits &= ~7;
        }

        /// <summary>
        /// Gets a value indicating whether SetInput can be called.
        /// </summary>
        public bool IsNeedingInput
        {
            get
            {
                return windowStart == windowEnd;
            }
        }

        /// <summary>
        /// Copies bytes from input buffer to output buffer starting
        /// at output[offset].  You have to make sure, that the buffer is
        /// byte aligned.  If not enough bytes are available, copies fewer
        /// bytes.
        /// </summary>
        /// <param name="output">
        /// The buffer to copy bytes to.
        /// </param>
        /// <param name="offset">
        /// The offset in the buffer at which copying starts.
        /// </param>
        /// <param name="length">
        /// The length to copy, 0 is allowed.
        /// </param>
        /// <returns>
        /// The number of bytes copied, 0 if no bytes were available.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Length is less than zero.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Bit buffer isnt byte aligned.
        /// </exception>
        public int CopyBytes(byte[] output, int offset, int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            if ((AvailableBits & 7) != 0)
            {
                // bits_in_buffer may only be 0 or a multiple of 8
                throw new InvalidOperationException("Bit buffer is not byte aligned!");
            }

            int count = 0;
            while ((AvailableBits > 0) && (length > 0))
            {
                output[offset++] = (byte)buffer;
                buffer >>= 8;
                AvailableBits -= 8;
                length--;
                count++;
            }

            if (length == 0)
            {
                return count;
            }

            int avail = windowEnd - windowStart;
            if (length > avail)
            {
                length = avail;
            }

            Array.Copy(window, windowStart, output, offset, length);
            windowStart += length;

            if (((windowStart - windowEnd) & 1) != 0)
            {
                // We always want an even number of bytes in input, see peekBits
                buffer = (uint)(window[windowStart++] & 0xff);
                AvailableBits = 8;
            }

            return count + length;
        }

        /// <summary>
        /// Resets state and empties internal buffers.
        /// </summary>
        public void Reset()
        {
            buffer = 0;
            windowStart = windowEnd = AvailableBits = 0;
        }

        /// <summary>
        /// Add more input for consumption.
        /// Only call when IsNeedingInput returns true.
        /// </summary>
        /// <param name="buffer">data to be input.</param>
        /// <param name="offset">offset of first byte of input.</param>
        /// <param name="count">number of bytes of input to add.</param>
        public void SetInput(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Cannot be negative");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Cannot be negative");
            }

            if (windowStart < windowEnd)
            {
                throw new InvalidOperationException("Old input was not completely processed");
            }

            int end = offset + count;

            // We want to throw an ArrayIndexOutOfBoundsException early.
            // Note the check also handles integer wrap around.
            if ((offset > end) || (end > buffer.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if ((count & 1) != 0)
            {
                // We always want an even number of bytes in input, see PeekBits
                this.buffer |= (uint)((buffer[offset++] & 0xff) << AvailableBits);
                AvailableBits += 8;
            }

            window = buffer;
            windowStart = offset;
            windowEnd = end;
        }
    }
}
