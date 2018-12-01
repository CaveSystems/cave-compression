using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Cave.Compression
{
    /// <summary>
    /// Provides a very simple ar file reader / writer. This calss does not support filenames longer than 15 characters.
    /// (Note: may be extended to support BSD style extended filenames)
    /// </summary>
    public class ArFile
    {
        #region static functionality
        /// <summary>
        /// Creates a new ar file at the specified stream
        /// </summary>
        /// <param name="stream">The stream to be written</param>
        /// <returns>A new <see cref="ArFile"/> instance</returns>
        public static ArFile CreateNewAr(Stream stream)
        {
            return new ArFile(stream, stream, false);
        }

        /// <summary>
        /// Creates a new ar file
        /// </summary>
        /// <param name="file">Can be an string containing a path</param>
        /// <returns>A new <see cref="ArFile"/> instance</returns>
        public static ArFile CreateNewAr(string file)
        {
            Stream stream = File.Open(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            return new ArFile(stream, stream, false);
        }

        /// <summary>
        /// Creates a new ar file with GZip compression
        /// </summary>
        /// <param name="file">Can be an string containing a path</param>
        /// <returns>A new <see cref="ArFile"/> instance</returns>
        public static ArFile CreateNewArGZip(string file)
        {
            Stream bottomStream = File.Open(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            Stream topStream = new GZipStream(bottomStream, CompressionMode.Compress);
            return new ArFile(topStream, bottomStream, false);
        }

        /// <summary>
        /// Reads a ar from stream
        /// </summary>
        /// <param name="stream">The stream to be read</param>
        /// <returns>A new <see cref="ArFile"/> instance</returns>
        public static ArFile ReadAr(Stream stream)
        {
            return new ArFile(stream, stream, false);
        }

        /// <summary>
        /// Reads a ar from stream with GZip compression
        /// </summary>
        /// <param name="stream">The stream to be read</param>
        /// <returns>A new <see cref="ArFile"/> instance</returns>
        public static ArFile ReadArGZip(Stream stream)
        {
            Stream topStream = new GZipStream(stream, CompressionMode.Decompress);
            return new ArFile(topStream, stream, false);
        }

        /// <summary>
        /// Reads a ar file
        /// </summary>
        /// <param name="file">Name of the file</param>
        /// <returns>A new <see cref="ArFile"/> instance</returns>
        public static ArFile ReadAr(string file)
        {
            Stream stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return new ArFile(stream, stream, false);
        }

        /// <summary>
        /// Reads a ar file with GZip compression
        /// </summary>
        /// <param name="file">Name of the file</param>
        /// <returns>A new <see cref="ArFile"/> instance</returns>
        public static ArFile ReadArGZip(string file)
        {
            Stream bottomStream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            Stream topStream = new GZipStream(bottomStream, CompressionMode.Decompress);
            return new ArFile(topStream, bottomStream, false);
        }
        #endregion

        #region constructors
        /// <summary>
        /// creates a new ar file from a stream
        /// </summary>
        /// <param name="topStream">top stream that is read / written</param>
        /// <param name="bottomStream">underlying compression / file stream</param>
        /// <param name="openRead">If set to true the stream is used for reading and will be checked for a valid header,
        /// otherwise a header for a new file is written</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Globalization", "CA1303:Literale nicht als lokalisierte Parameter übergeben", MessageId = "Cave.Text.ASCII.GetBytes(System.String)")]
        private ArFile(Stream topStream, Stream bottomStream, bool openRead)
        {
            m_TopStream = topStream;
            m_BottomStream = bottomStream;
            byte[] ArHeader = ASCII.GetBytes("!<arch>\n");
            if (openRead)
            {
                byte[] header = new byte[8];
                if ((m_TopStream.Read(header, 0, header.Length) != ArHeader.Length) || (!Equals(header, ArHeader)))
                {
                    throw new FormatException("ArFile header invalid!");
                }
            }
            else
            {
                m_TopStream.Write(ArHeader, 0, 8);
            }
        }
        #endregion

        #region private functionality

        private Stream m_BottomStream;
        private Stream m_TopStream;

        private enum Operation
        {
            None,
            ReadHeader,
            ReadData,
            WriteHeader,
            WriteData,
            Seek,
        }

        private Operation m_LastOperation = Operation.None;

        private void m_StartOperation(Operation operation)
        {
            switch (m_LastOperation)
            {
                case Operation.ReadData: m_LastOperation = Operation.None; break;
                case Operation.WriteData: m_LastOperation = Operation.None; break;
                case Operation.Seek: m_LastOperation = Operation.None; break;
            }
            bool valid = false;
            string requirements = "";
            string state = "";
            switch (operation)
            {
                case Operation.ReadHeader:
                    valid = m_TopStream.CanRead && (m_LastOperation == Operation.None);
                    if (!valid)
                    {
                        requirements = "BaseStream.CanRead = true, Operation = None";
                        state = "BaseStream.CanRead = " + m_TopStream.CanRead + ", Operation = " + m_LastOperation;
                    }
                    break;
                case Operation.ReadData:
                    valid = m_TopStream.CanRead && (m_LastOperation == Operation.ReadHeader);
                    if (!valid)
                    {
                        requirements = "BaseStream.CanRead = true, Operation = ReadHeader";
                        state = "BaseStream.CanRead = " + m_TopStream.CanRead + ", Operation = " + m_LastOperation;
                    }
                    break;
                case Operation.WriteHeader:
                    valid = m_TopStream.CanWrite && (m_LastOperation == Operation.None);
                    if (!valid)
                    {
                        requirements = "BaseStream.CanWrite = true, Operation = None";
                        state = "BaseStream.CanWrite = " + m_TopStream.CanWrite + ", Operation = " + m_LastOperation;
                    }
                    break;
                case Operation.WriteData:
                    valid = m_TopStream.CanWrite && (m_LastOperation == Operation.WriteHeader);
                    if (!valid)
                    {
                        requirements = "BaseStream.CanWrite = true, Operation = WriteHeader";
                        state = "BaseStream.CanWrite = " + m_TopStream.CanWrite + ", Operation = " + m_LastOperation;
                    }
                    break;
                case Operation.Seek:
                    valid = m_TopStream.CanSeek;
                    if (!valid)
                    {
                        requirements = "BaseStream.CanSeek = true";
                        state = "BaseStream.CanSeek = " + m_TopStream.CanSeek;
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
            m_LastOperation = operation;
        }

        #endregion

        #region public functionality

        /// <summary>
        /// reads a header from the ar file throws an EndOfStreamException if the last entry was read already
        /// </summary>
        /// <returns>the header of the next file</returns>
        public ArHeader ReadHeader()
        {
            m_StartOperation(Operation.ReadHeader);
            return ArHeader.FromStream(m_TopStream);
        }

        /// <summary>
        /// reads a file from the ar file
        /// </summary>
        /// <param name="fileData"></param>
        /// <returns>the header of the file read on success and null when no more files exist in the ar file</returns>
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
        /// reads a file from the ar file and saves it at the specified output directory
        /// </summary>
        /// <param name="outputDirectory"></param>
        /// <returns>fileName and path on success and null when no more files exist in the ar file</returns>
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
        /// reads data from the ar file
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public byte[] ReadData(int size)
        {
            m_StartOperation(Operation.ReadData);
            byte[] result = new byte[size];
            int bytesLeft = size;
            int pos = 0;
            while (bytesLeft > 0)
            {
                int block = 1024 * 1024;
                if (block > bytesLeft) block = bytesLeft;
                block = m_TopStream.Read(result, pos, block);
                pos += block;
                bytesLeft -= block;
            }
            int paddingBytes = size % 2;
            while (paddingBytes > 0)
            {
                paddingBytes -= m_TopStream.Read(new byte[paddingBytes], 0, paddingBytes);
                if (paddingBytes == 0) break;
            }
            return result;
        }

        /// <summary>
        /// reads data from the ar file to a specific specified FileName
        /// </summary>
        /// <param name="fileName">Name of the file</param>
        /// <param name="size"></param>
        public void ReadDataTo(string fileName, int size)
        {
            m_StartOperation(Operation.ReadData);
            using (FileStream stream = File.OpenWrite(fileName))
            {
                byte[] buffer = new byte[1024 * 1024];
                int bytesLeft = size;
                while (bytesLeft > 0)
                {
                    int block = 1024 * 1024;
                    if (block > bytesLeft) block = bytesLeft;
                    block = m_TopStream.Read(buffer, 0, block);
                    stream.Write(buffer, 0, block);
                    bytesLeft -= block;
                }
                int paddingBytes = size % 2;
                while (paddingBytes > 0)
                {
                    paddingBytes -= m_TopStream.Read(new byte[paddingBytes], 0, paddingBytes);
                    if (paddingBytes == 0) break;
                }
                stream.Flush();
            }
        }

        /// <summary>
        /// skips reading of data from the ar file
        /// </summary>
        /// <param name="size"></param>
        public void SkipData(int size)
        {
            m_StartOperation(Operation.ReadData);
            if (m_TopStream.CanSeek)
            {
                m_TopStream.Seek(size + (size % 2), SeekOrigin.Current);
            }
            else
            {
                byte[] buffer = new byte[1024 * 1024];
                int bytesLeft = size + (size % 2);
                while (bytesLeft > 0)
                {
                    int block = 1024 * 1024;
                    if (block > bytesLeft) block = bytesLeft;
                    bytesLeft -= m_TopStream.Read(buffer, 0, block);
                }
            }
        }

        /// <summary>
        /// writes a file header to the ar file
        /// </summary>
        /// <param name="header"></param>
        public void WriteHeader(ArHeader header)
        {
            if (header == null) throw new ArgumentNullException("header");
            m_StartOperation(Operation.WriteHeader);
            m_TopStream.Write(header.Data, 0, 60);
        }

        /// <summary>
        /// writes a file to the ar file
        /// </summary>
        /// <param name="file">Name of the file</param>
        public void WriteFile(string file)
        {
            if (file == null) throw new ArgumentNullException("file");
            WriteHeader(new ArHeader(file));
            WriteDataFrom(file);
        }

        /// <summary>
        /// writes data from a specified file to the ar file
        /// </summary>
        /// <param name="file">Name of the file</param>
        public void WriteDataFrom(string file)
        {
            if (file == null) throw new ArgumentNullException("file");
            m_StartOperation(Operation.WriteData);
            using (FileStream stream = File.OpenRead(file))
            {
                byte[] buffer = new byte[1024 * 1024];
                while (stream.Position < stream.Length)
                {
                    int size = stream.Read(buffer, 0, buffer.Length);
                    m_TopStream.Write(buffer, 0, size);
                }
                if ((stream.Length % 2) != 0)
                {
                    //padding
                    m_TopStream.WriteByte(0x0A);
                }
                stream.Flush();
            }
        }

        /// <summary>
        /// writes the specified data to the ar file
        /// </summary>
        /// <param name="data"></param>
        public void WriteData(byte[] data)
        {
            if (data == null) throw new ArgumentNullException("data");
            m_StartOperation(Operation.WriteData);
            int bytesLeft = data.Length;
            int pos = 0;
            while (bytesLeft > 0)
            {
                int blockSize = 1024 * 1024;
                if (blockSize > bytesLeft) blockSize = bytesLeft;
                m_TopStream.Write(data, pos, blockSize);
                bytesLeft -= blockSize;
                pos += blockSize;
            }
            if ((data.Length % 2) != 0)
            {
                //padding
                m_TopStream.WriteByte(0x0A);
            }
        }

        /// <summary>
        /// lists all entries in the ar file
        /// </summary>
        /// <returns></returns>
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
                if (unexpectedError) throw;
            }
            return headers.ToArray();
        }

        /// <summary>
        /// closes the ar file
        /// </summary>
        public void Close()
        {
            m_TopStream?.Flush();
#if NETSTANDARD13
            m_TopStream?.Dispose();
            m_BottomStream?.Dispose();
#else
            m_TopStream?.Close();
            m_BottomStream?.Close();
#endif
            m_TopStream = null;
            m_BottomStream = null;
        }

        /// <summary>
        /// obtain a simple progress on read operations
        /// </summary>
        public double Progress
        {
            get
            {
                return m_BottomStream.Position / (double)m_BottomStream.Length;
            }
        }

#endregion
    }
}
