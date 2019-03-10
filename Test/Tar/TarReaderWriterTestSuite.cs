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

        string GetTempDir()
        {
            while (true)
            {
                var temp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}");
                if (!Directory.Exists(temp))
                {
                    Directory.CreateDirectory(temp);
                    return temp;
                }
            }
        }

        [Test]
        [Category("Tar")]
        public void TarReaderWriterFiles()
        {
            var tempFileName = Path.GetTempFileName();
            Dictionary<string, byte[]> checks = new Dictionary<string, byte[]>();
            Random random = new Random();

            var temp1 = GetTempDir();
            var temp2 = GetTempDir();

            using (var stream = File.Create(tempFileName))
            {
                using (TarWriter writer = new TarWriter(stream, true))
                {
                    for (int i = 0; i < 1000; i++)
                    {
                        byte[] buffer = new byte[random.Next(64 * 1024)];
                        var name = $"{i % 10}/{i}.txt";
                        var fullName = Path.Combine(temp1, name);
                        Directory.CreateDirectory(Path.GetDirectoryName(fullName));
                        File.WriteAllBytes(fullName, buffer);
                        checks.Add(name, buffer);
                    }
                    writer.AddDirectory("/", temp1);
                }
            }

            using (var stream = File.OpenRead(tempFileName))
            {
                using (TarReader reader = new TarReader(stream, true))
                {
                    reader.UnpackTo(temp2);
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
                        CollectionAssert.AreEqual(expectedContent, File.ReadAllBytes(Path.Combine(temp1, name)));
                        CollectionAssert.AreEqual(expectedContent, File.ReadAllBytes(Path.Combine(temp2, name)));
                    }
                }
            }

            Directory.Delete(temp1, true);
            Directory.Delete(temp2, true);
        }
    }
}
