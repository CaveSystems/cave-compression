using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cave.Compression.Tar;
using Cave.IO;
using NUnit.Framework;
using Cave.Compression.Tests;
using Cave.Compression.Tests.TestSupport;

namespace Cave.Compression.Tests.Tar
{
    public class TarInputStreamTests
    {
        #region Public Methods

        [Test]
        public void ReadEmptyStream()
        {
            Assert.DoesNotThrow(() =>
            {
                using var emptyStream = new MemoryStream(new byte[0]);
                using var tarInputStream = new TarInputStream(emptyStream, StringEncoding.UTF_8);
                while (tarInputStream.GetNextEntry() is { } tarEntry)
                {
                }
            }, "reading from an empty input stream should not cause an error");
        }

        [Test]
        public void TestRead()
        {
            var entryBytes = Utils.GetDummyBytes(2000);
            using var ms = new MemoryStream();
            using (var tos = new TarOutputStream(ms, StringEncoding.UTF_8) { IsStreamOwner = false })
            {
                var e = TarEntry.CreateTarEntry("some entry");
                e.Size = entryBytes.Length;
                tos.PutNextEntry(e);
                tos.Write(entryBytes, 0, entryBytes.Length);
                tos.CloseEntry();
            }

            ms.Seek(0, SeekOrigin.Begin);

            using var tis = new TarInputStream(ms, StringEncoding.UTF8);
            var entry = tis.GetNextEntry();
            Assert.AreEqual("some entry", entry.Name);
            var buffer = new byte[1000]; // smaller than 2 blocks
            var read0 = tis.Read(buffer, 0, buffer.Length);
            Assert.AreEqual(1000, read0);
            Assert.AreEqual(entryBytes.GetRange(0, 1000), buffer);

            var read1 = tis.Read(buffer, 0, 5);
            Assert.AreEqual(5, read1);
            Assert.AreEqual(entryBytes.GetRange(1000, 5), buffer.GetRange(0, 5));

            var read2 = tis.Read(buffer, 0, 20);
            Assert.AreEqual(20, read2);
            Assert.AreEqual(entryBytes.GetRange(1005, 20), buffer.GetRange(0, 20));

            var read3 = tis.Read(buffer, 0, 975);
            Assert.AreEqual(975, read3);
            Assert.AreEqual(entryBytes.GetRange(1025, 975), buffer.GetRange(0, 975));
        }

        #endregion Public Methods
    }
}
