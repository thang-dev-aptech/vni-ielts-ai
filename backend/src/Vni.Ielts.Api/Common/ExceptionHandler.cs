using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Vni.Ielts.Application.Common;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// Turns an unhandled exception into the same problem-details envelope every
/// other error uses, and — crucially — into the <b>right status code</b>.
///
/// <para>
/// Without this, Kestrel's <see cref="BadHttpRequestException"/> for an
/// oversized body surfaced as <c>500</c>. That is wrong in a way that matters:
/// a 500 tells a client the server broke and invites a retry, so a client
/// sending a too-large payload retries it forever. A <c>413</c> tells the truth
/// and the client stops.
/// </para>
///
/// <para>
/// Internal detail never crosses this boundary. The client gets a stable code
/// and a trace id; the specifics go to the log, where they belong.
/// </para>
/// </summary>
public sealed class VniExceptionHandler(ILogger<VniExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, detail) = Map(exception);

        if (status >= StatusCodes.Status500InternalServerError)
        {
            // Method and Path both come from the request line. Besides making
            // cardinality unbounded, putting either in a log lets CR/LF from a
            // malformed request forge a second entry in line-oriented sinks.
            // The trace id already joins this event to the request telemetry.
            logger.LogError(exception, "Unhandled request exception");
        }
        else
        {
            // A client-caused failure is not a server incident. Logging it at
            // Error would drown the log in noise from ordinary bad requests.
            logger.LogInformation(
                "Request rejected with status {StatusCode} and code {Code}", status, code);
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = $"https://api.vni-ielts.example/errors/{code.Replace('_', '-').ToLowerInvariant()}",
            title = status >= 500 ? "Unexpected error" : "Request rejected",
            status,
            detail,
            instance = context.Request.Path.Value,
            code,
            traceId = context.TraceIdentifier,
        }, ct);

        return true;
    }

    /// <summary>Nginx's 499. Not among the framework constants, but honest in a log.</summary>
    private const int ClientClosedRequest = 499;

    private static (int Status, string Code, string Detail) Map(Exception exception) => exception switch
    {
        BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } =>
            (StatusCodes.Status413PayloadTooLarge, ErrorCodes.PayloadTooLarge,
             "The request body is larger than this endpoint accepts."),

        BadHttpRequestException bad =>
            (bad.StatusCode, ErrorCodes.ValidationFailed, "The request could not be read."),

        // The application asked for something the exam content cannot support —
        // most often an incomplete raw-to-band table. Surfaced rather than
        // swallowed, because the alternative is a fabricated band.
        InvalidOperationException when exception.Message.Contains(
            "Refusing to invent", StringComparison.Ordinal) =>
            (StatusCodes.Status500InternalServerError, "SCORING_PROFILE_INCOMPLETE",
             "This exam version cannot be scored. The result has not been published."),

        OperationCanceledException =>
            (ClientClosedRequest, "CLIENT_CLOSED_REQUEST",
             "The request was cancelled."),

        _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR",
              "An unexpected error occurred."),
    };
}
