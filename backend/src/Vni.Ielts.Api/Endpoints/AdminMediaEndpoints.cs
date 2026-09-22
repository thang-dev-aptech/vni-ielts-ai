using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>
/// The CMS media library's HTTP surface — the server side of
/// <c>apps/admin/src/screens/MediaLibraryPage.tsx</c>, which has been built
/// against a browser store since before this API existed.
///
/// <para>
/// <b>Who may do what follows the seeded split, not a new one.</b>
/// <c>media.read</c> opens the library and its bytes; <c>media.upload</c> adds
/// to it; <c>media.retire</c> withdraws and removes. The split exists because
/// the author who uploads a file is not the person who should be able to
/// clean the library out from under drafts others are editing.
/// → docs/ux/cms-content-operations.md § 5
/// </para>
/// </summary>
public sealed record MediaVersionReferenceView(
    string VersionId,
    string Title,
    string State);

public sealed record MediaAssetView(
    string MediaId,
    string Kind,
    string FileName,
    string ContentType,
    long Bytes,
    long? DurationMs,
    string Checksum,
    string UploadedByName,
    string UploadedAt,
    bool Retired,
    IReadOnlyList<MediaVersionReferenceView> ReferencedBy);

public sealed record MediaLibraryView(IReadOnlyList<MediaAssetView> Media);

public static class AdminMediaEndpoints
{
    /// <summary>
    /// The audio ceiling plus a multipart envelope allowance — the same move
    /// the import uploader makes, for the same reason: Kestrel's 1 MB default
    /// would refuse every real recording before application code ran, and the
    /// caller would see a connection reset instead of
    /// <see cref="ErrorCodes.MediaTooLarge"/>.
    /// </summary>
    private const long MultipartOverheadBytes = 64L * 1024;
    private const long MaxUploadBytes = 50L * 1024 * 1024 + MultipartOverheadBytes;

