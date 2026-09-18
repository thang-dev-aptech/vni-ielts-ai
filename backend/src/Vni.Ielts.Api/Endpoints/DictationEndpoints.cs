using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Dictation;

namespace Vni.Ielts.Api.Endpoints;

public sealed record CheckSentenceRequest(int Order, string Typed);

/// <summary>
/// The sets, wrapped.
///
/// <b>A named record rather than <c>new { sets = ... }</c>, so it has a
/// schema.</b> The wire shape is identical; what changes is that
/// <c>contracts/openapi</c> can describe it, and therefore that
/// <c>@vni/api-client</c> can carry it. Until this existed the whole dictation
/// group reached both clients as an untyped "OK" and each wrote the shape out
/// by hand. → `W7`, `A17`
/// </summary>
public sealed record DictationSetListView(IReadOnlyList<DictationSetSummary> Sets);

/// <summary>
/// Nghe chép chính tả — `M-22`.
///
/// <b>The comparison happens here, not in the browser.</b> Sending the
/// sentence to the client so it can diff locally would mean the answer is on
/// the page before the learner has typed anything; the exercise would still
/// look like it worked, and would teach nothing. Same rule as an exam answer
/// key. → threat `T7`
///
/// <b>Authenticated, like every other content route.</b> Not because a
/// sentence is secret, but because a corpus that can be scraped anonymously
/// can be republished with its answers.
/// </summary>
public static class DictationEndpoints
{
    public static void MapDictationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/dictation").WithTags("Dictation").RequireAuthorization();

        // <b>The response types are declared, not inferred.</b> These handlers
        // return `IResult`; without these lines nothing downstream knows what a
        // 200 carries. → `W7`
        group.MapGet("/", ListEndpoint)
            .WithName("ListDictationSets")
            .WithSummary("The dictation sets available")
            .Produces<DictationSetListView>();

        group.MapGet("/assets/{**reference}", AssetEndpoint)
            .WithName("GetDictationAsset")
            .WithSummary("Audio for one sentence")
            // Bytes, not JSON, so the promise is the media type rather than a
            // schema — the same treatment the import template download gets.
            // The list is exactly what the stores can answer with: an unknown
            // extension is served as `application/octet-stream` rather than as
            // whatever a browser would sniff it into.
            .Produces<byte[]>(
                StatusCodes.Status200OK,
                "audio/mp4",
                "audio/mpeg",
                "audio/wav",
                "audio/ogg",
                "application/octet-stream");

        group.MapGet("/{setId}", GetEndpoint)
            .WithName("GetDictationSet")
            .WithSummary("A set's sentences — audio only, never the text")
            .Produces<DictationSetView>();

        group.MapPost("/{setId}/check", CheckEndpoint)
            .WithName("CheckDictationSentence")
            .WithSummary("Compare a typed sentence with what was said")
            .Produces<DictationResultView>();
    }

    private static async Task<IResult> ListEndpoint(
        ClaimsPrincipal principal, ListDictationSets handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } userId) return Results.Unauthorized();

        return Results.Ok(new DictationSetListView(await handler.HandleAsync(userId, ct)));
    }

    private static async Task<IResult> GetEndpoint(
        string setId, ClaimsPrincipal principal, GetDictationSet handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } userId) return Results.Unauthorized();

        return await handler.HandleAsync(userId, setId, ct) is { } set
            ? Results.Ok(set)
            : Results.NotFound();
    }

    private static async Task<IResult> CheckEndpoint(
        string setId, ClaimsPrincipal principal, CheckSentenceRequest request,
        CheckDictationSentence handler, CancellationToken ct)
    {
        if (principal.UserId() is not { } userId) return Results.Unauthorized();

        // An empty attempt is a legitimate one — it reports every word as
        // missing, which is exactly what a learner who heard nothing needs.
        var result = await handler.HandleAsync(
            userId, setId, request.Order, request.Typed ?? string.Empty, ct);

        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> AssetEndpoint(
        string reference, ClaimsPrincipal principal, IDictationAssetStore assets,
        CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        if (await assets.OpenAsync($"assets/{reference}", ct) is not { } asset)
            return Results.NotFound();

        /*
         * <b>Range requests, the store's own content type, and an entity tag.</b>
         *
         * The type was hard-coded to `audio/mp4`, which is right for the
         * fixtures and wrong for anything else the store holds — a browser told
         * the wrong type either refuses to play or sniffs, and sniffing is what
         * the store refuses to do for it.
         *
         * The tag is what makes a re-listen free. Dictation audio does not
         * change, so a browser that already has the file should be able to ask
         * "is it still this one" and be told yes in a header.
         */
        return Results.Stream(
            asset.Content,
            asset.ContentType,
            enableRangeProcessing: true,
            entityTag: asset.ETag is { } tag
                ? new Microsoft.Net.Http.Headers.EntityTagHeaderValue(tag)
                : null);
    }
}
