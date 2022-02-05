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
            var checks = new Dictionary<string, byte[]>();
            var random = new Random();
            byte[] data;
            using (var stream = new MemoryStream())
            {
                using (var writer = new TarWriter(stream, true))
                {
                    for (var i = 0; i < 1000; i++)
                    {
                        var buffer = new byte[random.Next(64 * 1024)];
                        var name = $"{i % 10}/{i} {buffer.GetHashCode().ToString("x")}.txt";
                        checks.Add(name, buffer);
                        writer.AddFile(name, buffer);
                    }
                }
                data = stream.ToArray();
            }
            using (var stream = new MemoryStream(data))
            {
                using (var reader = new TarReader(stream, true))
                {
                    var copy = checks.ToDictionary(i => i.Key, i => i.Value);
                    while (reader.ReadNext(out var entry, out var content))
                    {
                        if (!copy.TryGetValue(entry.Name, out var expectedContent))
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
            var checks = new Dictionary<string, byte[]>();
            var random = new Random();

            var temp1 = GetTempDir();
            var temp2 = GetTempDir();

            using (var stream = File.Create(tempFileName))
            {
                using (var writer = new TarWriter(stream, true))
                {
                    for (var i = 0; i < 1000; i++)
                    {
                        var buffer = new byte[random.Next(64 * 1024)];
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
                using (var reader = new TarReader(stream, true))
                {
                    reader.UnpackTo(temp2);
                }
            }

            using (var stream = File.OpenRead(tempFileName))
            {
                using (var reader = new TarReader(stream, true))
                {
                    var copy = checks.ToDictionary(i => i.Key, i => i.Value);
                    while (reader.ReadNext(out var entry, out var content))
                    {
                        var name = entry.Name.TrimStart('/');
                        if (!copy.TryGetValue(name, out var expectedContent))
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
