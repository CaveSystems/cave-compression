using System;
using Cave.Collections;
using Cave.IO;
using Cave.Console;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Cave.Compression.Lzma;
using System.Linq;

namespace Cave.Compression.Tests.Lzma;

[TestFixture]
public class LzmaMiniTests
{
    #region Private Structs

    struct SampleStruct : IEquatable<SampleStruct>
    {
        #region Private Fields

        static Random random = new Random();
        bool Bool;

        byte Byte;

        double Double;

        float Float;

        int Int;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 200)]
        string String;

        #endregion Private Fields

        #region Public Methods

        public static SampleStruct Random()
        {
            var block = new byte[200];
            random.NextBytes(block);
            block = block.Where(b => b > 16 && b < 128).ToArray();
            return new SampleStruct()
            {
                Double = Math.Round(random.NextDouble() * 600, 3),
                Float = (float)Math.Round(random.NextDouble() * 600, 3),
                Int = random.Next(),
                Byte = (byte)random.Next(),
                Bool = random.Next(0, 100) < 50,
                String = ASCII.GetCleanString(block),
            };
        }

        public override bool Equals(object obj) => obj is SampleStruct s && Equals(s);

        public bool Equals(SampleStruct other) => Equals(Double, other.Double) && Equals(Float, other.Float) && Equals(Int, other.Int) && Equals(Byte, other.Byte) && Equals(Bool, other.Bool) && Equals(String, other.String);

        public override int GetHashCode() => DefaultHashingFunction.Combine(Double, Float, Int, Byte, Bool, String);

        #endregion Public Methods
    }

    #endregion Private Structs

    #region Public Methods

#if !NET20
    [Test]
    public void LzmaMiniTest()
    {
        var ratio = 0d;
        for (var i = 0; i < 1000; i++)
        {
            var sample = SampleStruct.Random();
            var block = MarshalStruct.GetBytes(sample);
            var compressed = LzmaMini.Compress(block);
            var decompressed = LzmaMini.Decompress(compressed);
            ratio += compressed.Length / (double)decompressed.Length;
            Assert.IsTrue(DefaultComparer.Equals(decompressed, block));
            var result = MarshalStruct.GetStruct<SampleStruct>(decompressed);
            Assert.AreEqual(sample, result);
        }
        ratio /= 1000d;
        SystemConsole.WriteLine($"Ratio: {ratio:P}");
    }
#endif

    #endregion Public Methods
}
