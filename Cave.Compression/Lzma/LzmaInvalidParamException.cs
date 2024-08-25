using System;

namespace Cave.Compression.Lzma;

/// <summary>The exception that is thrown when the value of an argument is outside the allowable range.</summary>
class LzmaInvalidParamException : ArgumentException
{
    #region Public Constructors

    public LzmaInvalidParamException(string paramName) : base($"Invalid parameter setting!", paramName) { }

    public LzmaInvalidParamException(string message, string paramName) : base(message, paramName) { }

    #endregion Public Constructors
}
