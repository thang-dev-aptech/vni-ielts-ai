using Microsoft.AspNetCore.Http;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// The four response headers this API has to send, and the reason each one is
/// not optional here.
///
/// <list type="bullet">
/// <item><b><c>X-Content-Type-Options: nosniff</c></b> — the asset endpoint
/// deliberately serves an unrecognised extension as
/// <c>application/octet-stream</c> rather than guessing, and comments in
/// <c>FixtureAssetStore</c> say so. That defence is incomplete without this
/// header: a browser is free to sniff a declared octet-stream and decide it
/// looks like HTML. Exam media arrives from an uploaded ZIP, which is
/// untrusted input by rule 3.</item>
///
/// <item><b><c>X-Frame-Options: DENY</c></b> and a matching
/// <c>frame-ancestors</c> — nothing this API returns is meant to be framed.
/// The CMS performs one-click state changes (publish, suspend, grant a role)
/// behind a session cookie-less bearer token, but a framed learner app is
/// still a phishing surface.</item>
///
/// <item><b><c>Referrer-Policy</c></b> — paths here carry session ids and user
/// ids. A full referrer sent to a third-party host leaks them.</item>
///
/// <item><b><c>Content-Security-Policy</c></b> — restrictive because this
/// origin serves JSON and media, never a document. If it ever serves a page,
/// this needs revisiting rather than relaxing by reflex.</item>
/// </list>
///
/// <b>HSTS is not here.</b> It belongs on the edge that terminates TLS, and
/// setting it from an origin reachable over plain HTTP in development is how a
/// developer ends up unable to load localhost. → `docs/development/nfr.md`
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

        return next(context);
    }
}
