using System;
using System.IO;

namespace Cave.Compression.Core
{
    /// <summary>
    /// This is the DeflaterHuffman class.
    ///
    /// This class is <i>not</i> thread safe.  This is inherent in the API, due
    /// to the split of Deflate and SetInput.
    ///
    /// author of the original java version : Jochen Hoenicke.
    /// </summary>
    class DeflaterHuffman
    {
        #region constants

        const int BufferSize = 1 << (DeflaterConstants.DefaultMemoryLevel + 6);
        const int LiteralCount = 286;

        /// <summary>
        /// Number of distance codes.
        /// </summary>
        const int DistanceCount = 30;

        /// <summary>
        /// Number of codes used to transfer bit lengths.
        /// </summary>
        const int BitLengthCount = 19;

        /// <summary>
        /// repeat previous bit length 3-6 times (2 bits of repeat count).
        /// </summary>
        const int Repeat3to6 = 16;

        /// <summary>
        /// repeat a zero length 3-10 times  (3 bits of repeat count).
        /// </summary>
        const int Repeat3to10 = 17;

        /// <summary>
        /// repeat a zero length 11-138 times  (7 bits of repeat count).
        /// </summary>
        const int Repeat11to138 = 18;

        const int EndOfFileSymbol = 256;

        /// <summary>
        /// The lengths of the bit length codes are sent in order of decreasing probability, to avoid transmitting the lengths for unused bit length codes.
        /// </summary>
        static readonly int[] BitLengthOrder = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

        static readonly byte[] Reverse4Bits =
        {
            0,
            8,
            4,
            12,
            2,
            10,
            6,
            14,
            1,
            9,
            5,
            13,
            3,
            11,
            7,
            15
        };
        #endregion

        #region static class

        /// <summary>
        /// Reverse the bits of a 16 bit value.
        /// </summary>
        /// <param name="toReverse">Value to reverse bits.</param>
        /// <returns>Value with bits reversed.</returns>
        public static short BitReverse(int toReverse)
        {
            return (short)(
                Reverse4Bits[toReverse & 0xF] << 12 |
                Reverse4Bits[(toReverse >> 4) & 0xF] << 8 |
                Reverse4Bits[(toReverse >> 8) & 0xF] << 4 |
                Reverse4Bits[toReverse >> 12]);
        }

        static int Lcode(int length)
        {
            if (length == 255)
            {
                return 285;
            }

            int code = 257;
            while (length >= 8)
            {
                code += 4;
                length >>= 1;
            }

            return code + length;
        }

        static int Dcode(int distance)
        {
            int code = 0;
            while (distance >= 4)
            {
                code += 2;
                distance >>= 1;
            }

            return code + distance;
        }

        static readonly short[] StaticLiteralCodes;
        static readonly byte[] StaticLiteralLength;
        static readonly short[] StaticDistanceCodes;
        static readonly byte[] StaticDistanceLength;

        static DeflaterHuffman()
        {
            // See RFC 1951 3.2.6
            // Literal codes
            StaticLiteralCodes = new short[LiteralCount];
            StaticLiteralLength = new byte[LiteralCount];

            int i = 0;
            while (i < 144)
            {
                StaticLiteralCodes[i] = BitReverse((0x030 + i) << 8);
                StaticLiteralLength[i++] = 8;
            }

            while (i < 256)
            {
                StaticLiteralCodes[i] = BitReverse((0x190 - 144 + i) << 7);
                StaticLiteralLength[i++] = 9;
            }

            while (i < 280)
            {
                StaticLiteralCodes[i] = BitReverse((0x000 - 256 + i) << 9);
                StaticLiteralLength[i++] = 7;
            }

            while (i < LiteralCount)
            {
                StaticLiteralCodes[i] = BitReverse((0x0c0 - 280 + i) << 8);
                StaticLiteralLength[i++] = 8;
            }

            // Distance codes
            StaticDistanceCodes = new short[DistanceCount];
            StaticDistanceLength = new byte[DistanceCount];
            for (i = 0; i < DistanceCount; i++)
            {
                StaticDistanceCodes[i] = BitReverse(i << 11);
                StaticDistanceLength[i] = 5;
            }
        }
        #endregion

