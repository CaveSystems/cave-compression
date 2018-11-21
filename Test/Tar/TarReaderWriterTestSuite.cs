using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cave.Compression.Tar;
using NUnit.Framework;

namespace Cave.Compression.Tests.Tar
{
    /// <summary>
    /// This class contains test cases for Tar archive handling.
    /// </summary>
    [TestFixture]
    public class TarReaderWriterTestSuite
    {
        [Test]
        [Category("Tar")]
        public void TarReaderWriter()
        {
            Dictionary<string, byte[]> checks = new Dictionary<string, byte[]>();
            Random random = new Random();
            byte[] data;
            using (MemoryStream stream = new MemoryStream())
            {
                using (TarWriter writer = new TarWriter(stream, true))
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        byte[] buffer = new byte[random.Next(64 * 1024)];
                        var name = $"{i % 10}/{i} {buffer.GetHashCode().ToString("x")}.txt";
                        checks.Add(name, buffer);
                        writer.AddFile(name, buffer);
                    }
                }
                data = stream.ToArray();
            }
            using (MemoryStream stream = new MemoryStream(data))
            {
                using (TarReader reader = new TarReader(stream, true))
                {
                    var copy = checks.ToDictionary(i => i.Key, i => i.Value);
                    while (reader.ReadNext(out TarEntry entry, out byte[] content))
                    {
                        if (!copy.TryGetValue(entry.Name, out byte[] expectedContent))
                        {
                            Assert.Fail("Entry name not found at source!");
                        }

                        copy.Remove(entry.Name);
                        CollectionAssert.AreEqual(expectedContent, content);
                    }
                }
            }
        }

        [Test]
        [Category("Tar")]
        public void TarReaderWriterFiles()
        {
            var tempFileName = Path.GetTempFileName();
            var temp = Path.Combine(Path.GetTempPath(), "TarReaderWriter");
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
            Directory.CreateDirectory(temp);
            Dictionary<string, byte[]> checks = new Dictionary<string, byte[]>();
            Random random = new Random();
            try
            {
                using (var stream = File.Create(tempFileName))
                {
                    using (TarWriter writer = new TarWriter(stream, true))
                    {
                        for (int i = 0; i < 1000; i++)
                        {
                            byte[] buffer = new byte[random.Next(64 * 1024)];
                            var name = $"{i % 10}/{i} {buffer.GetHashCode().ToString("x")}.txt";
                            var fullName = Path.Combine(temp, name);
                            Directory.CreateDirectory(Path.GetDirectoryName(fullName));
                            File.WriteAllBytes(fullName, buffer);
                            checks.Add(name, buffer);
                        }
                        writer.AddDirectory("/", temp);
                    }
                }
                Directory.Delete(temp, true);
                Directory.CreateDirectory(temp);
                using (var stream = File.OpenRead(tempFileName))
                {
                    using (TarReader reader = new TarReader(stream, true))
                    {
                        reader.UnpackTo(temp);
                    }
                }
                using (var stream = File.OpenRead(tempFileName))
                {
                    using (TarReader reader = new TarReader(stream, true))
                    {
                        var copy = checks.ToDictionary(i => i.Key, i => i.Value);
                        while (reader.ReadNext(out TarEntry entry, out byte[] content))
                        {
                            string name = entry.Name.TrimStart('/');
                            if (!copy.TryGetValue(name, out byte[] expectedContent))
                            {
                                Assert.Fail("Entry name not found at source!");
                            }

                            copy.Remove(name);
                            CollectionAssert.AreEqual(expectedContent, content);

                            var fullName = Path.Combine(temp, name);
                            CollectionAssert.AreEqual(expectedContent, File.ReadAllBytes(fullName));
                        }
                    }
                }
            }
            finally
            {
                File.Delete(tempFileName);
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, true);
                }
            }

        }
    }
}
