using System;
using System.IO;
using Cave.Compression.Streams;

namespace Cave.Compression
{
    /// <summary>
    /// Provides deflate de-/compression.
    /// </summary>
    public static class Deflate
    {
        /// <summary>
        /// Compresses the data at the current read position in the source stream to the specified targetstream
        /// </summary>
        /// <param name="sourceStream">The stream containing the data to be compressed</param>
        /// <param name="targetStream">The stream that will receive the compressed data</param>
        /// <param name="count">The number of bytes to compress</param>
        /// <param name="closeStream">Close the source stream after compression</param>
        public static void Compress(Stream sourceStream, Stream targetStream, long count, bool closeStream)
        {
            if (sourceStream == null)
            {
                throw new ArgumentNullException("sourceStream");
            }

            if (targetStream == null)
            {
                throw new ArgumentNullException("targetStream");
            }

            DeflaterOutputStream compressStream = new DeflaterOutputStream(targetStream)
            {
                IsStreamOwner = false
            };
            if (count > 0)
            {
                sourceStream.CopyBlocksTo(compressStream, count);
            }
            else
            {
                sourceStream.CopyBlocksTo(compressStream);
            }

            compressStream.Finish();
            if (closeStream)
            {
#if !NETSTANDARD13
                compressStream.Close();
                sourceStream.Close();
#endif
            }
        }

        /// <summary>
        /// Decompresses the data at the current read position in the source stream to the specified targetstream
        /// </summary>
        /// <param name="sourceStream">The stream containing the data to be compressed</param>
        /// <param name="targetStream">The stream that will receive the decompressed data</param>
        /// <param name="count">The number of bytes to decompress</param>
        /// <param name="closeStream">Close the source stream after decompression</param>
        public static void Decompress(Stream sourceStream, Stream targetStream, long count, bool closeStream)
        {
            if (sourceStream == null)
            {
                throw new ArgumentNullException("sourceStream");
            }

            if (targetStream == null)
            {
                throw new ArgumentNullException("targetStream");
            }

            InflaterInputStream decompressStream = new InflaterInputStream(sourceStream)
            {
                IsStreamOwner = false
            };
            if (count > 0)
            {
                decompressStream.CopyBlocksTo(targetStream, count);
            }
            else
            {
                decompressStream.CopyBlocksTo(targetStream);
            }

            if (closeStream)
            {
#if !NETSTANDARD13
                sourceStream.Close();
#endif
            }
        }

        /// <summary>
        /// Compresses a byte array into memory
        /// </summary>
        /// <param name="data">Array to compress</param>
        /// <returns>Returns a new compressed byte[] array</returns>
        public static byte[] Compress(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            using (MemoryStream source = new MemoryStream(data))
            {
                using (MemoryStream target = new MemoryStream())
                {
                    Compress(source, target, -1, true);
                    return target.ToArray();
                }
            }
        }

        /// <summary>
        /// Decompresses a byte array into memory
        /// </summary>
        /// <param name="data">data to decompress</param>
        /// <returns>Returns a new byte[] array with decompressed data</returns>
        public static byte[] Decompress(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException("data");
            }

            using (MemoryStream source = new MemoryStream(data))
            {
                using (MemoryStream target = new MemoryStream())
                {
                    Decompress(source, target, -1, true);
                    return target.ToArray();
                }
            }
        }

        /// <summary>
        /// Compresses a file on the disk (new file will have .gz extension)
        /// </summary>
        /// <param name="fileName">Filename of the file to compress</param>
        /// <param name="delete">Delete the file after successful operation</param>
        public static void Compress(string fileName, bool delete)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException("fileName");
            }

            using (Stream source = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (Stream target = File.Open(fileName + ".deflate", FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    Compress(source, target, source.Length, true);
                }
            }

            if (delete)
            {
                File.Delete(fileName);
            }
        }

        /// <summary>
        /// Decompresses a file on the disk (new file will have no longer a .deflate extension)
        /// </summary>
        /// <param name="fileName">Filename of the file to decompress</param>
        /// <param name="delete">Delete the file after successful operation</param>
        public static void Decompress(string fileName, bool delete)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException("fileName");
            }

            if (Path.GetExtension(fileName) != ".deflate")
            {
                throw new ArgumentException(string.Format(".deflate extension expected!"), "fileName");
            }

            using (Stream source = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (Stream target = File.Open(Path.GetFileNameWithoutExtension(fileName), FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    Decompress(source, target, source.Length, true);
                }
            }

            if (delete)
            {
                File.Delete(fileName);
            }
        }
    }
}