        #region class Tree
#pragma warning disable SA1401 // Fields must be private
        class Tree
        {
            #region Instance Fields
            readonly int[] bitLengthCounts;
            readonly int maxLength;
            short[] codes;
            DeflaterHuffman dh;

            public short[] Freqs;
            public byte[] Length;
            public int MinNumCodes;
            public int NumCodes;
            #endregion

            #region Constructors
            public Tree(DeflaterHuffman dh, int elems, int minCodes, int maxLength)
            {
                this.dh = dh;
                MinNumCodes = minCodes;
                this.maxLength = maxLength;
                Freqs = new short[elems];
                bitLengthCounts = new int[maxLength];
            }

            #endregion

            /// <summary>
            /// Resets the internal state of the tree.
            /// </summary>
            public void Reset()
            {
                for (int i = 0; i < Freqs.Length; i++)
                {
                    Freqs[i] = 0;
                }

                codes = null;
                Length = null;
            }

            public void WriteSymbol(int code)
            {
                // if (DeflaterConstants.DEBUGGING) {
                //                  freqs[code]--;
                //                  //        Console.Write("writeSymbol("+freqs.length+","+code+"): ");
                //              }
                dh.pending.WriteBits(codes[code] & 0xffff, Length[code]);
            }

            /// <summary>
            /// Check that all frequencies are zero.
            /// </summary>
            /// <exception cref="InvalidDataException">
            /// At least one frequency is non-zero.
            /// </exception>
            public void CheckEmpty()
            {
                bool empty = true;
                for (int i = 0; i < Freqs.Length; i++)
                {
                    empty &= Freqs[i] == 0;
                }

                if (!empty)
                {
                    throw new InvalidDataException("!Empty");
                }
            }

            /// <summary>
            /// Set static codes and length.
            /// </summary>
            /// <param name="staticCodes">new codes.</param>
            /// <param name="staticLengths">length for new codes.</param>
            public void SetStaticCodes(short[] staticCodes, byte[] staticLengths)
            {
                codes = staticCodes;
                Length = staticLengths;
            }

            /// <summary>
            /// Build dynamic codes and lengths.
            /// </summary>
            public void BuildCodes()
            {
                int numSymbols = Freqs.Length;
                int[] nextCode = new int[maxLength];
                int code = 0;

                codes = new short[Freqs.Length];

                // if (DeflaterConstants.DEBUGGING) {
                //                  //Console.WriteLine("buildCodes: "+freqs.Length);
                //              }
                for (int bits = 0; bits < maxLength; bits++)
                {
                    nextCode[bits] = code;
                    code += bitLengthCounts[bits] << (15 - bits);

                    // if (DeflaterConstants.DEBUGGING) {
                    //                      //Console.WriteLine("bits: " + ( bits + 1) + " count: " + bl_counts[bits]
                    //                                        +" nextCode: "+code);
                    //                  }
                }

#if DebugDeflation
				if ( DeflaterConstants.DEBUGGING && (code != 65536) ) 
				{
					throw new SharpZipBaseException("Inconsistent bl_counts!");
				}
#endif
                for (int i = 0; i < NumCodes; i++)
                {
                    int bits = Length[i];
                    if (bits > 0)
                    {
                        // if (DeflaterConstants.DEBUGGING) {
                        //                              //Console.WriteLine("codes["+i+"] = rev(" + nextCode[bits-1]+"),
                        //                                                +bits);
                        //                      }
                        codes[i] = BitReverse(nextCode[bits - 1]);
                        nextCode[bits - 1] += 1 << (16 - bits);
                    }
                }
            }

