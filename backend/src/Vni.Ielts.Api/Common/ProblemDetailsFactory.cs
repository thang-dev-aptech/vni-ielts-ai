using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Vni.Ielts.Application.Common;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// One error envelope for the whole API.
///
/// Every error response carries a stable machine-readable <c>code</c>.
/// <b>Clients branch on that, never on the title or detail</b> — those are
/// human-facing and will be translated. Four first-party clients depend on it,
/// and the mobile ones cannot be force-updated, so a code rename is a breaking
/// change even though nothing about it looks like a contract.
///
/// → docs/api/api-design-principles.md § Errors
/// </summary>
public static class ApiProblem
{
    public static IResult From(Error error, HttpContext http)
    {
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            // 404 for anything outside the caller's visibility, never 403.
            // A 403 confirms the resource exists, which is an enumeration
            // oracle. → threat T19
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(
            title: Title(error.Kind),
            detail: error.Detail,
            statusCode: status,
            type: $"https://api.vni-ielts.example/errors/{Slug(error.Code)}",
            instance: http.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = error.Code,
                ["traceId"] = http.TraceIdentifier,
            });
    }

    /// <summary>
    /// A validation failure carrying per-field detail.
    ///
    /// <b>All errors at once, never the first.</b> A form that reveals one
    /// problem per submission is a poor experience anywhere, and on a timed
    /// exam it is actively harmful.
    /// </summary>
    public static IResult Validation(
        IReadOnlyCollection<FieldError> errors, HttpContext http) =>
        Results.Problem(
            title: "Validation failed",
            detail: "One or more fields are invalid.",
            statusCode: StatusCodes.Status400BadRequest,
            type: "https://api.vni-ielts.example/errors/validation-failed",
            instance: http.Request.Path,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ErrorCodes.ValidationFailed,
                ["traceId"] = http.TraceIdentifier,
                ["errors"] = errors,
            });

    private static string Title(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "Validation failed",
        ErrorKind.Unauthorized => "Not authenticated",
        ErrorKind.Forbidden => "Not permitted",
        ErrorKind.NotFound => "Not found",
        ErrorKind.Conflict => "Conflict",
        _ => "Unexpected error",
    };

    private static string Slug(string code) => code.Replace('_', '-').ToLowerInvariant();
}

public sealed record FieldError(string Path, string Code, string Message);
