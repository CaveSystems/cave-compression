using System;

namespace Cave.Compression.Lzma;

/// <summary>The exception that is thrown when the value of an argument is outside the allowable range.</summary>
sealed class LzmaInvalidParamException : InvalidOperationException
{
    #region Public Constructors

    public LzmaInvalidParamException(string paramName) : base($"Invalid parameter setting for parameter {paramName}!") => Data["Parameter"] = paramName;

    public LzmaInvalidParamException(string message, string paramName) : base(message) => Data["Parameter"] = paramName;

    #endregion Public Constructors
}
