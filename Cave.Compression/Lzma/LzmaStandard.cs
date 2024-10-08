using System.IO;
using Cave.IO;

namespace Cave.Compression.Lzma;

/// <summary>Provides lzma standard compression / decompression</summary>
public static class LzmaStandard
{
    #region Public Methods

    /// <summary>Compresses the specified input stream.</summary>
    /// <param name="input"></param>
    /// <param name="output"></param>
    /// <param name="properties"></param>
    public static void Compress(Stream input, Stream output, LzmaCoderProperties? properties = null)
    {
        var datasize = input.CanSeek ? input.Length : -1L;
        LzmaEncoder encoder = new();
        if (properties is not null)
        {
            encoder.SetCoderProperties(properties);
        }
        else
        {
            encoder.SetDictionarySize(1 << LzmaEncoder.KDefaultDictionaryLogSize);
            encoder.SetWriteEndMarkerMode(datasize <= 0);
        }
        var state = encoder.GetEncoderState();
        var writer = new DataWriter(output);
        writer.Write(state);
        writer.Write(encoder.DictionarySize);
        writer.Write(datasize);
        encoder.Encode(input, output, datasize);
    }

    /// <summary>Decompresses the specified input stream.</summary>
    /// <param name="input"></param>
    /// <param name="output"></param>
    public static void Decompress(Stream input, Stream output)
    {
        LzmaDecoder decoder = new();
        var reader = new DataReader(input);
        var state = reader.ReadByte();
        var dictionarySize = reader.ReadUInt32();
        var dataSize = reader.ReadInt64();
        decoder.SetDecoderState(state);
        decoder.SetDictionarySize(dictionarySize);
        decoder.Decode(input, output, dataSize);
    }

    #endregion Public Methods
}
