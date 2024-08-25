using System;

namespace Cave.Compression.Lzma;

/// <summary>The exception that is thrown when an error in input stream occurs during decoding.</summary>
public class LzmaDataErrorException : ApplicationException
{
    #region Public Constructors

    /// <summary>Creates a new instance</summary>
    public LzmaDataErrorException() : base("Data Error") { }

    #endregion Public Constructors
}
