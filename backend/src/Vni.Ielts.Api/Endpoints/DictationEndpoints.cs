using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Dictation;

namespace Vni.Ielts.Api.Endpoints;

public sealed record CheckSentenceRequest(int Order, string Typed);

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

        group.MapGet("/", ListEndpoint)
            .WithName("ListDictationSets")
            .WithSummary("The dictation sets available");

        group.MapGet("/assets/{**reference}", AssetEndpoint)
            .WithName("GetDictationAsset")
            .WithSummary("Audio for one sentence");

        group.MapGet("/{setId}", GetEndpoint)
            .WithName("GetDictationSet")
            .WithSummary("A set's sentences — audio only, never the text");

        group.MapPost("/{setId}/check", CheckEndpoint)
            .WithName("CheckDictationSentence")
            .WithSummary("Compare a typed sentence with what was said");
    }

    private static IResult ListEndpoint(ClaimsPrincipal principal, ListDictationSets handler) =>
        principal.UserId() is null
            ? Results.Unauthorized()
            : Results.Ok(new { sets = handler.Handle() });

    private static IResult GetEndpoint(
        string setId, ClaimsPrincipal principal, GetDictationSet handler)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        return handler.Handle(setId) is { } set ? Results.Ok(set) : Results.NotFound();
    }

    private static IResult CheckEndpoint(
        string setId, ClaimsPrincipal principal, CheckSentenceRequest request,
        CheckDictationSentence handler)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        // An empty attempt is a legitimate one — it reports every word as
        // missing, which is exactly what a learner who heard nothing needs.
        var result = handler.Handle(setId, request.Order, request.Typed ?? string.Empty);

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
