using System;
using System.IO;
using System.Text;

namespace Cave.Compression.Tar
{
    /// <summary>
    /// The TarArchive class implements the concept of a
    /// 'Tape Archive'. A tar archive is a series of entries, each of
    /// which represents a file system object. Each entry in
    /// the archive consists of a header block followed by 0 or more data blocks.
    /// Directory entries consist only of the header block, and are followed by entries
    /// for the directory's contents. File entries consist of a
    /// header followed by the number of blocks needed to
    /// contain the file's contents. All entries are written on
    /// block boundaries. Blocks are 512 bytes long.
    ///
    /// TarArchives are instantiated in either read or write mode,
    /// based upon whether they are instantiated with an InputStream
    /// or an OutputStream. Once instantiated TarArchives read/write
    /// mode can not be changed.
    ///
    /// There is currently no support for random access to tar archives.
    /// However, it seems that subclassing TarArchive, and using the
    /// TarBuffer.CurrentRecord and TarBuffer.CurrentBlock
    /// properties, this would be rather trivial.
    /// </summary>
    public class TarArchive : IDisposable
    {
        #region static class
        // TODO: TarArchive - Is there a better way to test for a text file?
        // It no longer reads entire files into memory but is still a weak test!
        // This assumes that byte values 0-7, 14-31 or 255 are binary
        // and that all non text files contain one of these values
        static bool IsBinary(string filename)
        {
            using var fs = File.OpenRead(filename);
            var sampleSize = Math.Min(4096, (int)fs.Length);
            var content = new byte[sampleSize];

            var bytesRead = fs.Read(content, 0, sampleSize);

            for (var i = 0; i < bytesRead; ++i)
            {
                var b = content[i];
                if ((b < 8) || ((b > 13) && (b < 32)) || (b == 255))
                {
                    return true;
                }
            }

            return false;
        }

        static void EnsureDirectoryExists(string directoryName)
        {
            if (!Directory.Exists(directoryName))
            {
                try
                {
                    Directory.CreateDirectory(directoryName);
                }
                catch (Exception e)
                {
                    throw new Exception("Exception creating directory '" + directoryName + "', " + e.Message, e);
                }
            }
        }

        /// <summary>
        /// The InputStream based constructors create a TarArchive for the
        /// purposes of extracting or listing a tar archive. Thus, use
        /// these constructors when you wish to extract files from or list
        /// the contents of an existing tar archive.
        /// </summary>
        /// <param name="inputStream">The stream to retrieve archive data from.</param>
        /// <returns>Returns a new <see cref="TarArchive"/> suitable for reading from.</returns>
        public static TarArchive CreateInputTarArchive(Stream inputStream)
        {
            if (inputStream == null)
            {
                throw new ArgumentNullException(nameof(inputStream));
            }

            TarArchive result;
            if (inputStream is TarInputStream tarStream)
            {
                result = new TarArchive(tarStream);
            }
            else
            {
                result = CreateInputTarArchive(inputStream, TarBuffer.DefaultBlockFactor);
            }

            return result;
        }

        /// <summary>
        /// Create TarArchive for reading setting block factor.
        /// </summary>
        /// <param name="inputStream">A stream containing the tar archive contents.</param>
        /// <param name="blockFactor">The blocking factor to apply.</param>
        /// <returns>Returns a <see cref="TarArchive"/> suitable for reading.</returns>
        public static TarArchive CreateInputTarArchive(Stream inputStream, int blockFactor)
        {
            if (inputStream == null)
            {
                throw new ArgumentNullException(nameof(inputStream));
            }

            if (inputStream is TarInputStream)
            {
                throw new ArgumentException("TarInputStream not valid");
            }

            return new TarArchive(new TarInputStream(inputStream, blockFactor));
        }

        /// <summary>
        /// Create a TarArchive for writing to, using the default blocking factor.
        /// </summary>
        /// <param name="outputStream">The <see cref="Stream"/> to write to.</param>
        /// <returns>Returns a <see cref="TarArchive"/> suitable for writing.</returns>
        public static TarArchive CreateOutputTarArchive(Stream outputStream)
        {
            if (outputStream == null)
            {
                throw new ArgumentNullException(nameof(outputStream));
            }

            TarArchive result;
            if (outputStream is TarOutputStream tarStream)
            {
                result = new TarArchive(tarStream);
            }
            else
            {
                result = CreateOutputTarArchive(outputStream, TarBuffer.DefaultBlockFactor);
            }

            return result;
        }

