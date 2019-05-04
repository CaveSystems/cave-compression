using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Cave.Compression
{
    /// <summary>
    /// Provides a very simple ar file reader / writer. This calss does not support filenames longer than 15 characters.
    /// (Note: may be extended to support BSD style extended filenames).
    /// </summary>
    public class ArFile
    {
        #region static functionality

        /// <summary>
        /// Creates a new ar file at the specified stream.
        /// </summary>
        /// <param name="stream">The stream to be written.</param>
        /// <returns>A new <see cref="ArFile"/> instance.</returns>
        public static ArFile CreateNewAr(Stream stream)
        {
            return new ArFile(stream, stream, false);
        }

        /// <summary>
        /// Creates a new ar file.
        /// </summary>
        /// <param name="file">Can be an string containing a path.</param>
        /// <returns>A new <see cref="ArFile"/> instance.</returns>
        public static ArFile CreateNewAr(string file)
        {
            Stream stream = File.Open(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            return new ArFile(stream, stream, false);
        }

        /// <summary>
        /// Creates a new ar file with GZip compression.
        /// </summary>
        /// <param name="file">Can be an string containing a path.</param>
        /// <returns>A new <see cref="ArFile"/> instance.</returns>
        public static ArFile CreateNewArGZip(string file)
        {
            Stream bottomStream = File.Open(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            Stream topStream = new GZipStream(bottomStream, CompressionMode.Compress);
            return new ArFile(topStream, bottomStream, false);
        }

        /// <summary>
        /// Reads a ar from stream.
        /// </summary>
        /// <param name="stream">The stream to be read.</param>
        /// <returns>A new <see cref="ArFile"/> instance.</returns>
        public static ArFile ReadAr(Stream stream)
        {
            return new ArFile(stream, stream, false);
        }

        /// <summary>
        /// Reads a ar from stream with GZip compression.
        /// </summary>
        /// <param name="stream">The stream to be read.</param>
        /// <returns>A new <see cref="ArFile"/> instance.</returns>
        public static ArFile ReadArGZip(Stream stream)
        {
            Stream topStream = new GZipStream(stream, CompressionMode.Decompress);
            return new ArFile(topStream, stream, false);
        }

        /// <summary>
        /// Reads a ar file.
        /// </summary>
        /// <param name="file">Name of the file.</param>
        /// <returns>A new <see cref="ArFile"/> instance.</returns>
        public static ArFile ReadAr(string file)
        {
            Stream stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return new ArFile(stream, stream, false);
        }

        /// <summary>
        /// Reads a ar file with GZip compression.
        /// </summary>
        /// <param name="file">Name of the file.</param>
        /// <returns>A new <see cref="ArFile"/> instance.</returns>
        public static ArFile ReadArGZip(string file)
        {
            Stream bottomStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            Stream topStream = new GZipStream(bottomStream, CompressionMode.Decompress);
            return new ArFile(topStream, bottomStream, false);
        }
        #endregion

        #region constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ArFile"/> class.
        /// </summary>
        /// <param name="topStream">top stream that is read / written.</param>
        /// <param name="bottomStream">underlying compression / file stream.</param>
        /// <param name="openRead">If set to true the stream is used for reading and will be checked for a valid header,
        /// otherwise a header for a new file is written.</param>
        ArFile(Stream topStream, Stream bottomStream, bool openRead)
        {
            this.topStream = topStream;
            this.bottomStream = bottomStream;
            byte[] arHeader = ASCII.GetBytes("!<arch>\n");
            if (openRead)
            {
                byte[] header = new byte[8];
                if ((this.topStream.Read(header, 0, header.Length) != arHeader.Length) || (!Equals(header, arHeader)))
                {
                    throw new FormatException("ArFile header invalid!");
                }
            }
            else
            {
                this.topStream.Write(arHeader, 0, 8);
            }
        }
        #endregion

        #region private functionality

        Stream bottomStream;
        Stream topStream;

        enum Operation
        {
            None,
            ReadHeader,
            ReadData,
            WriteHeader,
            WriteData,
            Seek,
        }

        Operation lastOperation = Operation.None;

        void StartOperation(Operation operation)
        {
            switch (lastOperation)
            {
                case Operation.ReadData: lastOperation = Operation.None; break;
                case Operation.WriteData: lastOperation = Operation.None; break;
                case Operation.Seek: lastOperation = Operation.None; break;
            }

            bool valid = false;
            string requirements = string.Empty;
            string state = string.Empty;
            switch (operation)
            {
                case Operation.ReadHeader:
                    valid = topStream.CanRead && (lastOperation == Operation.None);
                    if (!valid)
                    {
                        requirements = "BaseStream.CanRead = true, Operation = None";
                        state = "BaseStream.CanRead = " + topStream.CanRead + ", Operation = " + lastOperation;
                    }

                    break;
                case Operation.ReadData:
                    valid = topStream.CanRead && (lastOperation == Operation.ReadHeader);
                    if (!valid)
                    {
                        requirements = "BaseStream.CanRead = true, Operation = ReadHeader";
                        state = "BaseStream.CanRead = " + topStream.CanRead + ", Operation = " + lastOperation;
                    }

                    break;
                case Operation.WriteHeader:
                    valid = topStream.CanWrite && (lastOperation == Operation.None);
                    if (!valid)
                    {
                        requirements = "BaseStream.CanWrite = true, Operation = None";
                        state = "BaseStream.CanWrite = " + topStream.CanWrite + ", Operation = " + lastOperation;
                    }

                    break;
                case Operation.WriteData:
                    valid = topStream.CanWrite && (lastOperation == Operation.WriteHeader);
                    if (!valid)
                    {
                        requirements = "BaseStream.CanWrite = true, Operation = WriteHeader";
                        state = "BaseStream.CanWrite = " + topStream.CanWrite + ", Operation = " + lastOperation;
                    }

                    break;
                case Operation.Seek:
                    valid = topStream.CanSeek;
                    if (!valid)
                    {
                        requirements = "BaseStream.CanSeek = true";
                        state = "BaseStream.CanSeek = " + topStream.CanSeek;
                    }

                    break;
                default:
                    throw new NotImplementedException("The requested operation is not implemented!");
            }

            if (!valid)
            {
                throw new NotSupportedException(
                    string.Format("The operation '{0}' is currently not allowed!", operation) + Environment.NewLine +
                    string.Format("Requirements:  {0}", requirements) + Environment.NewLine +
                    string.Format("Current state: {0}", state));
            }

            lastOperation = operation;
        }

        #endregion

        #region public functionality

        /// <summary>
        /// reads a header from the ar file throws an EndOfStreamException if the last entry was read already.
        /// </summary>
        /// <returns>the header of the next file.</returns>
        public ArHeader ReadHeader()
        {
            StartOperation(Operation.ReadHeader);
            return ArHeader.FromStream(topStream);
        }

        /// <summary>
        /// Reads a file from the ar file.
        /// </summary>
        /// <param name="fileData">Contents of the file.</param>
        /// <returns>the header of the file read on success and null when no more files exist in the ar file.</returns>
        public ArHeader ReadFile(out byte[] fileData)
        {
            ArHeader header;
            try
            {
                header = ReadHeader();
            }
            catch (EndOfStreamException)
            {
                fileData = null;
                return null;
            }

            fileData = ReadData(header.FileSize);
            return header;
        }

        /// <summary>
        /// Reads a file from the ar file and saves it at the specified output directory.
        /// </summary>
        /// <param name="outputDirectory">The output directory to copy the file to.</param>
        /// <returns>fileName and path on success and null when no more files exist in the ar file.</returns>
        public string ReadFile(string outputDirectory)
        {
            ArHeader header;
            try
            {
                header = ReadHeader();
            }
            catch (EndOfStreamException)
            {
                return null;
            }

            string fileName = header.FileName;
            string fullPath = Path.Combine(outputDirectory, fileName);
            ReadDataTo(fullPath, header.FileSize);
            return fullPath;
        }

        /// <summary>
        /// reads data from the ar file.
        /// </summary>
        /// <param name="size">Size in bytes.</param>
        /// <returns>Returns a new byte array.</returns>
        public byte[] ReadData(int size)
        {
            StartOperation(Operation.ReadData);
            byte[] result = new byte[size];
            int bytesLeft = size;
            int pos = 0;
            while (bytesLeft > 0)
            {
                int block = 1024 * 1024;
                if (block > bytesLeft)
                {
                    block = bytesLeft;
                }

                block = topStream.Read(result, pos, block);
                pos += block;
                bytesLeft -= block;
            }

            int paddingBytes = size % 2;
            while (paddingBytes > 0)
            {
                paddingBytes -= topStream.Read(new byte[paddingBytes], 0, paddingBytes);
                if (paddingBytes == 0)
                {
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Reads data from the ar file to a specific specified FileName.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="size">Size of the file in bytes.</param>
        public void ReadDataTo(string fileName, int size)
        {
            StartOperation(Operation.ReadData);
            using (FileStream stream = File.OpenWrite(fileName))
            {
                byte[] buffer = new byte[1024 * 1024];
                int bytesLeft = size;
                while (bytesLeft > 0)
                {
                    int block = 1024 * 1024;
                    if (block > bytesLeft)
                    {
                        block = bytesLeft;
                    }

                    block = topStream.Read(buffer, 0, block);
                    stream.Write(buffer, 0, block);
                    bytesLeft -= block;
                }

                int paddingBytes = size % 2;
                while (paddingBytes > 0)
                {
                    paddingBytes -= topStream.Read(new byte[paddingBytes], 0, paddingBytes);
                    if (paddingBytes == 0)
                    {
                        break;
                    }
                }

                stream.Flush();
            }
        }

        /// <summary>
        /// skips reading of data from the ar file.
        /// </summary>
        /// <param name="size">Number of bytes to skip.</param>
        public void SkipData(int size)
        {
            StartOperation(Operation.ReadData);
            if (topStream.CanSeek)
            {
                topStream.Seek(size + (size % 2), SeekOrigin.Current);
            }
            else
            {
                byte[] buffer = new byte[1024 * 1024];
                int bytesLeft = size + (size % 2);
                while (bytesLeft > 0)
                {
                    int block = 1024 * 1024;
                    if (block > bytesLeft)
                    {
                        block = bytesLeft;
                    }

                    bytesLeft -= topStream.Read(buffer, 0, block);
                }
            }
        }

        /// <summary>
        /// writes a file header to the ar file.
        /// </summary>
        /// <param name="header">Header to write.</param>
        public void WriteHeader(ArHeader header)
        {
            if (header == null)
            {
                throw new ArgumentNullException("header");
            }

            StartOperation(Operation.WriteHeader);
            topStream.Write(header.Data, 0, 60);
        }

        /// <summary>
        /// writes a file to the ar file.
        /// </summary>
        /// <param name="file">Name of the file.</param>
        public void WriteFile(string file)
        {
            if (file == null)
            {
                throw new ArgumentNullException("file");
            }

            WriteHeader(new ArHeader(file));
            WriteDataFrom(file);
        }

        /// <summary>
        /// writes data from a specified file to the ar file.
        /// </summary>
        /// <param name="file">Name of the file.</param>
        public void WriteDataFrom(string file)
        {
            if (file == null)
            {
                throw new ArgumentNullException("file");
            }

            StartOperation(Operation.WriteData);
            using (FileStream stream = File.OpenRead(file))
            {
                byte[] buffer = new byte[1024 * 1024];
                while (stream.Position < stream.Length)
                {
                    int size = stream.Read(buffer, 0, buffer.Length);
                    topStream.Write(buffer, 0, size);
                }

                if ((stream.Length % 2) != 0)
                {
                    // padding
                    topStream.WriteByte(0x0A);
                }

                stream.Flush();
            }
        }

        /// <summary>
        /// writes the specified data to the ar file.
        /// </summary>
        /// <param name="data">Byte array to write.</param>
        public void WriteData(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            StartOperation(Operation.WriteData);
            int bytesLeft = data.Length;
            int pos = 0;
            while (bytesLeft > 0)
            {
                int blockSize = 1024 * 1024;
                if (blockSize > bytesLeft)
                {
                    blockSize = bytesLeft;
                }

                topStream.Write(data, pos, blockSize);
                bytesLeft -= blockSize;
                pos += blockSize;
            }

            if ((data.Length % 2) != 0)
            {
                // padding
                topStream.WriteByte(0x0A);
            }
        }

        /// <summary>
        /// lists all entries in the ar file.
        /// </summary>
        /// <returns>Returns all headers.</returns>
        public ArHeader[] GetAllEntries()
        {
            List<ArHeader> headers = new List<ArHeader>();
            bool unexpectedError = false;
            try
            {
                while (true)
                {
                    ArHeader header = ReadHeader();
                    headers.Add(header);
                    unexpectedError = true;
                    SkipData(header.FileSize);
                    unexpectedError = false;
                }
            }
            catch (EndOfStreamException)
            {
                if (unexpectedError)
                {
                    throw;
                }
            }

            return headers.ToArray();
        }

        /// <summary>
        /// closes the ar file.
        /// </summary>
        public void Close()
        {
            topStream?.Flush();
#if NETSTANDARD13
            topStream?.Dispose();
            bottomStream?.Dispose();
#else
            topStream?.Close();
            bottomStream?.Close();
#endif
            topStream = null;
            bottomStream = null;
        }

        /// <summary>
        /// Gets a simple progress on read operations.
        /// </summary>
        public double Progress
        {
            get
            {
                return bottomStream.Position / (double)bottomStream.Length;
            }
        }
#endregion
    }
}
