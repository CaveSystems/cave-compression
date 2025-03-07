﻿using Cave.Compression;
using Cave.Compression.Checksum;
using NUnit.Framework;
using System;

#nullable disable

namespace Cave.Compression.Tests.Checksum;

[TestFixture]
[Category("Checksum")]
public class ChecksumTests
{
    #region Private Fields

    readonly
                // Represents ASCII string of "123456789"
                byte[] check = { 49, 50, 51, 52, 53, 54, 55, 56, 57 };

    #endregion Private Fields

    #region Private Methods

    void exceptionTesting(IZipChecksum crcUnderTest)
    {
        var exception = false;

        try
        {
            crcUnderTest.Update(null);
        }
        catch (ArgumentNullException)
        {
            exception = true;
        }
        Assert.IsTrue(exception, "Passing a null buffer should cause an ArgumentNullException");

        // reset exception
        exception = false;
        try
        {
            crcUnderTest.Update(new ArraySegment<byte>(null, 0, 0).Array);
        }
        catch (ArgumentNullException)
        {
            exception = true;
        }
        Assert.IsTrue(exception, "Passing a null buffer should cause an ArgumentNullException");

        // reset exception
        exception = false;
        try
        {
            crcUnderTest.Update(new ArraySegment<byte>(check, -1, 9).Array);
        }
        catch (ArgumentOutOfRangeException)
        {
            exception = true;
        }
        Assert.IsTrue(exception, "Passing a negative offset should cause an ArgumentOutOfRangeException");

        // reset exception
        exception = false;
        try
        {
            crcUnderTest.Update(new ArraySegment<byte>(check, 10, 0).Array);
        }
        catch (ArgumentException)
        {
            exception = true;
        }
        Assert.IsTrue(exception, "Passing an offset greater than buffer.Length should cause an ArgumentException");

        // reset exception
        exception = false;
        try
        {
            crcUnderTest.Update(new ArraySegment<byte>(check, 0, -1).Array);
        }
        catch (ArgumentOutOfRangeException)
        {
            exception = true;
        }
        Assert.IsTrue(exception, "Passing a negative count should cause an ArgumentOutOfRangeException");

        // reset exception
        exception = false;
        try
        {
            crcUnderTest.Update(new ArraySegment<byte>(check, 0, 10).Array);
        }
        catch (ArgumentException)
        {
            exception = true;
        }
        Assert.IsTrue(exception, "Passing a count + offset greater than buffer.Length should cause an ArgumentException");
    }

    #endregion Private Methods

    #region Public Methods

    [Test]
    public void Adler32()
    {
        var underTestAdler32 = new Adler32();
        Assert.AreEqual(0x00000001, underTestAdler32.Value);

        underTestAdler32.Update(check);
        Assert.AreEqual(0x091E01DE, underTestAdler32.Value);

        underTestAdler32.Reset();
        Assert.AreEqual(0x00000001, underTestAdler32.Value);

        exceptionTesting(underTestAdler32);
    }

    [Test]
    public void BZip2Crc()
    {
        var underTestBZip2Crc = new BZip2Crc();
        Assert.AreEqual(0x0, underTestBZip2Crc.Value);

        underTestBZip2Crc.Update(check);
        Assert.AreEqual(0xFC891918, underTestBZip2Crc.Value);

        underTestBZip2Crc.Reset();
        Assert.AreEqual(0x0, underTestBZip2Crc.Value);

        exceptionTesting(underTestBZip2Crc);
    }

    [Test]
    public void ZipCrc32()
    {
        var underTestCrc32 = new ZipCrc32();
        Assert.AreEqual(0x0, underTestCrc32.Value);

        underTestCrc32.Update(check);
        Assert.AreEqual(0xCBF43926, underTestCrc32.Value);

        underTestCrc32.Reset();
        Assert.AreEqual(0x0, underTestCrc32.Value);

        exceptionTesting(underTestCrc32);
    }

    #endregion Public Methods
}