            public void BuildTree()
            {
                int numSymbols = Freqs.Length;

                /* heap is a priority queue, sorted by frequency, least frequent
                * nodes first.  The heap is a binary tree, with the property, that
                * the parent node is smaller than both child nodes.  This assures
                * that the smallest node is the first parent.
                *
                * The binary tree is encoded in an array:  0 is root node and
                * the nodes 2*n+1, 2*n+2 are the child nodes of node n.
                */
                int[] heap = new int[numSymbols];
                int heapLen = 0;
                int maxCode = 0;
                for (int n = 0; n < numSymbols; n++)
                {
                    int freq = Freqs[n];
                    if (freq != 0)
                    {
                        // Insert n into heap
                        int pos = heapLen++;
                        int ppos;
                        while (pos > 0 && Freqs[heap[ppos = (pos - 1) / 2]] > freq)
                        {
                            heap[pos] = heap[ppos];
                            pos = ppos;
                        }

                        heap[pos] = n;

                        maxCode = n;
                    }
                }

                /* We could encode a single literal with 0 bits but then we
                * don't see the literals.  Therefore we force at least two
                * literals to avoid this case.  We don't care about order in
                * this case, both literals get a 1 bit code.
                */
                while (heapLen < 2)
                {
                    int node = maxCode < 2 ? ++maxCode : 0;
                    heap[heapLen++] = node;
                }

                NumCodes = Math.Max(maxCode + 1, MinNumCodes);

                int numLeafs = heapLen;
                int[] childs = new int[(4 * heapLen) - 2];
                int[] values = new int[(2 * heapLen) - 1];
                int numNodes = numLeafs;
                for (int i = 0; i < heapLen; i++)
                {
                    int node = heap[i];
                    childs[2 * i] = node;
                    childs[(2 * i) + 1] = -1;
                    values[i] = Freqs[node] << 8;
                    heap[i] = i;
                }

                /* Construct the Huffman tree by repeatedly combining the least two
                * frequent nodes.
                */
                do
                {
                    int first = heap[0];
                    int last = heap[--heapLen];

                    // Propagate the hole to the leafs of the heap
                    int ppos = 0;
                    int path = 1;

                    while (path < heapLen)
                    {
                        if (path + 1 < heapLen && values[heap[path]] > values[heap[path + 1]])
                        {
                            path++;
                        }

                        heap[ppos] = heap[path];
                        ppos = path;
                        path = (path * 2) + 1;
                    }

                    /* Now propagate the last element down along path.  Normally
                    * it shouldn't go too deep.
                    */
                    int lastVal = values[last];
                    while ((path = ppos) > 0 && values[heap[ppos = (path - 1) / 2]] > lastVal)
                    {
                        heap[path] = heap[ppos];
                    }

                    heap[path] = last;

                    int second = heap[0];

                    // Create a new node father of first and second
                    last = numNodes++;
                    childs[2 * last] = first;
                    childs[(2 * last) + 1] = second;
                    int mindepth = Math.Min(values[first] & 0xff, values[second] & 0xff);
                    values[last] = lastVal = values[first] + values[second] - mindepth + 1;

                    // Again, propagate the hole to the leafs
                    ppos = 0;
                    path = 1;

                    while (path < heapLen)
                    {
                        if (path + 1 < heapLen && values[heap[path]] > values[heap[path + 1]])
                        {
                            path++;
                        }

                        heap[ppos] = heap[path];
                        ppos = path;
                        path = (ppos * 2) + 1;
                    }

                    // Now propagate the new element down along path
                    while ((path = ppos) > 0 && values[heap[ppos = (path - 1) / 2]] > lastVal)
                    {
                        heap[path] = heap[ppos];
                    }

                    heap[path] = last;
                }
                while (heapLen > 1);

                if (heap[0] != (childs.Length / 2) - 1)
                {
                    throw new InvalidDataException("Heap invariant violated");
                }

                BuildLength(childs);
            }

