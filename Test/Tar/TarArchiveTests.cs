using System.IO;
using System.Text;
using NUnit.Framework;
using Cave.Compression.Tar;
using Cave.IO;
using Cave.Compression.Tests.TestSupport;
using Cave.Compression.Core;

namespace Cave.Compression.Tests.Tar
{
    [TestFixture]
    public class TarArchiveTests
    {
        #region Public Methods

        [Test]
        [Category("Tar")]
        [Category("CreatesTempFile")]
        [Platform(Include = "Win", Reason = "Backslashes are only treated as path separators on windows")]
        [TestCase(@"output\", @"../file")]
        [TestCase(@"output/", @"..\file")]
        [TestCase("output", @"..\output.txt")]
        [TestCase(@"output\", @"..\output.txt")]
        public void ExtractingContentsOnWindowsWithDisallowedPathsFails(string outputDir, string fileName)
        {
            Assert.Throws<InvalidNameException>(() => ExtractTarOK(outputDir, fileName, enforceRelative: false));
        }

        [Test]
        [Category("Tar")]
        [Category("CreatesTempFile")]
        [Platform(Include = "Win", Reason = "Backslashes are only treated as path separators on windows")]
        [TestCase(@"output\", @"..\file")]
        [TestCase(@"output/", @"..\file")]
        [TestCase("output", @"..\output.txt")]
        [TestCase("output/", @".\../../output.txt")]
        public void ExtractingContentsOnWindowsWithDisallowedPathsPossibleWithEnforceRelative(string outputDir, string fileName)
        {
            Assert.DoesNotThrow(() => ExtractTarOK(outputDir, fileName, enforceRelative: true));
        }

        [Test]
        [Category("Tar")]
        [Category("CreatesTempFile")]
        [TestCase("output/", "../file")]
        [TestCase("output", "../output.txt")]
        [TestCase("output/", "../output.txt")]
        [TestCase("output/", "./../../output.txt")]
        public void ExtractingContentsWithDisallowedPathsFails(string outputDir, string fileName)
        {
            Assert.Throws<InvalidNameException>(() => ExtractTarOK(outputDir, fileName, enforceRelative: false));
        }

        [Test]
        [Category("Tar")]
        [Category("CreatesTempFile")]
        [TestCase("output/", "../file")]
        [TestCase("output", "../output.txt")]
        [TestCase("output/", "../output.txt")]
        [TestCase("output/", "./../../output.txt")]
        public void ExtractingContentsWithDisallowedPathsPossibleWithEnforceRelative(string outputDir, string fileName)
        {
            Assert.DoesNotThrow(() => ExtractTarOK(outputDir, fileName, enforceRelative: true));
        }

        [Test]
        [Category("Tar")]
        [Category("CreatesTempFile")]
        public void ExtractingContentsWithEnforceRelativeFailsOnIntentionalTraversal()
        {
            Assert.Throws<InvalidNameException>(() => ExtractTarOK("output", "./main/path/../../../parent", enforceRelative: true));
        }

        [Test]
        [Category("Tar")]
        [Category("CreatesTempFile")]
        public void ExtractingContentsWithEnforceRelativeSucceeds()
        {
            Assert.DoesNotThrow(() => ExtractTarOK("output", "../file", enforceRelative: true));
        }

        [Test]
        [Category("Tar")]
        [Category("CreatesTempFile")]
        [TestCase("output")]
        public void ExtractingContentsWithNonTraversalPathSucceeds(string outputDir)
        {
            Assert.DoesNotThrow(() => ExtractTarOK(outputDir, "file", enforceRelative: false));
        }

        public void ExtractTarOK(string outputDir, string fileName, bool enforceRelative)
        {
            var fileContent = Encoding.UTF8.GetBytes("file content");
            using var tempDir = Utils.GetTempDir();

            var tempPath = tempDir.Fullpath;
            var extractPath = Path.Combine(tempPath, outputDir);
            var expectedOutputFile = enforceRelative ? Path.Combine(extractPath, Path.GetFileName(fileName)) : Path.Combine(extractPath, fileName);

            using var archiveStream = new MemoryStream();

            Directory.CreateDirectory(extractPath);

            using (var tos = new TarOutputStream(archiveStream, StringEncoding.UTF8) { IsStreamOwner = false })
            {
                var entry = TarEntry.CreateTarEntry(fileName);
                entry.Size = fileContent.Length;
                tos.PutNextEntry(entry);
                tos.Write(fileContent, 0, fileContent.Length);
                tos.CloseEntry();
            }

            archiveStream.Position = 0;

            using (var ta = TarArchive.CreateInputTarArchive(archiveStream, StringEncoding.UTF8))
            {
                ta.ExtractContents(extractPath, enforceRelative);
            }

            Assert.That(File.Exists(expectedOutputFile));
        }

        #endregion Public Methods
    }
}
