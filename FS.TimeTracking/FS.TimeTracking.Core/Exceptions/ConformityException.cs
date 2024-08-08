using FS.TimeTracking.Core.Models.Application.Core;

namespace FS.TimeTracking.Core.Exceptions;

/// <summary>
/// Exception for signalling when the model is not conform.
/// </summary>
public class ConformityException : ApplicationErrorException
{
    /// <inheritdoc />
    public ConformityException(params string[] errors)
        : base(ApplicationErrorCode.BadRequest, errors) { }

    /// <inheritdoc />
    public ConformityException(ApplicationErrorCode errorCode, params string[] errors)
        : base(errorCode, errors) { }
}