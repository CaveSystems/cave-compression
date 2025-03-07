namespace Cave.Compression.Checksum;

/// <summary>
/// Interface to compute a data checksum used by checked input/output streams. A data checksum can be updated by one byte or with a byte array. After each
/// update the value of the current checksum can be returned by calling.
/// <code>getValue</code>
/// . The complete checksum object can also be reset so it can be used again with new data.
/// </summary>
public interface IZipChecksum
{
    #region Public Properties

    /// <summary>Gets the data checksum computed so far.</summary>
    long Value { get; }

    #endregion Public Properties

    #region Public Methods

    /// <summary>Resets the data checksum as if no update was ever called.</summary>
    void Reset();

    /// <summary>Adds one byte to the data checksum.</summary>
    /// <param name="bval">the data value to add. The high byte of the int is ignored.</param>
    void Update(int bval);

    /// <summary>Updates the data checksum with the bytes taken from the array.</summary>
    /// <param name="buffer">buffer an array of bytes.</param>
    void Update(byte[] buffer);

    /// <summary>Adds the byte array to the data checksum.</summary>
    /// <param name="buffer">The buffer which contains the data.</param>
    /// <param name="offset">The offset in the buffer where the data starts.</param>
    /// <param name="count">the number of data bytes to add.</param>
    void Update(byte[] buffer, int offset, int count);

    #endregion Public Methods
}