            /// <summary>
            /// Get encoded length.
            /// </summary>
            /// <returns>Encoded length, the sum of frequencies * lengths.</returns>
            public int GetEncodedLength()
            {
                int len = 0;
                for (int i = 0; i < Freqs.Length; i++)
                {
                    len += Freqs[i] * Length[i];
                }

                return len;
            }

            /// <summary>
            /// Scan a literal or distance tree to determine the frequencies of the codes
            /// in the bit length tree.
            /// </summary>
            /// <param name="blTree">Bit length tree.</param>
            public void CalcBLFreq(Tree blTree)
            {
                int max_count;               /* max repeat count */
                int min_count;               /* min repeat count */
                int count;                   /* repeat count of the current code */
                int curlen = -1;             /* length of current code */

                int i = 0;
                while (i < NumCodes)
                {
                    count = 1;
                    int nextlen = Length[i];
                    if (nextlen == 0)
                    {
                        max_count = 138;
                        min_count = 3;
                    }
                    else
                    {
                        max_count = 6;
                        min_count = 3;
                        if (curlen != nextlen)
                        {
                            blTree.Freqs[nextlen]++;
                            count = 0;
                        }
                    }

                    curlen = nextlen;
                    i++;

                    while (i < NumCodes && curlen == Length[i])
                    {
                        i++;
                        if (++count >= max_count)
                        {
                            break;
                        }
                    }

                    if (count < min_count)
                    {
                        blTree.Freqs[curlen] += (short)count;
                    }
                    else if (curlen != 0)
                    {
                        blTree.Freqs[Repeat3to6]++;
                    }
                    else if (count <= 10)
                    {
                        blTree.Freqs[Repeat3to10]++;
                    }
                    else
                    {
                        blTree.Freqs[Repeat11to138]++;
                    }
                }
            }

            /// <summary>
            /// Write tree values.
            /// </summary>
            /// <param name="blTree">Tree to write.</param>
            public void WriteTree(Tree blTree)
            {
                int max_count;               // max repeat count
                int min_count;               // min repeat count
                int count;                   // repeat count of the current code
                int curlen = -1;             // length of current code

                int i = 0;
                while (i < NumCodes)
                {
                    count = 1;
                    int nextlen = Length[i];
                    if (nextlen == 0)
                    {
                        max_count = 138;
                        min_count = 3;
                    }
                    else
                    {
                        max_count = 6;
                        min_count = 3;
                        if (curlen != nextlen)
                        {
                            blTree.WriteSymbol(nextlen);
                            count = 0;
                        }
                    }

                    curlen = nextlen;
                    i++;

                    while (i < NumCodes && curlen == Length[i])
                    {
                        i++;
                        if (++count >= max_count)
                        {
                            break;
                        }
                    }

                    if (count < min_count)
                    {
                        while (count-- > 0)
                        {
                            blTree.WriteSymbol(curlen);
                        }
                    }
                    else if (curlen != 0)
                    {
                        blTree.WriteSymbol(Repeat3to6);
                        dh.pending.WriteBits(count - 3, 2);
                    }
                    else if (count <= 10)
                    {
                        blTree.WriteSymbol(Repeat3to10);
                        dh.pending.WriteBits(count - 3, 3);
                    }
                    else
                    {
                        blTree.WriteSymbol(Repeat11to138);
                        dh.pending.WriteBits(count - 11, 7);
                    }
                }
            }

