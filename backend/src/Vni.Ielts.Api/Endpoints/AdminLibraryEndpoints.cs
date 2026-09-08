using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Application.Content.Library;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Audit;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content.Library;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Api.Endpoints;

/// <summary>
/// The CMS half of the two libraries — <c>P-22</c>, lifecycle per
/// <c>cms-content-operations.md</c> § 3.2 (<c>Đ8</c>).
///
/// <b>Same rules as <see cref="AdminEndpoints"/>, restated because they are
/// enforced here and not there.</b> Every route checks a permission on the
/// server — hiding a button in the CMS is not authorisation. <c>*.write</c>
/// creates, edits, deletes and submits; <c>*.publish</c> publishes,
/// unpublishes and returns a submission to draft. A content editor holds the
/// first and not the second, mirroring <c>exam.update</c> / <c>exam.publish</c>
/// (threat <c>T20</c>). Publish and unpublish write an audit entry in the same
/// request as the change.
///
/// Every POST, PUT and DELETE under <c>/api/v1</c> passes the idempotency
/// middleware, so a retried create makes one document, not two.
/// </summary>
public static class AdminLibraryEndpoints
{
    public static void MapAdminLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/library")
            .WithTags("Admin")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.InSessionRead);

        // ── Documents ──────────────────────────────────────────────────────

        group.MapGet("/documents", ListDocumentsEndpoint)
            .WithName("AdminListLibraryDocuments")
            .WithSummary("Every document, drafts included")
            .Produces<LibraryDocumentListView>();

        group.MapPost("/documents", CreateDocumentEndpoint)
            .WithName("AdminCreateLibraryDocument")
            .WithSummary("Create a draft document")
            .Produces<LibraryDocumentView>(StatusCodes.Status201Created);

        group.MapGet("/documents/{id}", GetDocumentEndpoint)
            .WithName("AdminGetLibraryDocument")
            .Produces<LibraryDocumentView>();

        group.MapPut("/documents/{id}", UpdateDocumentEndpoint)
            .WithName("AdminUpdateLibraryDocument")
            .WithSummary("Replace a document's editable fields; status is unchanged")
            .Produces<LibraryDocumentView>();

        group.MapDelete("/documents/{id}", DeleteDocumentEndpoint)
            .WithName("AdminDeleteLibraryDocument")
            .WithSummary("Delete a document that is not published")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/documents/{id}/submit", (string id, ClaimsPrincipal p, ChangeLibraryDocumentStatus h, IAuditLog a, IClock c, HttpContext x, CancellationToken ct) =>
                DocumentTransition(id, LibraryTransition.Submit, PermissionKeys.DocumentWrite, null, p, h, a, c, x, ct))
            .WithName("AdminSubmitLibraryDocument")
            .WithSummary("Draft → pending review");

        group.MapPost("/documents/{id}/return", (string id, ClaimsPrincipal p, ChangeLibraryDocumentStatus h, IAuditLog a, IClock c, HttpContext x, CancellationToken ct) =>
                DocumentTransition(id, LibraryTransition.Return, PermissionKeys.DocumentPublish, null, p, h, a, c, x, ct))
            .WithName("AdminReturnLibraryDocument")
            .WithSummary("Pending review → draft");

        group.MapPost("/documents/{id}/publish", (string id, ClaimsPrincipal p, ChangeLibraryDocumentStatus h, IAuditLog a, IClock c, HttpContext x, CancellationToken ct) =>
                DocumentTransition(id, LibraryTransition.Publish, PermissionKeys.DocumentPublish, AuditAction.DocumentPublished, p, h, a, c, x, ct))
            .WithName("AdminPublishLibraryDocument")
            .WithSummary("Make a document visible to learners");

        group.MapPost("/documents/{id}/unpublish", (string id, ClaimsPrincipal p, ChangeLibraryDocumentStatus h, IAuditLog a, IClock c, HttpContext x, CancellationToken ct) =>
                DocumentTransition(id, LibraryTransition.Unpublish, PermissionKeys.DocumentPublish, AuditAction.DocumentUnpublished, p, h, a, c, x, ct))
            .WithName("AdminUnpublishLibraryDocument")
            .WithSummary("Hide a document from learners");

        // ── Articles ───────────────────────────────────────────────────────

        group.MapGet("/articles", ListArticlesEndpoint)
            .WithName("AdminListArticles")
            .WithSummary("Every article, drafts included")
            .Produces<ArticleListView>();

        group.MapPost("/articles", CreateArticleEndpoint)
            .WithName("AdminCreateArticle")
            .WithSummary("Create a draft article; 409 SLUG_TAKEN if the slug is in use")
            .Produces<ArticleView>(StatusCodes.Status201Created);

        group.MapGet("/articles/{id}", GetArticleEndpoint)
            .WithName("AdminGetArticle")
            .Produces<ArticleView>();

        group.MapPut("/articles/{id}", UpdateArticleEndpoint)
            .WithName("AdminUpdateArticle")
            .WithSummary("Replace an article's editable fields; status is unchanged")
            .Produces<ArticleView>();

        group.MapDelete("/articles/{id}", DeleteArticleEndpoint)
            .WithName("AdminDeleteArticle")
            .WithSummary("Delete an article that is not published")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/articles/{id}/submit", (string id, ClaimsPrincipal p, ChangeArticleStatus h, IAuditLog a, IClock c, HttpContext x, CancellationToken ct) =>
                ArticleTransition(id, LibraryTransition.Submit, PermissionKeys.ArticleWrite, null, p, h, a, c, x, ct))
            .WithName("AdminSubmitArticle")
            .WithSummary("Draft → pending review");

        group.MapPost("/articles/{id}/return", (string id, ClaimsPrincipal p, ChangeArticleStatus h, IAuditLog a, IClock c, HttpContext x, CancellationToken ct) =>
                ArticleTransition(id, LibraryTransition.Return, PermissionKeys.ArticlePublish, null, p, h, a, c, x, ct))
            .WithName("AdminReturnArticle")
            .WithSummary("Pending review → draft");

        group.MapPost("/articles/{id}/publish", (string id, ClaimsPrincipal p, ChangeArticleStatus h, IAuditLog a, IClock c, HttpContext x, CancellationToken ct) =>
                ArticleTransition(id, LibraryTransition.Publish, PermissionKeys.ArticlePublish, AuditAction.ArticlePublished, p, h, a, c, x, ct))
            .WithName("AdminPublishArticle")
            .WithSummary("Make an article visible to learners at /library/articles/{slug}");

        group.MapPost("/articles/{id}/unpublish", (string id, ClaimsPrincipal p, ChangeArticleStatus h, IAuditLog a, IClock c, HttpContext x, CancellationToken ct) =>
                ArticleTransition(id, LibraryTransition.Unpublish, PermissionKeys.ArticlePublish, AuditAction.ArticleUnpublished, p, h, a, c, x, ct))
            .WithName("AdminUnpublishArticle")
            .WithSummary("Hide an article from learners");
    }

    // ── Documents ──────────────────────────────────────────────────────────

    private static async Task<IResult> ListDocumentsEndpoint(
        ClaimsPrincipal principal, ListAllLibraryDocuments handler, CancellationToken ct)
    {
        if (DeniedUnlessAny(principal, PermissionKeys.DocumentWrite, PermissionKeys.DocumentPublish) is { } d) return d;
        return Results.Ok(new LibraryDocumentListView(await handler.HandleAsync(ct)));
    }

    private static async Task<IResult> GetDocumentEndpoint(
        string id, ClaimsPrincipal principal, GetLibraryDocumentForEditing handler,
        HttpContext http, CancellationToken ct)
    {
        if (DeniedUnlessAny(principal, PermissionKeys.DocumentWrite, PermissionKeys.DocumentPublish) is { } d) return d;
        var result = await handler.HandleAsync(new LibraryDocumentId(id), ct);
        return result.Match(Results.Ok, e => ApiProblem.From(e, http));
    }

    private static async Task<IResult> CreateDocumentEndpoint(
        LibraryDocumentInput input, ClaimsPrincipal principal, CreateLibraryDocument handler,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.DocumentWrite) is { } d) return d;

        var (details, errors) = input.Parse();
        if (details is null) return ApiProblem.Validation(Map(errors), http);

        var view = await handler.HandleAsync(details, new UserId(principal.UserId()!), ct);
        return Results.Created($"/api/v1/admin/library/documents/{view.Id}", view);
    }

    private static async Task<IResult> UpdateDocumentEndpoint(
        string id, LibraryDocumentInput input, ClaimsPrincipal principal, UpdateLibraryDocument handler,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.DocumentWrite) is { } d) return d;

        var (details, errors) = input.Parse();
        if (details is null) return ApiProblem.Validation(Map(errors), http);

        var result = await handler.HandleAsync(new LibraryDocumentId(id), details, ct);
        return result.Match(Results.Ok, e => ApiProblem.From(e, http));
    }

    private static async Task<IResult> DeleteDocumentEndpoint(
        string id, ClaimsPrincipal principal, DeleteLibraryDocument handler,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.DocumentWrite) is { } d) return d;
        var result = await handler.HandleAsync(new LibraryDocumentId(id), ct);
        return result.Match(_ => Results.NoContent(), e => ApiProblem.From(e, http));
    }

    private static async Task<IResult> DocumentTransition(
        string id, LibraryTransition transition, string permission, AuditAction? audited,
        ClaimsPrincipal principal, ChangeLibraryDocumentStatus handler, IAuditLog audit, IClock clock,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, permission) is { } d) return d;

        var result = await handler.HandleAsync(new LibraryDocumentId(id), transition, ct);
        if (!result.IsSuccess) return ApiProblem.From(result.Error, http);

        var view = result.Value!;
        if (audited is { } action)
            await Record(audit, principal, action, "library-document", view.Id, view.Title, clock.UtcNow, ct,
                new Dictionary<string, string> { ["slug"] = view.Slug });

        return Results.Ok(view);
    }

    // ── Articles ───────────────────────────────────────────────────────────

    private static async Task<IResult> ListArticlesEndpoint(
        ClaimsPrincipal principal, ListAllArticles handler, CancellationToken ct)
    {
        if (DeniedUnlessAny(principal, PermissionKeys.ArticleWrite, PermissionKeys.ArticlePublish) is { } d) return d;
        return Results.Ok(new ArticleListView(await handler.HandleAsync(ct)));
    }

    private static async Task<IResult> GetArticleEndpoint(
        string id, ClaimsPrincipal principal, GetArticleForEditing handler,
        HttpContext http, CancellationToken ct)
    {
        if (DeniedUnlessAny(principal, PermissionKeys.ArticleWrite, PermissionKeys.ArticlePublish) is { } d) return d;
        var result = await handler.HandleAsync(new ArticleId(id), ct);
        return result.Match(Results.Ok, e => ApiProblem.From(e, http));
    }

    private static async Task<IResult> CreateArticleEndpoint(
        ArticleInput input, ClaimsPrincipal principal, CreateArticle handler,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ArticleWrite) is { } d) return d;

        var (details, errors) = input.Parse();
        if (details is null) return ApiProblem.Validation(Map(errors), http);

        var result = await handler.HandleAsync(details, new UserId(principal.UserId()!), ct);
        return result.Match(
            v => Results.Created($"/api/v1/admin/library/articles/{v.Id}", v),
            e => ApiProblem.From(e, http));
    }

    private static async Task<IResult> UpdateArticleEndpoint(
        string id, ArticleInput input, ClaimsPrincipal principal, UpdateArticle handler,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ArticleWrite) is { } d) return d;

        var (details, errors) = input.Parse();
        if (details is null) return ApiProblem.Validation(Map(errors), http);

        var result = await handler.HandleAsync(new ArticleId(id), details, ct);
        return result.Match(Results.Ok, e => ApiProblem.From(e, http));
    }

    private static async Task<IResult> DeleteArticleEndpoint(
        string id, ClaimsPrincipal principal, DeleteArticle handler,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, PermissionKeys.ArticleWrite) is { } d) return d;
        var result = await handler.HandleAsync(new ArticleId(id), ct);
        return result.Match(_ => Results.NoContent(), e => ApiProblem.From(e, http));
    }

    private static async Task<IResult> ArticleTransition(
        string id, LibraryTransition transition, string permission, AuditAction? audited,
        ClaimsPrincipal principal, ChangeArticleStatus handler, IAuditLog audit, IClock clock,
        HttpContext http, CancellationToken ct)
    {
        if (Denied(principal, permission) is { } d) return d;

        var result = await handler.HandleAsync(new ArticleId(id), transition, ct);
        if (!result.IsSuccess) return ApiProblem.From(result.Error, http);

        var view = result.Value!;
        if (audited is { } action)
            await Record(audit, principal, action, "article", view.Id, view.Title, clock.UtcNow, ct,
                new Dictionary<string, string> { ["slug"] = view.Slug });

        return Results.Ok(view);
    }

    // ── Shared ─────────────────────────────────────────────────────────────

    private static IReadOnlyCollection<FieldError> Map(IReadOnlyList<LibraryFieldError> errors) =>
        [.. errors.Select(e => new FieldError(e.Path, e.Code, e.Message))];

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

    /// <summary>Read routes: either the write or the publish key opens the list.</summary>
    private static IResult? DeniedUnlessAny(ClaimsPrincipal principal, string first, string second)
    {
        if (principal.UserId() is null) return Results.Unauthorized();
        var held = principal.Permissions();
        return held.Contains(first) || held.Contains(second) ? null : Forbidden(first);
    }

    /// <summary>403 with a stable code, not 404 — the caller is a named operator. Same reasoning as <c>AdminEndpoints.Denied</c>.</summary>
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
}
