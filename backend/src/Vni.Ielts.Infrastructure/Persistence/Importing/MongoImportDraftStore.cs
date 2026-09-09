using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using PackageFinding = Vni.Ielts.Application.Importing.PackageFinding;

namespace Vni.Ielts.Infrastructure.Persistence.Importing;

/// <summary>
/// One <c>PackageFinding</c>, stored. Shared by <see cref="ExamImportDraftDocument"/>
/// and <see cref="ImportBatchCheckpointDocument"/> rather than duplicated —
/// both persist the same value shape.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class ImportFindingDocument
{
    [BsonElement("severity")] public string Severity { get; set; } = string.Empty;
    [BsonElement("code")] public string Code { get; set; } = string.Empty;
    [BsonElement("path")] public string Path { get; set; } = string.Empty;
    [BsonElement("message")] public string Message { get; set; } = string.Empty;

    public static ImportFindingDocument From(PackageFinding finding) => new()
    {
        Severity = finding.Severity, Code = finding.Code, Path = finding.Path, Message = finding.Message,
    };

    public PackageFinding ToFinding() => new(Severity, Code, Path, Message);
}

[BsonIgnoreExtraElements]
internal sealed class ImportWarningDocument
{
    [BsonElement("id")] public string Id { get; set; } = string.Empty;
    [BsonElement("category")] public string Category { get; set; } = string.Empty;
    [BsonElement("path")] public string Path { get; set; } = string.Empty;
    [BsonElement("message")] public string Message { get; set; } = string.Empty;
    [BsonElement("resolved")] public bool Resolved { get; set; }

    [BsonElement("overrideReason")]
    [BsonIgnoreIfNull]
    public string? OverrideReason { get; set; }
}

/// <summary>
/// The persistence model for <see cref="ExamImportDraft"/>.
///
/// <b>No domain aggregate is stored.</b> <c>ExamImportDraft.Version</c> is an
/// <see cref="ExamVersion"/> — a full domain aggregate with its own value
/// objects — and mapping that onto Bson attributes would be exactly the
/// persistence-attribute leak CLAUDE.md rule 7 exists to stop. Instead only
/// <c>PackageJson</c> is stored, and <c>Version</c> is reconstructed on read by
/// re-running it through the same <see cref="IExamPackageValidator"/> that
/// produced it in the first place — deterministic, and it is the one
/// validator every producer of an <see cref="ExamVersion"/> already goes
/// through (<see cref="Content.DevelopmentExamSeeder"/>'s own remarks call
/// this out as the constraint that keeps the seeder, the ZIP importer and
/// in-place CMS authoring from diverging).
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class ExamImportDraftDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonElement("definitionId")]
    public string DefinitionId { get; set; } = string.Empty;

    [BsonElement("versionNumber")]
    public int VersionNumber { get; set; }

    [BsonElement("route")]
    public string Route { get; set; } = string.Empty;

    [BsonElement("sourceHash")]
    public string SourceHash { get; set; } = string.Empty;

    [BsonElement("packageHash")]
    public string PackageHash { get; set; } = string.Empty;

    [BsonElement("parserProvider")] [BsonIgnoreIfNull] public string? ParserProvider { get; set; }
    [BsonElement("parserModel")] [BsonIgnoreIfNull] public string? ParserModel { get; set; }
    [BsonElement("parserPromptVersion")] [BsonIgnoreIfNull] public string? ParserPromptVersion { get; set; }
    [BsonElement("parserRequestId")] [BsonIgnoreIfNull] public string? ParserRequestId { get; set; }

    [BsonElement("approvalState")]
    public string ApprovalState { get; set; } = string.Empty;

    [BsonElement("findings")]
    public List<ImportFindingDocument> Findings { get; set; } = [];

    [BsonElement("sourceText")]
    public string SourceText { get; set; } = string.Empty;

    [BsonElement("packageJson")]
    public string PackageJson { get; set; } = string.Empty;

    [BsonElement("checklist")]
    public List<string> Checklist { get; set; } = [];

    [BsonElement("warnings")]
    public List<ImportWarningDocument> Warnings { get; set; } = [];

    [BsonElement("revision")]
    public int Revision { get; set; }

    [BsonElement("reviewedBy")]
    [BsonIgnoreIfNull]
    public string? ReviewedBy { get; set; }

    [BsonElement("checklistRequired")]
    [BsonIgnoreIfNull]
    public bool? ChecklistRequired { get; set; }

    [BsonElement("createdBy")]
    [BsonIgnoreIfNull]
    public string? CreatedBy { get; set; }

    [BsonElement("createdAt")]
    [BsonIgnoreIfNull]
    public DateTime? CreatedAt { get; set; }

    [BsonElement("examVersionId")]
    [BsonIgnoreIfNull]
    public string? ExamVersionId { get; set; }
}

