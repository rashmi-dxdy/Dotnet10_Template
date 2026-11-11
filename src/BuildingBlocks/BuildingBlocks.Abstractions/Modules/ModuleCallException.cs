namespace BuildingBlocks.Abstractions.Modules;

/// <summary>
/// A call to another module's internal API failed: the module was unreachable, timed out, or answered with an
/// error status. Lets Application code react to a failed call without depending on HTTP types.
/// </summary>
public sealed class ModuleCallException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ModuleCallException"/> class.</summary>
    public ModuleCallException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ModuleCallException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public ModuleCallException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ModuleCallException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error.</param>
    public ModuleCallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ModuleCallException"/> class.</summary>
    /// <param name="module">The module that was called.</param>
    /// <param name="statusCode">The HTTP status code it answered with, if it answered.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public ModuleCallException(string module, int? statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Module = module;
        StatusCode = statusCode;
    }

    /// <summary>Gets the module that was called.</summary>
    public string? Module { get; }

    /// <summary>Gets the HTTP status code the module answered with; <see langword="null"/> if it did not answer.</summary>
    public int? StatusCode { get; }
}