            void BuildLength(int[] childs)
            {
                Length = new byte[Freqs.Length];
                int numNodes = childs.Length / 2;
                int numLeafs = (numNodes + 1) / 2;
                int overflow = 0;

                for (int i = 0; i < maxLength; i++)
                {
                    bitLengthCounts[i] = 0;
                }

                // First calculate optimal bit lengths
                int[] lengths = new int[numNodes];
                lengths[numNodes - 1] = 0;

                for (int i = numNodes - 1; i >= 0; i--)
                {
                    if (childs[(2 * i) + 1] != -1)
                    {
                        int bitLength = lengths[i] + 1;
                        if (bitLength > maxLength)
                        {
                            bitLength = maxLength;
                            overflow++;
                        }

                        lengths[childs[2 * i]] = lengths[childs[(2 * i) + 1]] = bitLength;
                    }
                    else
                    {
                        // A leaf node
                        int bitLength = lengths[i];
                        bitLengthCounts[bitLength - 1]++;
                        Length[childs[2 * i]] = (byte)lengths[i];
                    }
                }

                // if (DeflaterConstants.DEBUGGING) {
                //                  //Console.WriteLine("Tree "+freqs.Length+" lengths:");
                //                  for (int i=0; i < numLeafs; i++) {
                //                      //Console.WriteLine("Node "+childs[2*i]+" freq: "+freqs[childs[2*i]]
                //                                        + " len: "+length[childs[2*i]]);
                //                  }
                //              }
                if (overflow == 0)
                {
                    return;
                }

                int incrBitLen = maxLength - 1;
                do
                {
                    // Find the first bit length which could increase:
                    while (bitLengthCounts[--incrBitLen] == 0)
                    {
                    }

                    // Move this node one down and remove a corresponding
                    // number of overflow nodes.
                    do
                    {
                        bitLengthCounts[incrBitLen]--;
                        bitLengthCounts[++incrBitLen]++;
                        overflow -= 1 << (maxLength - 1 - incrBitLen);
                    }
                    while (overflow > 0 && incrBitLen < maxLength - 1);
                }
                while (overflow > 0);

                /* We may have overshot above.  Move some nodes from maxLength to
                * maxLength-1 in that case.
                */
                bitLengthCounts[maxLength - 1] += overflow;
                bitLengthCounts[maxLength - 2] -= overflow;

                /* Now recompute all bit lengths, scanning in increasing
                * frequency.  It is simpler to reconstruct all lengths instead of
                * fixing only the wrong ones. This idea is taken from 'ar'
                * written by Haruhiko Okumura.
                *
                * The nodes were inserted with decreasing frequency into the childs
                * array.
                */
                int nodePtr = 2 * numLeafs;
                for (int bits = maxLength; bits != 0; bits--)
                {
                    int n = bitLengthCounts[bits - 1];
                    while (n > 0)
                    {
                        int childPtr = 2 * childs[nodePtr++];
                        if (childs[childPtr + 1] == -1)
                        {
                            // We found another leaf
                            Length[childs[childPtr]] = (byte)bits;
                            n--;
                        }
                    }
                }

                // if (DeflaterConstants.DEBUGGING) {
                //                  //Console.WriteLine("*** After overflow elimination. ***");
                //                  for (int i=0; i < numLeafs; i++) {
                //                      //Console.WriteLine("Node "+childs[2*i]+" freq: "+freqs[childs[2*i]]
                //                                        + " len: "+length[childs[2*i]]);
                //                  }
                //              }
            }
        }
#pragma warning restore SA1401 // Fields must be private
        #endregion

        #region Instance Fields

        /// <summary>
        /// Buffer for distances.
        /// </summary>
        readonly short[] distanceBuffer;
        readonly byte[] literalBuffer;

        /// <summary>
        /// Pending buffer to use.
        /// </summary>
        DeflaterPending pending;

        Tree literalTree;
        Tree distTree;
        Tree blTree;

        int lastLiteral;
        int extraBits;

        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="DeflaterHuffman"/> class.
        /// </summary>
        /// <param name="pending">Pending buffer to use.</param>
        public DeflaterHuffman(DeflaterPending pending)
        {
            this.pending = pending;

            literalTree = new Tree(this, LiteralCount, 257, 15);
            distTree = new Tree(this, DistanceCount, 1, 15);
            blTree = new Tree(this, BitLengthCount, 4, 7);

            distanceBuffer = new short[BufferSize];
            literalBuffer = new byte[BufferSize];
        }

        /// <summary>
        /// Reset internal state.
        /// </summary>
        public void Reset()
        {
            lastLiteral = 0;
            extraBits = 0;
            literalTree.Reset();
            distTree.Reset();
            blTree.Reset();
        }

