using System;
using System.IO;
using Cave.Compression.GZip;

namespace Cave.Compression.Tar
{
    /// <summary>The TarWriter provides functions for reading UNIX tar archives.</summary>
    public class TarReader : IDisposable
    {
        /// <summary>Reads an existing gzip compressed tar file (.tgz/.tar.gz).</summary>
        /// <param name="fileName">Name of the file to read.</param>
        /// <returns>Returns a new <see cref="TarReader"/> instance.</returns>
        public static TarReader ReadTGZ(string fileName)
        {
            return new TarReader(File.Create(fileName), true);
        }

        /// <summary>Reads an existing uncompressed tar file (.tar).</summary>
        /// <param name="fileName">Name of the file to read.</param>
        /// <returns>Returns a new <see cref="TarReader"/> instance.</returns>
        public static TarReader ReadTar(string fileName)
        {
            return new TarReader(File.Create(fileName), false);
        }

        /// <summary>
        /// Callback to be used by <see cref="ReadAll(GetStreamForEntry, Completed, ProgressCallback, object)"/> and <see cref="ReadNext(GetStreamForEntry,
        /// Completed, ProgressCallback, object)"/> functions to acquire the target stream to write to.
        /// </summary>
        /// <param name="entry">The entry to be written.</param>
        /// <returns>Returns the target stream to write to.</returns>
        public delegate Stream GetStreamForEntry(TarEntry entry);

        /// <summary>
        /// Completed callback for <see cref="ReadAll(GetStreamForEntry, Completed, ProgressCallback, object)"/> and <see cref="ReadNext(GetStreamForEntry,
        /// Completed, ProgressCallback, object)"/> functions.
        /// </summary>
        /// <param name="entry">Completed entry.</param>
        /// <param name="stream">Stream the entry was written to.</param>
        public delegate void Completed(TarEntry entry, Stream stream);

        Stream baseStream;
        TarInputStream tarStream;

        /// <summary>Initializes a new instance of the <see cref="TarReader"/> class.</summary>
        /// <param name="source">Source stream to read from.</param>
        /// <param name="gunzip">Use gunzip decompression.</param>
        public TarReader(Stream source, bool gunzip)
        {
            var s = baseStream = source;
            if (gunzip)
            {
                s = new GZipInputStream(s);
            }

            tarStream = new TarInputStream(s);
        }

        /// <summary>Reads the next file.</summary>
        /// <param name="tarEntry">The read entry (null if no more entries available).</param>
        /// <param name="content">The read content (null if no more entries available).</param>
        /// <param name="callback">The callback used during stream copy.</param>
        /// <param name="userItem">A user item for the callback.</param>
        /// <returns>Returns true if the operation completed, false if the callback used <see cref="ProgressEventArgs.Break"/>.</returns>
        public bool ReadNext(out TarEntry tarEntry, out byte[] content, ProgressCallback callback = null, object userItem = null)
        {
            if (tarStream == null)
            {
                throw new ObjectDisposedException(nameof(TarReader));
            }

            while ((tarEntry = tarStream.GetNextEntry()) != null)
            {
                if (tarEntry.IsDirectory)
                {
                    continue;
                }

                using var ms = new MemoryStream();
                tarStream.CopyEntryContents(ms, callback, userItem);
                content = ms.ToArray();
                return true;
            }

            content = null;
            return false;
        }

        /// <summary>Reads the next file using callback functions to acquire and close per entry target streams.</summary>
        /// <param name="streamForEntry">Callback for aquiring a target stream for the specified entry.</param>
        /// <param name="complete">Callback after stream was written and may be closed.</param>
        /// <param name="callback">The callback used during stream copy.</param>
        /// <param name="userItem">A user item for the callback.</param>
        /// <returns>Returns true if the operation completed, false if the callback used <see cref="ProgressEventArgs.Break"/>.</returns>
        public bool ReadNext(GetStreamForEntry streamForEntry, Completed complete, ProgressCallback callback = null, object userItem = null)
        {
            if (tarStream == null)
            {
                throw new ObjectDisposedException(nameof(TarReader));
            }

            TarEntry tarEntry;
            while ((tarEntry = tarStream.GetNextEntry()) != null)
            {
                if (tarEntry.IsDirectory)
                {
                    continue;
                }

                var targetStream = streamForEntry(tarEntry);
                tarStream.CopyEntryContents(targetStream, callback, userItem);
                complete?.Invoke(tarEntry, targetStream);
                return true;
            }

            return false;
        }

        /// <summary>Reads all entries using callback functions to acquire and close per entry target streams.</summary>
        /// <param name="streamForEntry">Callback for aquiring a target stream for the specified entry.</param>
        /// <param name="complete">Callback after stream was written and may be closed.</param>
        /// <param name="callback">The callback used during stream copy.</param>
        /// <param name="userItem">A user item for the callback.</param>
        /// <returns>Returns true if the operation completed, false if the callback used <see cref="ProgressEventArgs.Break"/>.</returns>
        public bool ReadAll(GetStreamForEntry streamForEntry, Completed complete, ProgressCallback callback = null, object userItem = null)
        {
            if (tarStream == null)
            {
                throw new ObjectDisposedException(nameof(TarReader));
            }

            var result = true;
            void CallbackOverride(object s, ProgressEventArgs e)
            {
                callback?.Invoke(s, e);
                if (e.Break)
                {
                    result = false;
                }
            }

            while (ReadNext(streamForEntry, complete, CallbackOverride, userItem))
            {
            }

            return result;
        }

        /// <summary>Unpacks all entries to the specified path.</summary>
        /// <param name="path">The target path to write to.</param>
        /// <param name="callback">The callback used during stream copy.</param>
        /// <param name="userItem">A user item for the callback.</param>
        /// <returns>Returns true if the operation completed, false if the callback used <see cref="ProgressEventArgs.Break"/>.</returns>
        public bool UnpackTo(string path, ProgressCallback callback = null, object userItem = null)
        {
            if (tarStream == null)
            {
                throw new ObjectDisposedException(nameof(TarReader));
            }

            path = Path.GetFullPath(path);

            Stream StreamForEntry(TarEntry t)
            {
                var name = t.Name;
                if (Path.IsPathRooted(name))
                {
                    name = name[Path.GetPathRoot(name).Length..];
                }

                var fullpath = Path.GetFullPath(Path.Combine(path, name));
                var dir = Path.GetDirectoryName(fullpath);
                if (!dir.StartsWith(path))
                {
                    throw new InvalidOperationException("Invalid relative path!");
                }

                Directory.CreateDirectory(dir);
                return File.Create(fullpath);
            }

            void Complete(TarEntry t, Stream s)
            {
#if NETSTANDARD13
                s.Flush();
                s.Dispose();
#else
                s.Close();
#endif
            }

            return ReadAll(StreamForEntry, Complete, callback, userItem);
        }

        /// <summary>Closes the writer and the underlying stream.</summary>
        public void Close()
        {
            if (tarStream != null)
            {
#if NETSTANDARD13
                tarStream.Dispose();
#else
                tarStream.Close();
#endif
                Dispose();
            }
        }

        #region IDisposable Support

        /// <summary>Releases the unmanaged resources used and optionally releases the managed resources.</summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
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

        /// <summary>Disposes this instance.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion IDisposable Support
    }
}