/// <summary>
/// The Mongo half of the exam-import front door (S6b) — the piece that never
/// existed before this slice: <c>ExamImportWorkflow</c>, <c>ImportReviewWorkflow</c>
/// and <c>ImportBatchRunner</c> were all built against a fake in tests only.
///
/// <para>
/// <b><c>SaveAsync</c> is idempotent by construction, not by a read-then-write
/// check.</b> <see cref="ExamImportWorkflow.StableDraftId"/> derives the id
/// from <c>(definitionId, versionNumber, route, packageHash)</c>, so a second
/// import with byte-identical content collides on <c>_id</c> — that is exactly
/// what "idempotent for identical content" (the interface's own doc comment)
/// means. The duplicate-key error is swallowed as a replay.
/// </para>
///
/// <para>
/// <b>Deliberately not an upsert.</b> An upsert would silently overwrite
/// whatever review progress happened after the first save — a resolved
/// warning, a completed checklist, an approval — every time a retried upload
/// (or a batch re-run) replays the same content. A replay must be a no-op on
/// the stored draft, and "insert, ignore the duplicate key" is the only way to
/// get that without adding a read before the write, which would just move the
/// same race somewhere less visible.
/// </para>
///
/// <para>
/// <b><c>ReplaceAsync</c>'s compare-and-swap follows the same idiom as
/// <c>IExamSessionRepository.TrySaveAsync</c>:</b> the expected revision rides
/// in the filter, not in a separate read-then-check, so the write itself is
/// the atomic compare. <c>MatchedCount == 0</c> — not <c>ModifiedCount</c> —
/// is what answers "was the draft still at the revision the caller found it
/// in", for the same reason <c>TrySaveAsync</c>'s own remarks give: a
/// replacement that happens to be byte-identical to what was already there is
/// not a race, and reporting it as one would be answering a question that
/// was not asked.
/// </para>
/// </summary>
internal sealed class MongoImportDraftStore(MongoContext context, IExamPackageValidator validator)
    : IImportDraftStore
{
    public async Task SaveAsync(ExamImportDraft draft, CancellationToken ct)
    {
        try
        {
            await context.ImportDrafts.InsertOneAsync(ToDocument(draft), cancellationToken: ct);
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // A replay of the same import content — see the type's remarks.
        }
        catch (MongoCommandException e) when (e.Code == 11000)
        {
            // Same replay, surfaced through the command-error path some
            // driver/server combinations take instead of MongoWriteException.
        }
    }

    public async Task<ExamImportDraft?> FindAsync(Guid draftId, CancellationToken ct)
    {
        var doc = await context.ImportDrafts
            .Find(Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, draftId.ToString("D")))
            .FirstOrDefaultAsync(ct);

        return doc is null ? null : ToDraft(doc);
    }

    public async Task<bool> ReplaceAsync(ExamImportDraft draft, int expectedRevision, CancellationToken ct)
    {
        var filter = Builders<ExamImportDraftDocument>.Filter.And(
            Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Id, draft.Id.ToString("D")),
            Builders<ExamImportDraftDocument>.Filter.Eq(d => d.Revision, expectedRevision));

        var result = await context.ImportDrafts.ReplaceOneAsync(filter, ToDocument(draft), cancellationToken: ct);
        return result.MatchedCount > 0;
    }

    public async Task<IReadOnlyList<ExamImportDraft>> ListAsync(CancellationToken ct)
    {
        var docs = await context.ImportDrafts
            .Find(Builders<ExamImportDraftDocument>.Filter.Empty)
            .Sort(Builders<ExamImportDraftDocument>.Sort.Descending(d => d.CreatedAt).Descending(d => d.Id))
            .ToListAsync(ct);

        return [.. docs.Select(ToDraft)];
    }

    internal ExamImportDraft ToDraft(ExamImportDraftDocument doc)
    {
        var definitionId = new ExamDefinitionId(doc.DefinitionId);
        var validation = validator.Validate(doc.PackageJson, definitionId, doc.VersionNumber);

        if (!validation.IsValid || validation.Version is null)
        {
            // Nothing ever rewrites `packageJson` after it passed validation
            // once, so this can only mean the schema (or the validator) has
            // changed under a draft that already exists — a real data/schema
            // drift, not a race a caller can retry past.
            throw new InvalidOperationException(
                $"Import draft {doc.Id} no longer validates against its own stored package JSON. "
                + "The schema or validator changed since this draft was saved.");
        }

        var parsed = validation.Version;
        var version = string.IsNullOrEmpty(doc.ExamVersionId)
            ? parsed
            : ExamVersion.Rehydrate(
                new ExamVersionId(doc.ExamVersionId),
                parsed.DefinitionId,
                parsed.VersionNumber,
                parsed.Title,
                parsed.Variant,
                parsed.Status,
                parsed.PublishedAt,
                parsed.Scoring,
                parsed.Timing,
                parsed.Sections,
                parsed.ListeningPlayback,
                parsed.ModuleSequence,
                parsed.Description,
                parsed.AuthorId,
                parsed.ContentSourceId);

        return new ExamImportDraft(
            Guid.Parse(doc.Id),
            definitionId,
            doc.VersionNumber,
            Enum.Parse<ExamImportRoute>(doc.Route),
            doc.SourceHash,
            doc.PackageHash,
            version,
            doc.ParserProvider is null
                ? null
                : new ParserRunMetadata(
                    doc.ParserProvider, doc.ParserModel ?? "", doc.ParserPromptVersion ?? "",
                    doc.ParserRequestId ?? ""),
            Enum.Parse<ImportApprovalState>(doc.ApprovalState),
            doc.Findings.Select(f => f.ToFinding()).ToArray(),
            doc.SourceText,
            doc.PackageJson,
            new ImportReviewChecklist(doc.Checklist.Select(Enum.Parse<ImportReviewCategory>).ToHashSet()),
            doc.Warnings.Select(w => new ImportReviewWarning(
                w.Id, Enum.Parse<ImportReviewCategory>(w.Category), w.Path, w.Message, w.Resolved,
                w.OverrideReason)).ToArray(),
            doc.Revision,
            doc.ReviewedBy,
            doc.ChecklistRequired ?? true,
            doc.CreatedBy is { Length: > 0 } createdBy ? new UserId(createdBy) : null,
            doc.CreatedAt is { } createdAt
                ? new DateTimeOffset(DateTime.SpecifyKind(createdAt, DateTimeKind.Utc))
                : null);
    }

    internal static ExamImportDraftDocument ToDocument(ExamImportDraft draft) => new()
    {
        Id = draft.Id.ToString("D"),
        DefinitionId = draft.DefinitionId.Value,
        VersionNumber = draft.VersionNumber,
        Route = draft.Route.ToString(),
        SourceHash = draft.SourceHash,
        PackageHash = draft.PackageHash,
        ParserProvider = draft.Parser?.Provider,
        ParserModel = draft.Parser?.Model,
        ParserPromptVersion = draft.Parser?.PromptVersion,
        ParserRequestId = draft.Parser?.RequestId,
        ApprovalState = draft.ApprovalState.ToString(),
        Findings = draft.Findings.Select(ImportFindingDocument.From).ToList(),
        SourceText = draft.SourceText,
        PackageJson = draft.PackageJson,
        Checklist = draft.Checklist.Confirmed.Select(c => c.ToString()).ToList(),
        Warnings = draft.Warnings.Select(w => new ImportWarningDocument
        {
            Id = w.Id,
            Category = w.Category.ToString(),
            Path = w.Path,
            Message = w.Message,
            Resolved = w.Resolved,
            OverrideReason = w.OverrideReason,
        }).ToList(),
        Revision = draft.Revision,
        ReviewedBy = draft.ReviewedBy,
        ChecklistRequired = draft.ChecklistRequired,
        CreatedBy = draft.CreatedBy?.Value,
        CreatedAt = draft.CreatedAt?.UtcDateTime,
        ExamVersionId = draft.Version.Id.Value,
    };
}