        /// <summary>
        /// Write all trees to pending buffer.
        /// </summary>
        /// <param name="blTreeCodes">The number/rank of treecodes to send.</param>
        public void SendAllTrees(int blTreeCodes)
        {
            blTree.BuildCodes();
            literalTree.BuildCodes();
            distTree.BuildCodes();
            pending.WriteBits(literalTree.NumCodes - 257, 5);
            pending.WriteBits(distTree.NumCodes - 1, 5);
            pending.WriteBits(blTreeCodes - 4, 4);
            for (int rank = 0; rank < blTreeCodes; rank++)
            {
                pending.WriteBits(blTree.Length[BitLengthOrder[rank]], 3);
            }

            literalTree.WriteTree(blTree);
            distTree.WriteTree(blTree);

#if DebugDeflation
			if (DeflaterConstants.DEBUGGING) {
				blTree.CheckEmpty();
			}
#endif
        }

        /// <summary>
        /// Compress current buffer writing data to pending buffer.
        /// </summary>
        public void CompressBlock()
        {
            for (int i = 0; i < lastLiteral; i++)
            {
                int litlen = literalBuffer[i] & 0xff;
                int dist = distanceBuffer[i];
                if (dist-- != 0)
                {
                    // if (DeflaterConstants.DEBUGGING) {
                    //                      Console.Write("["+(dist+1)+","+(litlen+3)+"]: ");
                    //                  }
                    int lc = Lcode(litlen);
                    literalTree.WriteSymbol(lc);

                    int bits = (lc - 261) / 4;
                    if (bits > 0 && bits <= 5)
                    {
                        pending.WriteBits(litlen & ((1 << bits) - 1), bits);
                    }

                    int dc = Dcode(dist);
                    distTree.WriteSymbol(dc);

                    bits = (dc / 2) - 1;
                    if (bits > 0)
                    {
                        pending.WriteBits(dist & ((1 << bits) - 1), bits);
                    }
                }
                else
                {
                    // if (DeflaterConstants.DEBUGGING) {
                    //                      if (litlen > 32 && litlen < 127) {
                    //                          Console.Write("("+(char)litlen+"): ");
                    //                      } else {
                    //                          Console.Write("{"+litlen+"}: ");
                    //                      }
                    //                  }
                    literalTree.WriteSymbol(litlen);
                }
            }

#if DebugDeflation
			if (DeflaterConstants.DEBUGGING) {
				Console.Write("EOF: ");
			}
#endif
            literalTree.WriteSymbol(EndOfFileSymbol);

#if DebugDeflation
			if (DeflaterConstants.DEBUGGING) {
				literalTree.CheckEmpty();
				distTree.CheckEmpty();
			}
#endif
        }

        /// <summary>
        /// Flush block to output with no compression.
        /// </summary>
        /// <param name="stored">Data to write.</param>
        /// <param name="storedOffset">Index of first byte to write.</param>
        /// <param name="storedLength">Count of bytes to write.</param>
        /// <param name="lastBlock">True if this is the last block.</param>
        public void FlushStoredBlock(byte[] stored, int storedOffset, int storedLength, bool lastBlock)
        {
#if DebugDeflation
			//			if (DeflaterConstants.DEBUGGING) {
			//				//Console.WriteLine("Flushing stored block "+ storedLength);
			//			}
#endif
            pending.WriteBits((DeflaterConstants.StoredBlock << 1) + (lastBlock ? 1 : 0), 3);
            pending.AlignToByte();
            pending.WriteShort(storedLength);
            pending.WriteShort(~storedLength);
            pending.WriteBlock(stored, storedOffset, storedLength);
            Reset();
        }