        /// <summary>
        /// Create a <see cref="TarArchive">tar archive</see> for writing.
        /// </summary>
        /// <param name="outputStream">The stream to write to.</param>
        /// <param name="blockFactor">The blocking factor to use for buffering.</param>
        /// <returns>Returns a <see cref="TarArchive"/> suitable for writing.</returns>
        public static TarArchive CreateOutputTarArchive(Stream outputStream, int blockFactor)
        {
            if (outputStream == null)
            {
                throw new ArgumentNullException(nameof(outputStream));
            }

            if (outputStream is TarOutputStream)
            {
                throw new ArgumentException("TarOutputStream is not valid");
            }

            return new TarArchive(new TarOutputStream(outputStream, blockFactor));
        }
        #endregion

        #region Instance Fields
        bool keepOldFiles;
        bool asciiTranslate;

        int userId;
        string userName = string.Empty;
        int groupId;
        string groupName = string.Empty;

        string rootPath;
        string pathPrefix;

        bool applyUserInfoOverrides;

        TarInputStream tarIn;
        TarOutputStream tarOut;
        bool isDisposed;
        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="TarArchive"/> class.
        /// </summary>
        protected TarArchive()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TarArchive"/> class.
        /// </summary>
        /// <param name="stream">The <see cref="TarInputStream"/> to use for input.</param>
        protected TarArchive(TarInputStream stream)
        {
            tarIn = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="TarArchive"/> class.
        /// </summary>
        /// <param name="stream">The <see cref="TarOutputStream"/> to use for output.</param>
        protected TarArchive(TarOutputStream stream)
        {
            tarOut = stream ?? throw new ArgumentNullException(nameof(stream));
        }
        #endregion

        /// <summary>
        /// Called for each TarEntry to be processed.
        /// </summary>
        public event EventHandler<TarEntryEventArgs> ProcessEntry;

        /// <summary>
        /// Calls the <see cref="ProcessEntry"/> event.
        /// </summary>
        /// <param name="e">The event arguments.</param>
        protected virtual void OnProcessEntry(TarEntryEventArgs e) => ProcessEntry?.Invoke(this, e);

        /// <summary>
        /// Set the flag that determines whether existing files are
        /// kept, or overwritten during extraction.
        /// </summary>
        /// <param name="keepExistingFiles">
        /// If true, do not overwrite existing files.
        /// </param>
        public void SetKeepOldFiles(bool keepExistingFiles)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException("TarArchive");
            }

            keepOldFiles = keepExistingFiles;
        }

