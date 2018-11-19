#region CopyRight 2018
/*
    Copyright (c) 2003-2018 Andreas Rohleder (andreas@rohleder.cc)
    All rights reserved
*/
#endregion
#region License LGPL-3
/*
    This program/library/sourcecode is free software; you can redistribute it
    and/or modify it under the terms of the GNU Lesser General Public License
    version 3 as published by the Free Software Foundation subsequent called
    the License.

    You may not use this program/library/sourcecode except in compliance
    with the License. The License is included in the LICENSE file
    found at the installation directory or the distribution package.

    Permission is hereby granted, free of charge, to any person obtaining
    a copy of this software and associated documentation files (the
    "Software"), to deal in the Software without restriction, including
    without limitation the rights to use, copy, modify, merge, publish,
    distribute, sublicense, and/or sell copies of the Software, and to
    permit persons to whom the Software is furnished to do so, subject to
    the following conditions:

    The above copyright notice and this permission notice shall be included
    in all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
    NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
    LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
    OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
    WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
#endregion
#region Authors & Contributors
/*
   Author:
     Andreas Rohleder <andreas@rohleder.cc>

   Contributors:

 */
#endregion

using System;
using System.IO;
using System.IO.Compression;
using Cave.Compression.Streams;
using Cave.IO;

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

            DeflaterOutputStream compressStream = new DeflaterOutputStream(targetStream);
            compressStream.IsStreamOwner = false;
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

            InflaterInputStream decompressStream = new InflaterInputStream(sourceStream);
            decompressStream.IsStreamOwner = false;
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