        /// <summary>
        /// Flush block to output with compression.
        /// </summary>
        /// <param name="stored">Data to flush.</param>
        /// <param name="storedOffset">Index of first byte to flush.</param>
        /// <param name="storedLength">Count of bytes to flush.</param>
        /// <param name="lastBlock">True if this is the last block.</param>
        public void FlushBlock(byte[] stored, int storedOffset, int storedLength, bool lastBlock)
        {
            literalTree.Freqs[EndOfFileSymbol]++;

            // Build trees
            literalTree.BuildTree();
            distTree.BuildTree();

            // Calculate bitlen frequency
            literalTree.CalcBLFreq(blTree);
            distTree.CalcBLFreq(blTree);

            // Build bitlen tree
            blTree.BuildTree();

            int blTreeCodes = 4;
            for (int i = 18; i > blTreeCodes; i--)
            {
                if (blTree.Length[BitLengthOrder[i]] > 0)
                {
                    blTreeCodes = i + 1;
                }
            }

            int opt_len = 14 + (blTreeCodes * 3) + blTree.GetEncodedLength() +
                literalTree.GetEncodedLength() + distTree.GetEncodedLength() +
                extraBits;

            int static_len = extraBits;
            for (int i = 0; i < LiteralCount; i++)
            {
                static_len += literalTree.Freqs[i] * StaticLiteralLength[i];
            }

            for (int i = 0; i < DistanceCount; i++)
            {
                static_len += distTree.Freqs[i] * StaticDistanceLength[i];
            }

            if (opt_len >= static_len)
            {
                // Force static trees
                opt_len = static_len;
            }

            if (storedOffset >= 0 && storedLength + 4 < opt_len >> 3)
            {
                // Store Block

                // if (DeflaterConstants.DEBUGGING) {
                //                  //Console.WriteLine("Storing, since " + storedLength + " < " + opt_len
                //                                    + " <= " + static_len);
                //              }
                FlushStoredBlock(stored, storedOffset, storedLength, lastBlock);
            }
            else if (opt_len == static_len)
            {
                // Encode with static tree
                pending.WriteBits((DeflaterConstants.StaticTree << 1) + (lastBlock ? 1 : 0), 3);
                literalTree.SetStaticCodes(StaticLiteralCodes, StaticLiteralLength);
                distTree.SetStaticCodes(StaticDistanceCodes, StaticDistanceLength);
                CompressBlock();
                Reset();
            }
            else
            {
                // Encode with dynamic tree
                pending.WriteBits((DeflaterConstants.DynamicTree << 1) + (lastBlock ? 1 : 0), 3);
                SendAllTrees(blTreeCodes);
                CompressBlock();
                Reset();
            }
        }

        /// <summary>
        /// Get value indicating if internal buffer is full.
        /// </summary>
        /// <returns>true if buffer is full.</returns>
        public bool IsFull()
        {
            return lastLiteral >= BufferSize;
        }

        /// <summary>
        /// Add literal to buffer.
        /// </summary>
        /// <param name="literal">Literal value to add to buffer.</param>
        /// <returns>Value indicating internal buffer is full.</returns>
        public bool TallyLit(int literal)
        {
            distanceBuffer[lastLiteral] = 0;
            literalBuffer[lastLiteral++] = (byte)literal;
            literalTree.Freqs[literal]++;
            return IsFull();
        }

        /// <summary>
        /// Add distance code and length to literal and distance trees.
        /// </summary>
        /// <param name="distance">Distance code.</param>
        /// <param name="length">Length.</param>
        /// <returns>Value indicating if internal buffer is full.</returns>
        public bool TallyDist(int distance, int length)
        {
            distanceBuffer[lastLiteral] = (short)distance;
            literalBuffer[lastLiteral++] = (byte)(length - 3);

            int lc = Lcode(length - 3);
            literalTree.Freqs[lc]++;
            if (lc >= 265 && lc < 285)
            {
                extraBits += (lc - 261) / 4;
            }

            int dc = Dcode(distance - 1);
            distTree.Freqs[dc]++;
            if (dc >= 4)
            {
                extraBits += (dc / 2) - 1;
            }

            return IsFull();
        }
    }
}
