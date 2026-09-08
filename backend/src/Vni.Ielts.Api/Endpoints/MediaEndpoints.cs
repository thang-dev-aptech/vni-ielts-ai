using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Media;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>
/// The Media Library — uploads independent of the package pipeline
/// (`docs/development/phase-2-content-import-plan.md` Plan 04). Matches
/// <c>apps/admin/src/lib/media.ts</c>'s already-shipped client contract:
/// same kinds, same size ceilings, same magic-byte signatures, checked again
/// here because an uploaded file is untrusted input and a browser-side check
/// is for the operator's convenience, not the boundary.
/// </summary>
public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/media")
            .WithTags("Media")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.InSessionRead);

        group.MapGet("/", ListEndpoint)
            .WithName("AdminListMedia")
            .WithSummary("Every uploaded media asset, newest first");

        group.MapPost("/", UploadEndpoint)
            .WithName("AdminUploadMedia")
            .WithSummary("Add one file to the Media Library")
            .DisableAntiforgery();

        group.MapPost("/{mediaId}/retire", RetireEndpoint)
            .WithName("AdminRetireMedia")
            .WithSummary("Withdraw a file from the picker without touching what already uses it");

        group.MapDelete("/{mediaId}", DeleteEndpoint)
            .WithName("AdminDeleteMedia")
            .WithSummary("Remove a file nothing references");
    }

    // ── Signatures — same bytes as apps/admin/src/lib/media.ts's SIGNATURES ─

    private sealed record Signature(MediaKind Kind, string ContentType, int At, byte[] Bytes);

    private static readonly Signature[] Signatures =
    [
        new(MediaKind.Audio, "audio/mpeg", 0, [0x49, 0x44, 0x33]), // ID3
        new(MediaKind.Audio, "audio/mpeg", 0, [0xff, 0xfb]),
        new(MediaKind.Audio, "audio/mpeg", 0, [0xff, 0xf3]),
        new(MediaKind.Audio, "audio/mp4", 4, [0x66, 0x74, 0x79, 0x70]), // ftyp
        new(MediaKind.Audio, "audio/wav", 0, [0x52, 0x49, 0x46, 0x46]), // RIFF
        new(MediaKind.Audio, "audio/ogg", 0, [0x4f, 0x67, 0x67, 0x53]), // OggS
        new(MediaKind.Image, "image/png", 0, [0x89, 0x50, 0x4e, 0x47]),
        new(MediaKind.Image, "image/jpeg", 0, [0xff, 0xd8, 0xff]),
        new(MediaKind.Image, "image/webp", 8, [0x57, 0x45, 0x42, 0x50]),
        new(MediaKind.File, "application/pdf", 0, [0x25, 0x50, 0x44, 0x46]),
    ];

    /// <summary>Same ceilings as `media.ts`'s <c>MAX_BYTES</c> — a Listening part is comfortably under 50 MB; anything past it is an accidental export.</summary>
    private static readonly Dictionary<MediaKind, long> MaxBytes = new()
    {
        [MediaKind.Audio] = 50L * 1024 * 1024,
        [MediaKind.Image] = 5L * 1024 * 1024,
        [MediaKind.File] = 20L * 1024 * 1024,
    };

    private static Signature? Sniff(byte[] head)
    {
        foreach (var signature in Signatures)
        {
            if (signature.At + signature.Bytes.Length > head.Length) continue;
            if (signature.Bytes.Select((b, i) => head[signature.At + i] == b).All(m => m))
                return signature;
        }

        return null;
    }

    private static readonly TimeSpan UploadCleanupTimeout = TimeSpan.FromSeconds(15);

    private static async Task<IResult> UploadEndpoint(
        ClaimsPrincipal principal,
        HttpRequest request,
        [FromServices] IMediaAssetRepository media,
        [FromServices] IObjectStorage storage,
        [FromServices] IMediaOrphanReconciliation orphans,
        [FromServices] IAuditLog audit,
        [FromServices] IClock clock,
        [FromServices] ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.MediaUpload) is { } denied) return denied;

        if (!request.HasFormContentType)
            return Problem(ErrorCodes.ValidationFailed, "Expected a multipart upload.", StatusCodes.Status400BadRequest);

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
            return Problem(ErrorCodes.ValidationFailed, "A media upload needs a file part named \"file\".", StatusCodes.Status400BadRequest);

        // Magic bytes decide the kind — never the declared filename or the
        // client's Content-Type header, both attacker-controlled.
        await using var content = file.OpenReadStream();
        var head = new byte[12];
        var read = await content.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        content.Position = 0;

        var signature = Sniff(head[..read]);
        if (signature is null)
            return Problem(ErrorCodes.ValidationFailed, "This file's format is not recognised.", StatusCodes.Status400BadRequest);

        if (file.Length > MaxBytes[signature.Kind])
            return Problem(ErrorCodes.PayloadTooLarge, "This file is larger than this category accepts.", StatusCodes.Status413PayloadTooLarge);

        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(content, ct)).ToLowerInvariant();
        content.Position = 0;
        var storageKey = await storage.PutAsync(content, signature.ContentType, ct);

        var now = clock.UtcNow;
        var asset = MediaAsset.Create(
            Guid.NewGuid().ToString("n"), signature.Kind, file.FileName, signature.ContentType,
            file.Length, durationMs: null, sha256, storageKey, new UserId(principal.UserId()!), now);

        try
        {
            await media.SaveAsync(asset, ct);
        }
        catch (Exception)
        {
            // Never use the request token for compensation — the client may
            // already have canceled, and that must not leave an orphan object.
            await CompensateFailedUploadAsync(
                storage, orphans, loggers.CreateLogger("MediaUploadCompensation"),
                storageKey, asset.Id, asset.FileName, now);
            throw;
        }

        await Record(
            audit, principal, AuditAction.MediaUploaded,
            "media", asset.Id, asset.FileName, now, ct,
            new Dictionary<string, string> { ["kind"] = asset.Kind.ToString() });

        return Results.Ok(ToWire(asset, principal.DisplayName() ?? principal.Email() ?? "unknown"));
    }

    /// <summary>
    /// Bounded cleanup after Put+Save partial failure. Independent of the
    /// request cancellation token; on cleanup failure records durable intent.
    /// </summary>
    internal static async Task CompensateFailedUploadAsync(
        IObjectStorage storage,
        IMediaOrphanReconciliation orphans,
        ILogger logger,
        string storageKey,
        string mediaId,
        string fileName,
        DateTimeOffset now)
    {
        using var cleanup = new CancellationTokenSource(UploadCleanupTimeout);
        try
        {
            await storage.DeleteAsync(storageKey, cleanup.Token);
        }
        catch (Exception cleanupEx)
        {
            logger.LogError(
                cleanupEx,
                "Media upload compensation failed for storage key {StorageKey}; recording orphan intent.",
                storageKey);

            try
            {
                var errorCode = MediaCleanupErrorCodes.From(cleanupEx);
                await orphans.RecordFailedCleanupAsync(
                    storageKey, mediaId, fileName, errorCode, now, CancellationToken.None);
            }
            catch (Exception recordEx)
            {
                logger.LogError(
                    recordEx,
                    "Failed to record media orphan intent for storage key {StorageKey}.",
                    storageKey);
            }
        }
    }

    private static async Task<IResult> ListEndpoint(
        ClaimsPrincipal principal,
        [FromServices] IMediaAssetRepository media,
        [FromServices] IUserRepository users,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.MediaRead) is { } denied) return denied;

        var assets = await media.ListAsync(ct);
        var names = await DisplayNamesOf(users, assets.Select(a => a.UploadedBy.Value), ct);

        return Results.Ok(assets.Select(a => ToWire(a, names.GetValueOrDefault(a.UploadedBy.Value, "(tài khoản đã xoá)"))));
    }

    private static async Task<IResult> RetireEndpoint(
        string mediaId, ClaimsPrincipal principal,
        [FromServices] IMediaAssetRepository media,
        [FromServices] IAuditLog audit,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.MediaRetire) is { } denied) return denied;

        var asset = await media.FindAsync(mediaId, ct);
        if (asset is null) return Results.NotFound();

        try
        {
            asset.Retire();
        }
        catch (InvalidOperationException e)
        {
            return Problem(ErrorCodes.ValidationFailed, e.Message, StatusCodes.Status409Conflict);
        }

        await media.SaveAsync(asset, ct);

        await Record(
            audit, principal, AuditAction.MediaRetired, "media", asset.Id, asset.FileName, clock.UtcNow, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteEndpoint(
        string mediaId, ClaimsPrincipal principal,
        [FromServices] IMediaAssetRepository media,
        [FromServices] IObjectStorage storage,
        [FromServices] IExamCatalogue catalogue,
        [FromServices] IAuditLog audit,
        [FromServices] IClock clock,
        CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.MediaRetire) is { } denied) return denied;

        var asset = await media.FindAsync(mediaId, ct);
        if (asset is null) return Results.NotFound();

        if (await IsReferencedAsync(catalogue, mediaId, ct))
        {
            return Problem(
                ErrorCodes.ValidationFailed,
                "This file is referenced by at least one exam version and cannot be deleted.",
                StatusCodes.Status409Conflict);
        }

        // Storage first, then metadata. DeleteObject is idempotent; if metadata
        // delete fails the row still points at a missing key and a retry finishes
        // the job. The reverse order (metadata then storage) orphans billable objects.
        await storage.DeleteAsync(asset.StorageKey, ct);
        await media.DeleteAsync(mediaId, ct);

        await Record(
            audit, principal, AuditAction.MediaDeleted, "media", asset.Id, asset.FileName, clock.UtcNow, ct);

        return Results.NoContent();
    }

    /// <summary>
    /// Real, but currently always false: nothing produces a
    /// <c>media/&lt;id&gt;</c>-shaped <c>assetRef</c> yet — that format is
    /// Phase 3's (in-place authoring). Written against the format the plan
    /// already names, not against today's content, so wiring Phase 3 in does
    /// not require rewriting this check.
    /// </summary>
    private static async Task<bool> IsReferencedAsync(IExamCatalogue catalogue, string mediaId, CancellationToken ct)
    {
        var reference = $"media/{mediaId}";
        var versions = await catalogue.ListAllAsync(ct);

        return versions
            .SelectMany(v => v.Sections)
            .SelectMany(s => s.Parts)
            .Any(p => p.AudioKey == reference
                || p.ImageKey == reference
                || p.Questions.Any(q => q.Group?.Image == reference));
    }

    private static object ToWire(MediaAsset asset, string uploadedByName) => new
    {
        mediaId = asset.Id,
        kind = asset.Kind.ToString().ToLowerInvariant(),
        fileName = asset.FileName,
        contentType = asset.ContentType,
        bytes = asset.Bytes,
        durationMs = asset.DurationMs,
        checksum = asset.Checksum,
        uploadedByName,
        uploadedAt = asset.UploadedAt,
        retired = asset.Retired,
    };

    private static async Task<Dictionary<string, string>> DisplayNamesOf(
        IUserRepository users, IEnumerable<string?> ids, CancellationToken ct)
    {
        var names = new Dictionary<string, string>();

        foreach (var id in ids.Where(id => id is not null).Select(id => id!).Distinct())
        {
            var user = await users.FindByIdAsync(new UserId(id), ct);
            names[id] = user?.DisplayName ?? "(tài khoản đã xoá)";
        }

        return names;
    }

    private static Task Record(
        IAuditLog audit, ClaimsPrincipal principal, AuditAction action,
        string targetType, string targetId, string targetLabel,
        DateTimeOffset now, CancellationToken ct,
        IReadOnlyDictionary<string, string>? detail = null) =>
        audit.AppendAsync(
            AuditEntry.Record(
                new UserId(principal.UserId() ?? "unknown"),
                principal.Email() ?? principal.DisplayName(),
                action, targetType, targetId, targetLabel, now, detail),
            ct);

    private static IResult? Denied(ClaimsPrincipal principal, string permission)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        return principal.Permissions().Contains(permission) ? null : Forbidden(permission);
    }

    private static IResult Forbidden(string permission) =>
        Results.Problem(
            detail: $"This account does not hold {permission}.",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ErrorCodes.PermissionDenied,
                ["permission"] = permission,
            });

    private static IResult Problem(string code, string detail, int status) =>
        Results.Problem(
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
