using Cave.Compression.BZip2;
using Cave.Compression.Tests.TestSupport;
using NUnit.Framework;
using System;
using System.IO;

namespace Cave.Compression.Tests.BZip2
{
    /// <summary>
    /// This class contains test cases for Bzip2 compression
    /// </summary>
    [TestFixture]
    public class BZip2Suite
    {
        /// <summary>
        /// Basic compress/decompress test BZip2
        /// </summary>
        [Test]
        [Category("BZip2")]
        public void BasicRoundTrip()
        {
            var ms = new MemoryStream();
            var outStream = new BZip2OutputStream(ms);

            var buf = new byte[10000];
            var rnd = new Random();
            rnd.NextBytes(buf);

            outStream.Write(buf, 0, buf.Length);
            outStream.Close();
            ms = new MemoryStream(ms.GetBuffer());
            ms.Seek(0, SeekOrigin.Begin);

            using var inStream = new BZip2InputStream(ms);
            var buf2 = new byte[buf.Length];
            var pos = 0;
            while (true)
            {
                var numRead = inStream.Read(buf2, pos, 4096);
                if (numRead <= 0)
                {
                    break;
                }
                pos += numRead;
            }

            for (var i = 0; i < buf.Length; ++i)
            {
                Assert.AreEqual(buf2[i], buf[i]);
            }
        }

        /// <summary>
        /// Check that creating an empty archive is handled ok
        /// </summary>
        [Test]
        [Category("BZip2")]
        public void CreateEmptyArchive()
        {
            var ms = new MemoryStream();
            var outStream = new BZip2OutputStream(ms);
            outStream.Close();
            ms = new MemoryStream(ms.GetBuffer());

            ms.Seek(0, SeekOrigin.Begin);

            using var inStream = new BZip2InputStream(ms);
            var buffer = new byte[1024];
            var pos = 0;
            while (true)
            {
                var numRead = inStream.Read(buffer, 0, buffer.Length);
                if (numRead <= 0)
                {
                    break;
                }
                pos += numRead;
            }

            Assert.AreEqual(pos, 0);
        }

        /*
		BZip2OutputStream outStream_;
		BZip2InputStream inStream_;
		WindowedStream window_;
		long readTarget_;
		long writeTarget_;
        */

        [Test]
        [Category("BZip2")]
        [Category("Performance")]
        [Explicit("Long-running")]
        public void WriteThroughput()
        {
            PerformanceTesting.TestWrite(
                size: TestDataSize.Small,
                output: w => new BZip2OutputStream(w)
            );
        }

        [Test]
        [Category("BZip2")]
        [Category("Performance")]
        [Explicit("Long-running")]
        public void ReadWriteThroughput()
        {
            PerformanceTesting.TestReadWrite(
                size: TestDataSize.Small,
                input: w => new BZip2InputStream(w),
                output: w => new BZip2OutputStream(w)
            );
        }
    }
}
