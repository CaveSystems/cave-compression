using System;
using System.IO;
using Cave.Compression.GZip;
using Cave.IO;

namespace Cave.Compression.Tar
{
    /// <summary>
    /// The TarWriter provides functions for writing UNIX tar archives.
    /// </summary>
    public class TarWriter : IDisposable
    {
        /// <summary>
        /// Create a new gzip compressed tar file (.tgz/.tar.gz)
        /// </summary>
        /// <param name="fileName">Name of the file to create</param>
        /// <returns>Returns a new <see cref="TarWriter"/> instance</returns>
        public static TarWriter CreateTGZ(string fileName)
        {
            return new TarWriter(File.Create(fileName), true);
        }

        /// <summary>
        /// Create a new uncompressed tar file (.tar)
        /// </summary>
        /// <param name="fileName">Name of the file to create</param>
        /// <returns>Returns a new <see cref="TarWriter"/> instance</returns>
        public static TarWriter CreateTar(string fileName)
        {
            return new TarWriter(File.Create(fileName), false);
        }

        Stream baseStream;
        TarOutputStream tarStream;

        /// <summary>
        /// Initializes a new instance of the <see cref="TarWriter"/> class.
        /// </summary>
        /// <param name="target">Target stream to write to</param>
        /// <param name="gzip">Use gzip compression</param>
        public TarWriter(Stream target, bool gzip)
        {
            var s = baseStream = target;
            if (gzip)
            {
                s = new GZipOutputStream(s);
            }

            tarStream = new TarOutputStream(s);
        }

        /// <summary>
        /// Adds all files within a specified directory to the archive.
        /// </summary>
        /// <param name="pathInTar">Relative path in tar file</param>
        /// <param name="directory">The root directory to be searched for files.</param>
        /// <param name="fileMask">The file mask</param>
        /// <param name="search">The search option</param>
        /// <param name="callback">The callback used during stream copy</param>
        /// <param name="userItem">A user item for the callback</param>
        /// <returns>Returns true if the operation completed, false if the callback used <see cref="ProgressEventArgs.Break"/>.</returns>
        public bool AddDirectory(string pathInTar, string directory, string fileMask = "*.*", SearchOption search = SearchOption.AllDirectories, ProgressCallback callback = null, object userItem = null)
        {
            if (tarStream == null)
            {
                throw new ObjectDisposedException(nameof(TarWriter));
            }

            foreach (var file in Directory.GetFiles(directory, fileMask, search))
            {
                if (!file.StartsWith(directory))
                {
                    throw new InvalidOperationException("Invalid relative path!");
                }

                var name = pathInTar + '/' + file.Substring(directory.Length);
                using (var stream = File.OpenRead(file))
                {
                    var result = AddFile(name, stream, (int)stream.Length, callback, userItem);
                    if (!result)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Adds a file to the archive.
        /// </summary>
        /// <param name="pathInTar">Relative path in tar file</param>
        /// <param name="fileName">Full path to the file to add.</param>
        /// <param name="callback">The callback used during stream copy</param>
        /// <param name="userItem">A user item for the callback</param>
        /// <returns>Returns true if the operation completed, false if the callback used <see cref="ProgressEventArgs.Break"/>.</returns>
        public bool AddFile(string pathInTar, string fileName, ProgressCallback callback = null, object userItem = null)
        {
            using (var fs = File.OpenRead(fileName))
            {
                return AddFile(pathInTar, fs, (int)fs.Length, callback, userItem);
            }
        }

        /// <summary>
        /// Adds a file to the archive.
        /// </summary>
        /// <param name="pathInTar">Relative path in tar file</param>
        /// <param name="content">The content to add.</param>
        /// <param name="callback">The callback used during stream copy</param>
        /// <param name="userItem">A user item for the callback</param>
        /// <returns>Returns true if the operation completed, false if the callback used <see cref="ProgressEventArgs.Break"/>.</returns>
        public bool AddFile(string pathInTar, byte[] content, ProgressCallback callback = null, object userItem = null)
        {
            using (MemoryStream ms = new MemoryStream(content))
            {
                return AddFile(pathInTar, ms, content.Length, callback, userItem);
            }
        }

        /// <summary>
        /// Adds a file to the archive.
        /// </summary>
        /// <param name="pathInTar">Relative path in tar file</param>
        /// <param name="source">The stream to copy the file data from.</param>
        /// <param name="size">The number of bytes to copy from source.</param>
        /// <param name="callback">The callback used during stream copy</param>
        /// <param name="userItem">A user item for the callback</param>
        /// <returns>Returns true if the operation completed, false if the callback used <see cref="ProgressEventArgs.Break"/>.</returns>
        public bool AddFile(string pathInTar, Stream source, int size, ProgressCallback callback = null, object userItem = null)
        {
            if (tarStream == null)
            {
                throw new ObjectDisposedException(nameof(TarWriter));
            }

            pathInTar = pathInTar.Replace('\\', '/').TrimEnd('/');
            while (pathInTar.Contains("//"))
            {
                pathInTar = pathInTar.Replace("//", "/");
            }

            var entry = TarEntry.CreateTarEntry(pathInTar);
            entry.Size = size;
            tarStream.PutNextEntry(entry);
            var result = source.CopyBlocksTo(tarStream, size, callback, userItem);
            if (result < size)
            {
                Dispose();
                return false;
            }

            tarStream.CloseEntry();
            return true;
        }

        /// <summary>
        /// Closes the writer and the underlying stream.
        /// </summary>
        public void Close()
        {
            if (tarStream != null)
            {
                tarStream.Finish();
#if NETSTANDARD13
                tarStream.Dispose();
#else
                tarStream.Close();
#endif
                Dispose();
            }
        }

        /// <summary>
        /// Releases the unmanaged resources used and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
#region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (tarStream != null)
            {
                if (disposing)
                {
                    tarStream.Dispose();
                }

                baseStream = null;
                tarStream = null;
            }
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
#endregion
    }
}