    public static void MapAdminMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/media")
            .WithTags("Admin")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.InSessionRead);

        group.MapGet("/", ListEndpoint)
            .WithName("AdminListMedia")
            .WithSummary("The whole media library, newest first")
            .Produces<MediaLibraryView>();

        group.MapPost("/", UploadEndpoint)
            .WithName("AdminUploadMedia")
            .WithSummary("Upload one file; the server sniffs its own type from the bytes")
            .DisableAntiforgery();

        group.MapGet("/{mediaId}/content", ContentEndpoint)
            .WithName("AdminGetMediaContent")
            .WithSummary("Stream one asset's bytes, with range and ETag support");

        group.MapPost("/{mediaId}/retire", RetireEndpoint)
            .WithName("AdminRetireMedia")
            .WithSummary("Withdraw an asset from the upload pickers; audited");

        group.MapDelete("/{mediaId}", DeleteEndpoint)
            .WithName("AdminDeleteMedia")
            .WithSummary("Remove an asset and its stored bytes; audited");
    }

    private static MediaAssetView ToView(
        MediaAsset asset,
        IReadOnlyList<Application.Media.MediaVersionReference> referencedBy) => new(
        asset.MediaId,
        asset.Kind.ToString().ToLowerInvariant(),
        asset.FileName,
        asset.ContentType,
        asset.Bytes,
        asset.DurationMs,
        asset.ChecksumSha256,
        asset.UploadedByName,
        asset.UploadedAt.ToString("o"),
        asset.Retired,
        referencedBy.Select(v => new MediaVersionReferenceView(v.VersionId, v.Title, v.State))
            .ToList());

    private static async Task<IResult> ListEndpoint(
        ClaimsPrincipal principal, ListMediaAssets assetHandler,
        Application.Media.ListMediaVersionReferences versionHandler, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.MediaRead) is { } denial) return denial;
        var assets = await assetHandler.HandleAsync(ct);
        var versionMapping = await versionHandler.HandleAsync(ct);

        var views = assets.Select(asset =>
        {
            var referencedBy = versionMapping.TryGetValue(asset.MediaId, out var versions)
                ? versions
                : new List<Application.Media.MediaVersionReference>();
            return ToView(asset, referencedBy);
        }).ToList();

        return Results.Ok(new MediaLibraryView(views));
    }

    private static async Task<IResult> UploadEndpoint(
        HttpRequest request, ClaimsPrincipal principal, UploadMediaAsset handler,
        IAuditLog audit, IClock clock, HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        if (Denied(principal, PermissionKeys.MediaUpload) is { } denial) return denial;

        if (!request.HasFormContentType)
            return Problem(ErrorCodes.ValidationFailed, "Expected a multipart upload.", 400, http);

        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap)
            cap.MaxRequestBodySize = MaxUploadBytes;

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return Problem(ErrorCodes.ValidationFailed, "An upload needs a non-empty 'file' part.", 400, http);

        long? durationMs = long.TryParse(form["durationMs"], out var parsed) ? parsed : null;

        await using var uploadStream = file.OpenReadStream();
        var result = await handler.HandleAsync(
            new UploadMediaAsset.Command(
                uploadStream,
                file.FileName,
                durationMs,
                principal.DisplayName() ?? principal.Email() ?? "unknown"),
            ct);

        if (!result.IsSuccess) return ApiProblem.From(result.Error, http);

        var asset = result.Value!;
        await Record(audit, principal, AuditAction.MediaUploaded, asset, clock, http, ct);
        return Results.Created($"/api/v1/admin/media/{asset.MediaId}/content",
            ToView(asset, new List<Application.Media.MediaVersionReference>()));
    }

    private static async Task<IResult> ContentEndpoint(
        string mediaId, ClaimsPrincipal principal, OpenMediaAssetContent handler, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.MediaRead) is { } denial) return denial;

        var result = await handler.HandleAsync(mediaId, ct);
        if (!result.IsSuccess) return Results.NotFound();

        /*
         * Range + ETag, the same two headers the learner asset endpoint
         * carries: an audio element seeks by byte ranges, and a file that has
         * not changed revalidates in one header rather than megabytes.
         */
        var content = result.Value!;
        return Results.Stream(
            content.Content,
            content.ContentType,
            enableRangeProcessing: true,
            entityTag: content.ETag is { } tag
                ? new Microsoft.Net.Http.Headers.EntityTagHeaderValue(tag)
                : null);
    }

    private static async Task<IResult> RetireEndpoint(
        string mediaId, ClaimsPrincipal principal, RetireMediaAsset handler,
        IAuditLog audit, IClock clock, HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        if (Denied(principal, PermissionKeys.MediaRetire) is { } denial) return denial;

        var result = await handler.HandleAsync(mediaId, ct);
        if (!result.IsSuccess) return ApiProblem.From(result.Error, http);

        var retired = result.Value!;
        await Record(audit, principal, AuditAction.MediaRetired, retired, clock, http, ct);
        return Results.Ok(ToView(retired, new List<Application.Media.MediaVersionReference>()));
    }

    private static async Task<IResult> DeleteEndpoint(
        string mediaId, ClaimsPrincipal principal, DeleteMediaAsset handler,
        IAuditLog audit, IClock clock, HttpContext http, CancellationToken ct)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        if (Denied(principal, PermissionKeys.MediaRetire) is { } denial) return denial;

        var result = await handler.HandleAsync(mediaId, ct);
        if (!result.IsSuccess) return ApiProblem.From(result.Error, http);

        var deleted = result.Value!;
        await Record(audit, principal, AuditAction.MediaDeleted, deleted, clock, http, ct,
            new Dictionary<string, string> { ["fileName"] = deleted.FileName });
        return Results.NoContent();
    }

    /// <summary>
    /// The operator's identity reaches the audit log here, at the edge, the
    /// same way the library endpoints record theirs — the handler knows what
    /// was stored; the endpoint knows who was holding the token.
    /// </summary>
    private static Task Record(
        IAuditLog audit, ClaimsPrincipal principal, AuditAction action,
        MediaAsset asset, IClock clock, HttpContext http, CancellationToken ct,
        IReadOnlyDictionary<string, string>? extra = null) =>
        audit.AppendAsync(
            AuditEntry.Record(
                new UserId(principal.UserId() ?? "unknown"),
                principal.Email() ?? principal.DisplayName() ?? "unknown",
                action, "media-asset", asset.MediaId, asset.FileName,
                clock.UtcNow,
                extra),
            ct);

    private static IResult? Denied(ClaimsPrincipal principal, string permission)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        return principal.Permissions().Contains(permission)
            ? null
            : Results.Problem(
                detail: $"This account does not hold {permission}.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = ErrorCodes.PermissionDenied,
                    ["permission"] = permission,
                });
    }

    private static IResult Problem(string code, string detail, int statusCode, HttpContext http) =>
        Results.Problem(
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["traceId"] = http.TraceIdentifier,
            });
}



