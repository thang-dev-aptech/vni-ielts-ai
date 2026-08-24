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

    private static IResult AssetEndpoint(
        string reference, ClaimsPrincipal principal, IDictationAssetStore assets)
    {
        if (principal.UserId() is null) return Results.Unauthorized();

        var stream = assets.Open($"assets/{reference}");
        if (stream is null) return Results.NotFound();

        return Results.Stream(stream, "audio/mp4", enableRangeProcessing: true);
    }
}
