using Microsoft.AspNetCore.Http;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// Stamps <c>X-Server-Time</c> on every response.
///
/// This is not a convenience header. The exam timer is server-authoritative
/// (ADR-0007, CLAUDE.md rule 1) and the client timer is display only — this
/// header is what lets the client correct its own drift without a dedicated
/// time endpoint, on every response it was already making.
///
/// It lives here, in the foundation, because retrofitting a header contract
/// across four first-party clients is not free, and because a client written
/// against a clock it owns has to be rebuilt rather than adjusted.
/// </summary>
public sealed class ServerTimeMiddleware(RequestDelegate next, IClock clock)
{
    public const string HeaderName = "X-Server-Time";

    public Task InvokeAsync(HttpContext context)
    {
        // OnStarting, because headers cannot be set once the response has begun
        // writing — and a handler that streams would otherwise silently lose it.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] =
                clock.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        });

        return next(context);
    }
}
