using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cave.IO;

namespace Cave.Compression.Lzma;

public static class LzmaMini
{
    #region Public Methods

    public static IEnumerable<LzmaCoderProperties> BuildSettings(int[] validLitPosStateBits = null, int[] validPosStateBits = null, LzmaMatchFinderType[] validMatchFinders = null)
    {
        if (validLitPosStateBits is null) validLitPosStateBits = new[] { 0, 1, 2, 3, 4 };
        if (validPosStateBits is null) validPosStateBits = new[] { 0, 1, 2, 3, 4 };
        if (validMatchFinders is null) validMatchFinders = new[] { LzmaMatchFinderType.BT2, LzmaMatchFinderType.BT4 };
        //context bits do not make sense because we got not context during mini packs
        var settings = new List<LzmaCoderProperties>(256);
        foreach (var matchFinder in validMatchFinders)
        {
            foreach (var litPosStateBits in validLitPosStateBits)
            {
                foreach (var posStateBits in validPosStateBits)
                {
                    settings.Add(new LzmaCoderProperties() { MatchFinder = matchFinder, EndMarker = false, LiteralContextBits = 0, LiteralPosStateBits = litPosStateBits, PosStateBits = posStateBits });
                }
            }
        }
        return settings.ToArray();
    }

    public static byte[] Compress(byte[] data, LzmaCoderProperties coderProperties = null)
    {
        if (coderProperties == null) coderProperties = new LzmaCoderProperties()
        {
            EndMarker = false,
            LiteralContextBits = 0,
            LiteralPosStateBits = 0,
            MatchFinder = LzmaMatchFinderType.BT2,
            NumFastBytes = 273,
            PosStateBits = 2
        };
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        LzmaEncoder encoder = new();
        encoder.SetDictionarySize((uint)data.Length);
        encoder.SetCoderProperties(coderProperties);
        var state = encoder.GetCoderState();
        var writer = new DataWriter(output);
        writer.Write7BitEncoded32(data.Length);
        writer.Write(state);
        encoder.Encode(input, output, data.Length);
        return output.ToArray();
    }

    /// <summary>Compress the specified byte block</summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public static byte[] Decompress(byte[] data)
    {
        if (data[0] == 0)
        {
            //uncompressed
            return data[1..];
        }
        using var output = new MemoryStream();
        LzmaDecoder decoder = new();
        using var input = new MemoryStream(data);
        var reader = new DataReader(input);
        var dataSize = reader.Read7BitEncodedUInt32();
        var state = reader.ReadByte();
        decoder.SetDecoderState(state);
        decoder.SetDictionarySize(dataSize);
        decoder.Decode(input, output, dataSize);
        return output.ToArray();
    }

    /// <summary>Compress the specified byte block</summary>
    /// <param name="data"></param>
    /// <returns></returns>
    public static byte[] ParallelCompress(byte[] data, IEnumerable<LzmaCoderProperties> coderProperties)
    {
        var syncRoot = new object();
        var best = Uncompressed(data);
        Parallel.ForEach(coderProperties, (coder) =>
        {
            var block = Compress(data, coder);
            lock (syncRoot)
            {
                if (block.Length < best.Length)
                {
                    best = block;
                }
            }
        });
        return best;
    }

    public static byte[] Uncompressed(byte[] data)
    {
        //uncompressed
        var block = new byte[data.Length + 1];
        Buffer.BlockCopy(data, 0, block, 1, data.Length);
        return block;
    }

    #endregion Public Methods
}
