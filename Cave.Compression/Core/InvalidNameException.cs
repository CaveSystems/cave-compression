﻿using System;

namespace Cave.Compression.Core;

/// <summary>InvalidNameException is thrown for invalid names such as directory traversal paths and names with invalid characters</summary>
[Serializable]
public class InvalidNameException : Exception
{
    #region Public Constructors

    /// <summary>Initializes a new instance of the InvalidNameException class with a default error message.</summary>
    public InvalidNameException() : base("An invalid name was specified")
    {
    }

    /// <summary>Initializes a new instance of the InvalidNameException class with a specified error message.</summary>
    /// <param name="message">A message describing the exception.</param>
    public InvalidNameException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the InvalidNameException class with a specified error message and a reference to the inner exception that is the cause of
    /// this exception.
    /// </summary>
    /// <param name="message">A message describing the exception.</param>
    /// <param name="innerException">The inner exception</param>
    public InvalidNameException(string message, Exception innerException) : base(message, innerException)
    {
    }

    #endregion Public Constructors
}
