using System;
using System.IO;
using Cave;
using Cave.Collections;
using Cave.IO;
using Cave.Console;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Cave.Compression.Lzma;

namespace Cave.Compression.Tests.Lzma;

[TestFixture]
public class LzmaStandardTests
{
    #region Private Classes

    class CheckStream : Stream
    {
        #region Private Fields

        long length;

        #endregion Private Fields

        #region Public Constructors

        public CheckStream(long length) => this.length = length;

        #endregion Public Constructors

        #region Public Properties

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => length;

        public override long Position { get; set; }

        #endregion Public Properties

        #region Public Methods

        public override void Flush() => throw new NotImplementedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

        public override void SetLength(long value) => throw new NotImplementedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var expected = (byte)(Position++ ^ 0xA3);
                Assert.IsTrue(buffer[offset++] == expected);
            }
        }

        #endregion Public Methods
    }

    class TestStream : Stream
    {
        #region Private Fields

        long length;

        #endregion Private Fields

        #region Public Constructors

        public TestStream(long length) => this.length = length;

        #endregion Public Constructors

        #region Public Properties

        public override bool CanRead => true;
        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get; set; }

        #endregion Public Properties

        #region Public Methods

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            count = (int)Math.Min(count, length - Position);
            for (var i = 0; i < count; i++)
            {
                buffer[offset++] = (byte)(Position++ ^ 0xA3);
            }
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();

        public override void SetLength(long value) => throw new NotImplementedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

        #endregion Public Methods
    }

    #endregion Private Classes

    #region Public Methods

    [Test]
    public void LzmaTest()
    {
        for (long i = 10000; i < uint.MaxValue; i *= 100)
        {
            var ms = new MemoryStream();
            var stopWatch = StopWatch.StartNew();
            LzmaStandard.Compress(new TestStream(i), ms);
            var compressionTime = stopWatch.Elapsed;
            ms.Position = 0;
            var checkStream = new CheckStream(i);
            stopWatch.Reset();
            LzmaStandard.Decompress(ms, checkStream);
            var decompressionTime = stopWatch.Elapsed;
            Assert.IsTrue(checkStream.Position == checkStream.Length);
            Assert.IsTrue(checkStream.Position == i);
            var ratio = ms.Length / (double)i;
            SystemConsole.WriteLine($"Lzma Streaming {i.FormatSize()} compressionTime={compressionTime.FormatTime()} decompressionTime={decompressionTime.FormatTime()} ratio={ratio:P} ok.");
        }
    }

    #endregion Public Methods
}