        /// <summary>
        /// Gets or sets a value indicating whether ascii translation shall be used.
        /// If ascii file translation
        /// is true, then the file is checked to see if it a binary file or not.
        /// If the flag is true and the test indicates it is ascii text
        /// file, it will be translated. The translation converts the local
        /// operating system's concept of line ends into the UNIX line end,
        /// '\n', which is the defacto standard for a TAR archive. This makes
        /// text files compatible with UNIX.
        /// </summary>
        public bool AsciiTranslate
        {
            get
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                return asciiTranslate;
            }
            set
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                asciiTranslate = value;
            }
        }

        /// <summary>
        /// Gets or sets pathPrefix to be added to entry names as they are written.
        /// A slash character is appended after PathPrefix.
        /// </summary>
        public string PathPrefix
        {
            get
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                return pathPrefix;
            }
            set
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                pathPrefix = value;
            }
        }

        /// <summary>
        /// Gets or sets rootPath to be removed from entry names if it is found at the beginning of the name.
        /// </summary>
        public string RootPath
        {
            get
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                return rootPath;
            }

            set
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                // Convert to forward slashes for matching. Trim trailing / for correct final path
                rootPath = value.Replace('\\', '/').TrimEnd('/');
            }
        }

        /// <summary>
        /// Set user and group information that will be used to fill in the
        /// tar archive's entry headers. This information is based on that available
        /// for the linux operating system, which is not always available on other
        /// operating systems.  TarArchive allows the programmer to specify values
        /// to be used in their place.
        /// <see cref="ApplyUserInfoOverrides"/> is set to true by this call.
        /// </summary>
        /// <param name="userId">
        /// The user id to use in the headers.
        /// </param>
        /// <param name="userName">
        /// The user name to use in the headers.
        /// </param>
        /// <param name="groupId">
        /// The group id to use in the headers.
        /// </param>
        /// <param name="groupName">
        /// The group name to use in the headers.
        /// </param>
        public void SetUserInfo(int userId, string userName, int groupId, string groupName)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException("TarArchive");
            }

            this.userId = userId;
            this.userName = userName;
            this.groupId = groupId;
            this.groupName = groupName;
            applyUserInfoOverrides = true;
        }

        /// <summary>
        /// Gets or sets a value indicating whether overrides defined by <see cref="SetUserInfo"/> should be applied.
        /// </summary>
        /// <remarks>If overrides are not applied then the values as set in each header will be used.</remarks>
        public bool ApplyUserInfoOverrides
        {
            get
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                return applyUserInfoOverrides;
            }
            set
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                applyUserInfoOverrides = value;
            }
        }

        /// <summary>
        /// Gets the archive user id.
        /// See <see cref="ApplyUserInfoOverrides">ApplyUserInfoOverrides</see> for detail
        /// on how to allow setting values on a per entry basis.
        /// </summary>
        /// <returns>
        /// The current user id.
        /// </returns>
        public int UserId
        {
            get
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                return userId;
            }
        }

        /// <summary>
        /// Gets the archive user name.
        /// See <see cref="ApplyUserInfoOverrides"/> for detail
        /// on how to allow setting values on a per entry basis.
        /// </summary>
        /// <returns>
        /// The current user name.
        /// </returns>
        public string UserName
        {
            get
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                return userName;
            }
        }

        /// <summary>
        /// Gets the archive group id.
        /// See <see cref="ApplyUserInfoOverrides">ApplyUserInfoOverrides</see> for detail
        /// on how to allow setting values on a per entry basis.
        /// </summary>
        /// <returns>
        /// The current group id.
        /// </returns>
        public int GroupId
        {
            get
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                return groupId;
            }
        }

        /// <summary>
        /// Gets the archive group name.
        /// See <see cref="ApplyUserInfoOverrides">ApplyUserInfoOverrides</see> for detail
        /// on how to allow setting values on a per entry basis.
        /// </summary>
        /// <returns>
        /// The current group name.
        /// </returns>
        public string GroupName
        {
            get
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                return groupName;
            }
        }

        /// <summary>
        /// Gets the archive's record size. Tar archives are composed of
        /// a series of RECORDS each containing a number of BLOCKS.
        /// This allowed tar archives to match the IO characteristics of
        /// the physical device being used. Archives are expected
        /// to be properly "blocked".
        /// </summary>
        /// <returns>
        /// The record size this archive is using.
        /// </returns>
        public int RecordSize
        {
            get
            {
                if (isDisposed)
                {
                    throw new ObjectDisposedException("TarArchive");
                }

                if (tarIn != null)
                {
                    return tarIn.RecordSize;
                }
                else if (tarOut != null)
                {
                    return tarOut.RecordSize;
                }

                return TarBuffer.DefaultRecordSize;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instances closes the underlying stream.
        /// </summary>
        public bool IsStreamOwner
        {
            get => tarIn?.IsStreamOwner ?? tarOut.IsStreamOwner;
            set
            {
                if (tarIn != null)
                {
                    tarIn.IsStreamOwner = value;
                }
                else
                {
                    tarOut.IsStreamOwner = value;
                }
            }
        }

        /// <summary>
        /// Perform the "list" command for the archive contents.
        ///
        /// NOTE That this method uses the <see cref="ProcessEntry"/> event to actually list
        /// the contents. If the progress display event is not set, nothing will be listed!.
        /// </summary>
        public void ListContents()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException("TarArchive");
            }

            while (true)
            {
                var entry = tarIn.GetNextEntry();

                if (entry == null)
                {
                    break;
                }

                var e = new TarEntryEventArgs(entry);
                OnProcessEntry(e);
                if (e.Break)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Perform the "extract" command and extract the contents of the archive.
        /// </summary>
        /// <param name="destinationDirectory">
        /// The destination directory into which to extract.
        /// </param>
        public void ExtractContents(string destinationDirectory)
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException("TarArchive");
            }

            while (true)
            {
                var entry = tarIn.GetNextEntry();

                if (entry == null)
                {
                    break;
                }

                if (entry.TarHeader.TypeFlag == TarEntryType.Link || entry.TarHeader.TypeFlag == TarEntryType.Symlink)
                {
                    continue;
                }

                if (!ExtractEntry(destinationDirectory, entry))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Extract an entry from the archive. This method assumes that the
        /// tarIn stream has been properly set with a call to GetNextEntry().
        /// </summary>
        /// <param name="destDir">
        /// The destination directory into which to extract.
        /// </param>
        /// <param name="entry">
        /// The TarEntry returned by tarIn.GetNextEntry().
        /// </param>
        /// <returns>Returns whether operation may continue or not.</returns>
        bool ExtractEntry(string destDir, TarEntry entry)
        {
            {
                var e = new TarEntryEventArgs(entry);
                OnProcessEntry(e);
                if (e.Break)
                {
                    return false;
                }
            }

            var name = entry.Name;

            if (Path.IsPathRooted(name))
            {
                // NOTE:
                // for UNC names...  \\machine\share\zoom\beet.txt gives \zoom\beet.txt
                name = name[Path.GetPathRoot(name).Length..];
            }

            var destFile = Path.Combine(destDir, name);

            if (entry.IsDirectory)
            {
                EnsureDirectoryExists(destFile);
            }
            else
            {
                var parentDirectory = Path.GetDirectoryName(destFile);
                EnsureDirectoryExists(parentDirectory);

                var e = new TarEntryEventArgs(entry);
                OnProcessEntry(e);
                if (e.Break)
                {
                    return false;
                }

                var process = !e.Skip;
                if (process)
                {
                    var fileInfo = new FileInfo(destFile);
                    if (fileInfo.Exists)
                    {
                        if (keepOldFiles)
                        {
                            throw new InvalidOperationException("Destination file already exists");
                        }
                        else if ((fileInfo.Attributes & FileAttributes.ReadOnly) != 0)
                        {
                            throw new InvalidOperationException("Destination file already exists, and is read-only");
                        }
                    }

                    var asciiTrans = false;

                    Stream outputStream = File.Create(destFile);
                    if (asciiTranslate)
                    {
                        asciiTrans = !IsBinary(destFile);
                    }

                    StreamWriter outw = null;
                    if (asciiTrans)
                    {
                        outw = new StreamWriter(outputStream);
                    }

                    var rdbuf = new byte[32 * 1024];

                    while (true)
                    {
                        var numRead = tarIn.Read(rdbuf, 0, rdbuf.Length);

                        if (numRead <= 0)
                        {
                            break;
                        }

                        if (asciiTrans)
                        {
                            for (int off = 0, b = 0; b < numRead; ++b)
                            {
                                if (rdbuf[b] == 10)
                                {
                                    var s = Encoding.ASCII.GetString(rdbuf, off, b - off);
                                    outw.WriteLine(s);
                                    off = b + 1;
                                }
                            }
                        }
                        else
                        {
                            outputStream.Write(rdbuf, 0, numRead);
                        }
                    }

                    if (asciiTrans)
                    {
                        outw.Dispose();
                    }
                    else
                    {
                        outputStream.Dispose();
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Write an entry to the archive. This method will call the putNextEntry
        /// and then write the contents of the entry, and finally call closeEntry()
        /// for entries that are files. For directories, it will call putNextEntry(),
        /// and then, if the recurse flag is true, process each entry that is a
        /// child of the directory.
        /// </summary>
        /// <param name="sourceEntry">
        /// The TarEntry representing the entry to write to the archive.
        /// </param>
        /// <param name="recurse">
        /// If true, process the children of directory entries.
        /// </param>
        /// <returns>Returns whether the operation may continue or not.</returns>
        public bool WriteEntry(TarEntry sourceEntry, bool recurse)
        {
            if (sourceEntry == null)
            {
                throw new ArgumentNullException(nameof(sourceEntry));
            }

            if (isDisposed)
            {
                throw new ObjectDisposedException("TarArchive");
            }

            try
            {
                if (recurse)
                {
                    TarHeader.SetValueDefaults(sourceEntry.UserId, sourceEntry.UserName, sourceEntry.GroupId, sourceEntry.GroupName);
                }

                return WriteEntryCore(sourceEntry, recurse);
            }
            finally
            {
                if (recurse)
                {
                    TarHeader.RestoreSetValues();
                }
            }
        }

        /// <summary>
        /// Write an entry to the archive. This method will call the putNextEntry
        /// and then write the contents of the entry, and finally call closeEntry()
        /// for entries that are files. For directories, it will call putNextEntry(),
        /// and then, if the recurse flag is true, process each entry that is a
        /// child of the directory.
        /// </summary>
        /// <param name="sourceEntry">
        /// The TarEntry representing the entry to write to the archive.
        /// </param>
        /// <param name="recurse">
        /// If true, process the children of directory entries.
        /// </param>
        /// <returns>Returns whether the operation may continue.</returns>
        bool WriteEntryCore(TarEntry sourceEntry, bool recurse)
        {
            string tempFileName = null;
            var entryFilename = sourceEntry.FileName;

            var entry = (TarEntry)sourceEntry.Clone();

            if (applyUserInfoOverrides)
            {
                entry.GroupId = groupId;
                entry.GroupName = groupName;
                entry.UserId = userId;
                entry.UserName = userName;
            }

            var e = new TarEntryEventArgs(entry);
            OnProcessEntry(e);
            if (e.Break)
            {
                return false;
            }

            if (e.Skip)
            {
                return true;
            }

            if (asciiTranslate && !entry.IsDirectory)
            {
                if (!IsBinary(entryFilename))
                {
                    tempFileName = Path.GetTempFileName();

                    using (var inStream = new StreamReader(File.Open(entryFilename, FileMode.Open, FileAccess.Read, FileShare.Read)))
                    {
                        using Stream outStream = File.Create(tempFileName);
                        while (true)
                        {
                            var line = inStream.ReadLine();
                            if (line == null)
                            {
                                break;
                            }

                            var data = Encoding.ASCII.GetBytes(line);
                            outStream.Write(data, 0, data.Length);
                            outStream.WriteByte((byte)'\n');
                        }

                        outStream.Flush();
                    }

                    entry.Size = new FileInfo(tempFileName).Length;
                    entryFilename = tempFileName;
                }
            }

            string newName = null;

            if (rootPath != null)
            {
                if (entry.Name.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    newName = entry.Name[(rootPath.Length + 1)..];
                }
            }

            if (pathPrefix != null)
            {
                newName = (newName == null) ? pathPrefix + "/" + entry.Name : pathPrefix + "/" + newName;
            }

            if (newName != null)
            {
                entry.Name = newName;
            }

            tarOut.PutNextEntry(entry);

            if (entry.IsDirectory)
            {
                if (recurse)
                {
                    var list = entry.GetDirectoryEntries();
                    for (var i = 0; i < list.Length; ++i)
                    {
                        if (!WriteEntryCore(list[i], recurse))
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                using (Stream inputStream = File.OpenRead(entryFilename))
                {
                    var localBuffer = new byte[32 * 1024];
                    while (true)
                    {
                        var numRead = inputStream.Read(localBuffer, 0, localBuffer.Length);

                        if (numRead <= 0)
                        {
                            break;
                        }

                        tarOut.Write(localBuffer, 0, numRead);
                    }
                }

                if (!string.IsNullOrEmpty(tempFileName))
                {
                    File.Delete(tempFileName);
                }

                tarOut.CloseEntry();
            }

            return true;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the FileStream and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources;
        /// false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                isDisposed = true;
                if (disposing)
                {
                    if (tarOut != null)
                    {
                        tarOut.Flush();
                        tarOut.Dispose();
                    }

                    if (tarIn != null)
                    {
                        tarIn.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Closes the archive and releases any associated resources.
        /// </summary>
        public virtual void Close()
        {
            Dispose(true);
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="TarArchive"/> class.
        /// Ensures that resources are freed and other cleanup operations are performed
        /// when the garbage collector reclaims the <see cref="TarArchive"/>.
        /// </summary>
        ~TarArchive()
        {
            Dispose(false);
        }
    }
}
