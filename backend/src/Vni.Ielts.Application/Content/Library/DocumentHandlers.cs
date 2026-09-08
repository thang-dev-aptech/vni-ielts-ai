using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Content.Library;

namespace Vni.Ielts.Application.Content.Library;

/// <summary>A per-field validation failure, reported all at once. The API maps these to its <c>errors[]</c>.</summary>
public sealed record LibraryFieldError(string Path, string Code, string Message);

/// <summary>
/// The four verbs of the shared lifecycle, as a caller names them. One
/// handler per library takes a verb rather than four handlers each, because
/// the four differ only in the guard and the target state.
/// </summary>
public enum LibraryTransition { Submit, Return, Publish, Unpublish }

/// <summary>
/// What the CMS sends for a document — strings where the domain has enums,
/// because the request is untrusted and every bad field should be reported
/// together. <see cref="Parse"/> is the one place that turns it into
/// <see cref="LibraryDocumentDetails"/>.
/// </summary>
public sealed record LibraryDocumentInput(
    string? Slug,
    string? Title,
    string? Description,
    string? Skill,
    string? Category,
    string? Type,
    string? Format,
    string? TargetBand,
    string? Topic,
    int? PageCount,
    string? Size,
    string? FileUrl,
    bool IsFeatured,
    bool IsNew,
    bool IsUpdated,
    bool IsPopular,
    string? Access)
{
    public (LibraryDocumentDetails? Details, IReadOnlyList<LibraryFieldError> Errors) Parse()
    {
        var errors = new List<LibraryFieldError>();

        void Require(string? value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add(new(path, "REQUIRED", $"{path} is required."));
        }

        Require(Title, "/title");
        Require(Size, "/size");
        if (!Domain.Content.Library.Slug.IsValid(Slug))
            errors.Add(new("/slug", "SLUG_INVALID", "Lowercase letters, digits and single hyphens only."));

        var skill = LibraryWire.ParseSkill(Skill);
        if (skill is null) errors.Add(new("/skill", "INVALID", "Unknown skill."));

        var type = LibraryWire.ParseType(Type);
        if (type is null) errors.Add(new("/type", "INVALID", "Unknown document type."));

        var format = LibraryWire.ParseFormat(Format);
        if (format is null) errors.Add(new("/format", "INVALID", "Format is PDF, DOCX or MP3."));

        var access = LibraryWire.ParseAccess(Access);
        if (access is null) errors.Add(new("/access", "INVALID", "Access is free or premium."));

        if (TargetBand is not null && !LibraryDocument.Bands.Contains(TargetBand))
            errors.Add(new("/targetBand", "INVALID", $"One of {string.Join(", ", LibraryDocument.Bands)}."));

        if (PageCount is < 1)
            errors.Add(new("/pageCount", "INVALID", "Page count must be positive."));

        if (errors.Count > 0) return (null, errors);

        return (new LibraryDocumentDetails(
            Slug!, Title!.Trim(), Description?.Trim() ?? string.Empty, skill!.Value,
            Category?.Trim() ?? LibraryWire.Skill(skill.Value), type!.Value, format!.Value,
            TargetBand, Topic?.Trim(), PageCount, Size!.Trim(), string.IsNullOrWhiteSpace(FileUrl) ? null : FileUrl.Trim(),
            IsFeatured, IsNew, IsUpdated, IsPopular, access!.Value), errors);
    }
}

// ── Learner side: published rows only ────────────────────────────────────

public sealed class ListLibraryDocuments(ILibraryDocumentStore store)
{
    public async Task<IReadOnlyList<LibraryDocumentView>> HandleAsync(
        LibraryDocumentFilter filter, CancellationToken ct) =>
        [.. (await store.ListPublishedAsync(filter, ct)).Select(LibraryDocumentView.Of)];
}

public sealed class GetLibraryDocument(ILibraryDocumentStore store)
{
    /// <summary>
    /// A draft answers 404, not 403: to a learner an unpublished document does
    /// not exist, and confirming that it does is an enumeration oracle.
    /// </summary>
    public async Task<Result<LibraryDocumentView>> HandleAsync(LibraryDocumentId id, CancellationToken ct)
    {
        var document = await store.FindAsync(id, ct);
        if (document is null || document.Status != LibraryContentStatus.Published)
            return Error.NotFound(ErrorCodes.NotFound, "No such document.");
        return LibraryDocumentView.Of(document);
    }
}

