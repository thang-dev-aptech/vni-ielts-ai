namespace Vni.Ielts.Application.Common;

/// <summary>
/// A use-case outcome that is either a value or a stable, machine-readable
/// failure code.
///
/// Exceptions are for defects — a null where there should not be one. An
/// expected failure like "that email is already registered" is not a defect
/// and should not cost a stack unwind or leave the API layer guessing which
/// exception maps to which status code.
///
/// The <c>Code</c> is the same stable string the client branches on, so the
/// mapping from use case to HTTP response has one obvious source.
/// → docs/api/api-design-principles.md § Errors
/// </summary>
public readonly record struct Error(string Code, string Detail, ErrorKind Kind)
{
    public static Error Validation(string code, string detail) => new(code, detail, ErrorKind.Validation);
    public static Error NotFound(string code, string detail) => new(code, detail, ErrorKind.NotFound);
    public static Error Conflict(string code, string detail) => new(code, detail, ErrorKind.Conflict);
    public static Error Unauthorized(string code, string detail) => new(code, detail, ErrorKind.Unauthorized);
    public static Error Forbidden(string code, string detail) => new(code, detail, ErrorKind.Forbidden);

    /// <summary>
    /// Refused because the caller has tried too often.
    ///
    /// Distinct from the HTTP rate limiter, which rejects before any use case
    /// runs. This one comes from a use case that knows something the pipeline
    /// cannot — which account is being attacked.
    /// </summary>
    public static Error TooManyRequests(string code, string detail) =>
        new(code, detail, ErrorKind.TooManyRequests);
}

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    TooManyRequests,
}

public readonly struct Result<T>
{
    private Result(T value)
    {
        Value = value;
        Error = default;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        Value = default;
        Error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public Error Error { get; }

    public static Result<T> Ok(T value) => new(value);
    public static Result<T> Fail(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error);
}
