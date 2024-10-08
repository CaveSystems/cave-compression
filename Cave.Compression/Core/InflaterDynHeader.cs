using System;
using System.IO;
using Cave.Compression.Streams;

namespace Cave.Compression.Core;

sealed class InflaterDynHeader
{
    #region Private Fields

    const int BLLENS = 3;
    const int BLNUM = 2;
    const int DNUM = 1;
    const int LENS = 4;
    const int LNUM = 0;
    const int REPS = 5;

    static readonly int[] BitLengthOrder = Globals.BitLengthOrder;
    static readonly int[] RepeatBits = [2, 3, 7];
    static readonly int[] RepeatMin = [3, 3, 11];
    byte[]? blLens;
    int blnum;
    InflaterHuffmanTree? blTree;
    int dnum;
    byte lastLen;
    byte[] litdistLens = [];
    int lnum;
    int mode;
    int num;
    int ptr;
    int repSymbol;

    #endregion Private Fields

    #region Public Methods

    public InflaterHuffmanTree BuildDistTree()
    {
        var distLens = new byte[dnum];
        Array.Copy(litdistLens, lnum, distLens, 0, dnum);
        return new InflaterHuffmanTree(distLens);
    }

    public InflaterHuffmanTree BuildLitLenTree()
    {
        var litlenLens = new byte[lnum];
        Array.Copy(litdistLens, 0, litlenLens, 0, lnum);
        return new InflaterHuffmanTree(litlenLens);
    }

    public bool Decode(StreamManipulator input)
    {
    decode_loop:
        for (; ; )
        {
            switch (mode)
            {
                case LNUM:
                    lnum = input.PeekBits(5);
                    if (lnum < 0)
                    {
                        return false;
                    }

                    lnum += 257;
                    input.DropBits(5);

                    // System.err.println("LNUM: "+lnum);
                    mode = DNUM;
                    goto case DNUM; // fall through
                case DNUM:
                    dnum = input.PeekBits(5);
                    if (dnum < 0)
                    {
                        return false;
                    }

                    dnum++;
                    input.DropBits(5);

                    // System.err.println("DNUM: "+dnum);
                    num = lnum + dnum;
                    litdistLens = new byte[num];
                    mode = BLNUM;
                    goto case BLNUM; // fall through
                case BLNUM:
                    blnum = input.PeekBits(4);
                    if (blnum < 0)
                    {
                        return false;
                    }

                    blnum += 4;
                    input.DropBits(4);
                    blLens = new byte[19];
                    ptr = 0;

                    // System.err.println("BLNUM: "+blnum);
                    mode = BLLENS;
                    goto case BLLENS; // fall through
                case BLLENS:
                    if (blLens is null) throw new InvalidOperationException("blLens is null");
                    while (ptr < blnum)
                    {
                        var len = input.PeekBits(3);
                        if (len < 0)
                        {
                            return false;
                        }

                        input.DropBits(3);

                        // System.err.println("blLens["+BL_ORDER[ptr]+"]: "+len);
                        blLens[BitLengthOrder[ptr]] = (byte)len;
                        ptr++;
                    }

                    blTree = new InflaterHuffmanTree(blLens);
                    blLens = null;
                    ptr = 0;
                    mode = LENS;
                    goto case LENS; // fall through
                case LENS:
                {
                    int symbol;
                    if (blTree is null) throw new InvalidOperationException("blTree is null");
                    while (((symbol = blTree.GetSymbol(input)) & ~15) == 0)
                    {
                        /* Normal case: symbol in [0..15] */

                        // System.err.println("litdistLens["+ptr+"]: "+symbol);
                        litdistLens[ptr++] = lastLen = (byte)symbol;

                        if (ptr == num)
                        {
                            /* Finished */
                            return true;
                        }
                    }

                    /* need more input ? */
                    if (symbol < 0)
                    {
                        return false;
                    }

                    /* otherwise repeat code */
                    if (symbol >= 17)
                    {
                        /* repeat zero */
                        // System.err.println("repeating zero");
                        lastLen = 0;
                    }
                    else
                    {
                        if (ptr == 0)
                        {
                            throw new InvalidDataException();
                        }
                    }

                    repSymbol = symbol - 16;
                }

                mode = REPS;
                goto case REPS; // fall through
                case REPS:
                {
                    var bits = RepeatBits[repSymbol];
                    var count = input.PeekBits(bits);
                    if (count < 0)
                    {
                        return false;
                    }

                    input.DropBits(bits);
                    count += RepeatMin[repSymbol];

                    // System.err.println("litdistLens repeated: "+count);
                    if (ptr + count > num)
                    {
                        throw new InvalidDataException();
                    }

                    while (count-- > 0)
                    {
                        litdistLens[ptr++] = lastLen;
                    }

                    if (ptr == num)
                    {
                        /* Finished */
                        return true;
                    }
                }

                mode = LENS;
                goto decode_loop;
            }
        }
    }

    #endregion Public Methods
}