// ── CMS side: every status ───────────────────────────────────────────────

public sealed class ListAllLibraryDocuments(ILibraryDocumentStore store)
{
    public async Task<IReadOnlyList<LibraryDocumentView>> HandleAsync(CancellationToken ct) =>
        [.. (await store.ListAllAsync(ct)).Select(LibraryDocumentView.Of)];
}

public sealed class GetLibraryDocumentForEditing(ILibraryDocumentStore store)
{
    public async Task<Result<LibraryDocumentView>> HandleAsync(LibraryDocumentId id, CancellationToken ct)
    {
        var document = await store.FindAsync(id, ct);
        return document is null
            ? Error.NotFound(ErrorCodes.NotFound, "No such document.")
            : LibraryDocumentView.Of(document);
    }
}

public sealed class CreateLibraryDocument(ILibraryDocumentStore store, IClock clock)
{
    public async Task<LibraryDocumentView> HandleAsync(
        LibraryDocumentDetails details, UserId actor, CancellationToken ct)
    {
        var document = LibraryDocument.Create(details, actor, clock.UtcNow);
        await store.UpsertAsync(document, ct);
        return LibraryDocumentView.Of(document);
    }
}

public sealed class UpdateLibraryDocument(ILibraryDocumentStore store, IClock clock)
{
    public async Task<Result<LibraryDocumentView>> HandleAsync(
        LibraryDocumentId id, LibraryDocumentDetails details, CancellationToken ct)
    {
        var document = await store.FindAsync(id, ct);
        if (document is null) return Error.NotFound(ErrorCodes.NotFound, "No such document.");

        document.Update(details, clock.UtcNow);
        await store.UpsertAsync(document, ct);
        return LibraryDocumentView.Of(document);
    }
}

public sealed class DeleteLibraryDocument(ILibraryDocumentStore store)
{
    public async Task<Result<bool>> HandleAsync(LibraryDocumentId id, CancellationToken ct)
    {
        var document = await store.FindAsync(id, ct);
        if (document is null) return Error.NotFound(ErrorCodes.NotFound, "No such document.");

        if (!LibraryLifecycle.CanDelete(document.Status))
            return Error.Conflict(LibraryErrorCodes.ContentStatusConflict,
                "Gỡ tài liệu khỏi thư viện trước khi xoá.");

        await store.DeleteAsync(id, ct);
        return true;
    }
}

public sealed class ChangeLibraryDocumentStatus(ILibraryDocumentStore store, IClock clock)
{
    public async Task<Result<LibraryDocumentView>> HandleAsync(
        LibraryDocumentId id, LibraryTransition transition, CancellationToken ct)
    {
        var document = await store.FindAsync(id, ct);
        if (document is null) return Error.NotFound(ErrorCodes.NotFound, "No such document.");

        var now = clock.UtcNow;
        var allowed = transition switch
        {
            LibraryTransition.Submit => LibraryLifecycle.CanSubmit(document.Status),
            LibraryTransition.Return => LibraryLifecycle.CanReturn(document.Status),
            LibraryTransition.Publish => LibraryLifecycle.CanPublish(document.Status),
            LibraryTransition.Unpublish => LibraryLifecycle.CanUnpublish(document.Status),
            _ => false,
        };

        if (!allowed)
            return Error.Conflict(LibraryErrorCodes.ContentStatusConflict,
                $"Cannot {transition.ToString().ToLowerInvariant()} a document that is {LibraryWire.Status(document.Status)}.");

        switch (transition)
        {
            case LibraryTransition.Submit: document.Submit(now); break;
            case LibraryTransition.Return: document.ReturnToDraft(now); break;
            case LibraryTransition.Publish: document.Publish(now); break;
            case LibraryTransition.Unpublish: document.Unpublish(now); break;
        }

        await store.UpsertAsync(document, ct);
        return LibraryDocumentView.Of(document);
    }
}
