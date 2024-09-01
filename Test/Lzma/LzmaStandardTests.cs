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

    #region Private Methods

    void Test(long size)
    {
        var ms = new MemoryStream();
        var stopWatch = StopWatch.StartNew();
        LzmaStandard.Compress(new TestStream(size), ms);
        var compressionTime = stopWatch.Elapsed;
        ms.Position = 0;
        var checkStream = new CheckStream(size);
        stopWatch.Reset();
        LzmaStandard.Decompress(ms, checkStream);
        var decompressionTime = stopWatch.Elapsed;
        Assert.IsTrue(checkStream.Position == checkStream.Length);
        Assert.IsTrue(checkStream.Position == size);
        var ratio = ms.Length / (double)size;
        SystemConsole.WriteLine($"Lzma Streaming {size.FormatBinarySize()} compressionTime={compressionTime.FormatTime()} decompressionTime={decompressionTime.FormatTime()} speed={(size / decompressionTime.TotalSeconds).FormatBinarySize()}/s ratio={ratio:P} ok.");
    }

    #endregion Private Methods

    #region Public Methods

#if !NET20
    [Test]
    public void LzmaTest10kiB()
    {
        Test(10 * 1024);
    }

    [Test]
    [Category("Performance")]
    [Category("Long Running")]
    [Explicit("Long Running")]
    public void LzmaTest64MiB()
    {
        Test(64 * 1024 * 1024);
    }
#endif

    #endregion Public Methods
}
