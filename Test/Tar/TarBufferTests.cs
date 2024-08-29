using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cave.Compression.Tar;
using NUnit.Framework;
using Cave.Compression.Tests.TestSupport;

namespace Cave.Compression.Tests.Tar
{
    [TestFixture]
    public class TarBufferTests
    {
        #region Public Methods

        [Test]
        public void TestSimpleReadWrite()
        {
            var ms = new MemoryStream();
            var reader = TarBuffer.CreateInputTarBuffer(ms, 1);
            var writer = TarBuffer.CreateOutputTarBuffer(ms, 1);
            writer.IsStreamOwner = false;

            var block = Cave.Compression.Tests.TestSupport.Utils.GetDummyBytes(TarBuffer.BlockSize);

            writer.WriteBlock(block);
            writer.WriteBlock(block);
            writer.WriteBlock(block);
            writer.Close();

            ms.Seek(0, SeekOrigin.Begin);

            var block0 = reader.ReadBlock();
            var block1 = reader.ReadBlock();
            var block2 = reader.ReadBlock();
            Assert.AreEqual(block, block0);
            Assert.AreEqual(block, block1);
            Assert.AreEqual(block, block2);
            writer.Close();
        }

        [Test]
        public void TestSkipBlock()
        {
            var ms = new MemoryStream();
            var reader = TarBuffer.CreateInputTarBuffer(ms, 1);
            var writer = TarBuffer.CreateOutputTarBuffer(ms, 1);
            writer.IsStreamOwner = false;

            var block0 = Utils.GetDummyBytes(TarBuffer.BlockSize);
            var block1 = Utils.GetDummyBytes(TarBuffer.BlockSize);

            writer.WriteBlock(block0);
            writer.WriteBlock(block1);
            writer.Close();

            ms.Seek(0, SeekOrigin.Begin);

            reader.SkipBlock();
            var block = reader.ReadBlock();
            Assert.AreEqual(block, block1);
            writer.Close();
        }

        #endregion Public Methods
    }
}
