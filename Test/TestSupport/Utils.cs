using NUnit.Framework;
using System;
using System.IO;

namespace Cave.Compression.Tests.TestSupport
{
    /// <summary>Miscellaneous test utilities.</summary>
    public static class Utils
    {
        #region Private Fields

        static Random random = new Random();

        #endregion Private Fields

        #region Private Methods

        static void Compare(byte[] a, byte[] b)
        {
            if (a == null)
            {
                throw new ArgumentNullException(nameof(a));
            }

            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            Assert.AreEqual(a.Length, b.Length);
            for (var i = 0; i < a.Length; ++i)
            {
                Assert.AreEqual(a[i], b[i]);
            }
        }

        #endregion Private Methods

        #region Internal Fields

        internal const int DefaultSeed = 5;

        #endregion Internal Fields

        #region Public Classes

        public class TempDir : IDisposable
        {
            #region Private Fields

            bool disposed = false;

            #endregion Private Fields

            #region Protected Methods

            protected virtual void Dispose(bool disposing)
            {
                if (!disposed)
                {
                    if (disposing && Directory.Exists(Fullpath))
                    {
                        try
                        {
                            Directory.Delete(Fullpath, true);
                        }
                        catch { }
                    }

                    disposed = true;
                }
            }

            #endregion Protected Methods

            #region Internal Methods

            internal string CreateDummyFile(int size = -1)
                => CreateDummyFile(GetDummyFileName(), size);

            internal string CreateDummyFile(string name, int size = -1)
            {
                var fileName = Path.Combine(Fullpath, name);
                WriteDummyData(fileName, size);
                return fileName;
            }

            #endregion Internal Methods

            #region Public Constructors

            public TempDir()
            {
                Fullpath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(Fullpath);
            }

            #endregion Public Constructors

            #region Public Properties

            public string Fullpath { get; internal set; }

            #endregion Public Properties

            #region Public Methods

            // To detect redundant calls
            public void Dispose()
                => Dispose(true);

            #endregion Public Methods
        }

        public class TempFile : IDisposable
        {
            #region Private Fields

            bool disposed = false;

            #endregion Private Fields

            #region Protected Methods

            protected virtual void Dispose(bool disposing)
            {
                if (!disposed)
                {
                    if (disposing && File.Exists(Filename))
                    {
                        try
                        {
                            File.Delete(Filename);
                        }
                        catch { }
                    }

                    disposed = true;
                }
            }

            #endregion Protected Methods

            #region Public Constructors

            public TempFile()
            {
                Filename = Path.GetTempFileName();
            }

            #endregion Public Constructors

            #region Public Properties

            public string Filename { get; internal set; }

            #endregion Public Properties

            #region Public Methods

            // To detect redundant calls
            public void Dispose()
                => Dispose(true);

            #endregion Public Methods
        }

        #endregion Public Classes

        #region Public Methods

        /// <summary>Creates a buffer of <paramref name="size"/> pseudo-random bytes</summary>
        /// <param name="size"></param>
        /// <param name="seed"></param>
        /// <returns></returns>
        public static byte[] GetDummyBytes(int size, int seed = DefaultSeed)
        {
            var random = new Random(seed);
            var bytes = new byte[size];
            random.NextBytes(bytes);
            return bytes;
        }

        public static TempFile GetDummyFile(int size = -1)
        {
            var tempFile = new TempFile();
            WriteDummyData(tempFile.Filename, size);
            return tempFile;
        }

        public static string GetDummyFileName()
            => $"{random.Next():x8}{random.Next():x8}{random.Next():x8}";

        public static TempDir GetTempDir() => new TempDir();

        public static void WriteDummyData(string fileName, int size = -1)
        {
            if (size < 0)
            {
                File.WriteAllText(fileName, DateTime.UtcNow.Ticks.ToString("x16"));
            }
            else if (size > 0)
            {
                var bytes = Array.CreateInstance(typeof(byte), size) as byte[];
                random.NextBytes(bytes);
                File.WriteAllBytes(fileName, bytes);
            }
        }

        #endregion Public Methods
    }
}
