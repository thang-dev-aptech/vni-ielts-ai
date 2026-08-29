using System.Text.RegularExpressions;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Content;

/// <summary>
/// Where a source may be used.
///
/// <b>Three named places, not a boolean.</b> "Publishable" is not one
/// question: a paper that is fine to build screens against, fine to show a
/// reviewer inside VNI, and not fine to put in front of a paying learner is
/// the ordinary case, not an edge case — <c>exam/Exam1</c> is exactly that,
/// and its own README says so.
/// </summary>
public enum ContentEnvironment
{
    /// <summary>Development, demos and automated tests. Never a learner.</summary>
    Fixture,

    /// <summary>Shown to VNI staff for review. Still never a learner.</summary>
    InternalReview,

    /// <summary>
    /// A learner can be served this. <b>Nothing holds this right today</b> —
    /// <c>M-53</c> is open and the owner has not said which papers are cleared.
    /// → CLAUDE.md `G-11`
    /// </summary>
    LearnerProduction,
}

/// <summary>
/// A stable name a human chose for a source.
///
/// <b>Not derived from a path.</b> The VOL 9 folder carries a Google Drive
/// export stamp — <c>-20260819T082203Z-1-001</c> — inside a path segment, and
/// an id derived from that changes the moment anybody re-exports the folder.
/// A rights record whose key moves is a rights record nobody can find.
/// </summary>
public readonly record struct ContentSourceId
{
    private static readonly Regex Slug = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);

    public ContentSourceId(string value)
    {
        if (!Slug.IsMatch(value ?? string.Empty))
        {
            throw new ArgumentException(
                $"'{value}' is not a content source id. Ids are lowercase slugs — letters, "
                + "digits and hyphens, up to 64 characters — chosen by a person, never "
                + "derived from a filesystem path.",
                nameof(value));
        }

        Value = value!;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// The evidence behind a publication right: what the permission is, who
/// checked it, and when.
///
/// <b>A right with no proof is not expressible.</b> That is the structural
/// half of <c>M-53</c> — see <see cref="ContentSource.Register"/>.
/// </summary>
public sealed record RightsProof
{
    public RightsProof(string Reference, string Reviewer, DateTimeOffset ReviewedAt)
    {
        if (string.IsNullOrWhiteSpace(Reference))
            throw new ArgumentException("A rights proof needs a reference.", nameof(Reference));

        if (string.IsNullOrWhiteSpace(Reviewer))
            throw new ArgumentException("A rights proof needs a reviewer.", nameof(Reviewer));

        this.Reference = Reference.Trim();
        this.Reviewer = Reviewer.Trim();
        this.ReviewedAt = ReviewedAt;
    }

    /// <summary>Where the permission is written down — a contract id, a licence, an email.</summary>
    public string Reference { get; init; }

    /// <summary>The person who read it and said yes.</summary>
    public string Reviewer { get; init; }

    public DateTimeOffset ReviewedAt { get; init; }
}

/// <summary>
/// One file belonging to a source: where it is, and — when anybody has
/// computed one — what it hashed to.
/// </summary>
public sealed record ContentFileRef
{
    private static readonly Regex Sha256Hex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    public ContentFileRef(string RelativePath, string? Sha256, long? SizeBytes)
    {
        var normalised = (RelativePath ?? string.Empty).Replace('\\', '/').Trim();

        if (normalised.Length == 0)
            throw new ArgumentException("A file reference needs a path.", nameof(RelativePath));

        if (normalised.StartsWith('/') || normalised.Contains(':'))
        {
            throw new ArgumentException(
                $"'{RelativePath}' is rooted. File references are relative to the content root "
                + "so the registry survives being checked out anywhere.",
                nameof(RelativePath));
        }

        if (normalised.Split('/').Any(segment => segment is ".."))
        {
            throw new ArgumentException(
                $"'{RelativePath}' escapes the content root.", nameof(RelativePath));
        }

        if (Sha256 is not null && !Sha256Hex.IsMatch(Sha256))
        {
            throw new ArgumentException(
                "A recorded hash must be 64 lowercase hex characters, or absent. "
                + "Absent means nobody has computed one — it never means 'unchanged'.",
                nameof(Sha256));
        }

        this.RelativePath = normalised;
        this.Sha256 = Sha256;
        this.SizeBytes = SizeBytes;
    }

    /// <summary>Forward slashes, relative to the content root. Seeded on Windows, read on Linux.</summary>
    public string RelativePath { get; init; }

    /// <summary><c>null</c> when nobody has hashed it yet.</summary>
    public string? Sha256 { get; init; }

    public long? SizeBytes { get; init; }
}

/// <summary>An attempt to grant a publication right nobody can point at.</summary>
public sealed class UnprovenPublishRightException(ContentSourceId id)
    : InvalidOperationException(
        $"Source '{id}' cannot be granted {nameof(ContentEnvironment.LearnerProduction)} "
        + "without a rights proof — a reference to the permission and the person who "
        + "checked it. M-53 (which papers may be shown to a learner) is open; the registry "
        + "deliberately cannot express 'publishable, source unknown'.")
{
    public ContentSourceId SourceId { get; } = id;
}

/// <summary>
/// One body of source material and what VNI is allowed to do with it.
///
/// <b>A rights record, not a content record.</b> It holds paths and hashes and
/// never the bytes — the material itself is gitignored precisely because
/// nobody has established the right to redistribute it, and copying it into a
/// database would undo that.
///
/// <b>Carries no persistence attribute.</b> The Mongo document that stores it
/// lives in Infrastructure. → CLAUDE.md rule 7, ADR-0004
/// </summary>
public sealed class ContentSource
{
    private readonly HashSet<ContentEnvironment> _allowed;
    private readonly HashSet<string> _boundExamVersionIds;
    private readonly HashSet<string> _boundExamDefinitionIds;

    private ContentSource(
        ContentSourceId id, string title, string? owner, RightsProof? proof,
        IEnumerable<ContentEnvironment> allowedEnvironments, DateTimeOffset? expiresAt,
        string rootPath, IEnumerable<ContentFileRef> files,
        IEnumerable<ExamVersionId> boundExamVersionIds,
        IEnumerable<ExamDefinitionId> boundExamDefinitionIds)
    {
        Id = id;
        Title = string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("A source needs a title.", nameof(title))
            : title.Trim();
        Owner = string.IsNullOrWhiteSpace(owner) ? null : owner.Trim();
        Proof = proof;
        ExpiresAt = expiresAt;
        RootPath = (rootPath ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');
        Files = [.. files];
        _allowed = [.. allowedEnvironments];
        _boundExamVersionIds = [.. boundExamVersionIds.Select(v => v.Value)];
        _boundExamDefinitionIds = [.. boundExamDefinitionIds.Select(d => d.Value)];
    }

    public ContentSourceId Id { get; }

    public string Title { get; }

    /// <summary>
    /// Who the material belongs to, as far as anybody has recorded.
    /// <c>null</c> means <b>unknown</b>, which is a fact worth storing — it is
    /// not a synonym for "VNI".
    /// </summary>
    public string? Owner { get; }

    /// <summary><c>null</c> means no licence or permission has been recorded.</summary>
    public RightsProof? Proof { get; }

    /// <summary><c>null</c> means the recorded rights do not lapse on a date.</summary>
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>Where the material sits, relative to the content root. For inventory, not for identity.</summary>
    public string RootPath { get; }

    public IReadOnlyList<ContentFileRef> Files { get; }

    public IReadOnlySet<ContentEnvironment> AllowedEnvironments => _allowed;

    /// <summary>Particular exam versions built from this material.</summary>
    public IReadOnlyCollection<string> BoundExamVersionIds => _boundExamVersionIds;

    /// <summary>
    /// The exam <i>definitions</i> built from this material.
    ///
    /// <b>The durable half of the binding.</b> A version id follows the
    /// content — the seeder derives it from a fingerprint, so editing a paper
    /// mints a new one — and a rights record keyed only on version ids would
    /// quietly stop covering a paper the moment anybody corrected a typo in
    /// it. That failure looks like "publish suddenly refused", which is the
    /// safe direction, but it is still a registry that has lost track of its
    /// own material. The definition id survives the edit.
    /// </summary>
    public IReadOnlyCollection<string> BoundExamDefinitionIds => _boundExamDefinitionIds;

    /// <summary>
    /// Builds a record, refusing an unprovable publication right outright.
    /// </summary>
    public static ContentSource Register(
        ContentSourceId id, string title, string? owner, RightsProof? proof,
        IEnumerable<ContentEnvironment> allowedEnvironments, DateTimeOffset? expiresAt,
        string rootPath, IEnumerable<ContentFileRef> files,
        IEnumerable<ExamVersionId> boundExamVersionIds,
        IEnumerable<ExamDefinitionId> boundExamDefinitionIds)
    {
        var allowed = allowedEnvironments.ToArray();

        if (allowed.Contains(ContentEnvironment.LearnerProduction) && proof is null)
            throw new UnprovenPublishRightException(id);

        return new ContentSource(
            id, title, owner, proof, allowed, expiresAt, rootPath, files,
            boundExamVersionIds, boundExamDefinitionIds);
    }

    /// <summary>
    /// Rebuilds a record from storage <b>without</b> re-applying
    /// <see cref="Register"/>'s refusal.
    ///
    /// <para>
    /// A document that grants production with no proof can only have come from
    /// a hand edit or an older writer, and throwing here would take the whole
    /// listing down with it. <see cref="ContentRightsPolicy"/> refuses such a
    /// grant instead, so the corrupt row costs one refusal rather than an
    /// outage — and refusing is the safe direction.
    /// </para>
    /// </summary>
    public static ContentSource Rehydrate(
        ContentSourceId id, string title, string? owner, RightsProof? proof,
        IEnumerable<ContentEnvironment> allowedEnvironments, DateTimeOffset? expiresAt,
        string rootPath, IEnumerable<ContentFileRef> files,
        IEnumerable<ExamVersionId> boundExamVersionIds,
        IEnumerable<ExamDefinitionId> boundExamDefinitionIds) =>
        new(id, title, owner, proof, allowedEnvironments, expiresAt, rootPath, files,
            boundExamVersionIds, boundExamDefinitionIds);

    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt is { } end && now >= end;

    public bool Produced(ExamVersionId examVersionId) =>
        _boundExamVersionIds.Contains(examVersionId.Value);

    public bool Covers(ExamDefinitionId examDefinitionId) =>
        _boundExamDefinitionIds.Contains(examDefinitionId.Value);
}

/// <summary>Why a rights check said no.</summary>
public enum ContentRightsDenial
{
    /// <summary>
    /// Nothing in the registry covers this material.
    ///
    /// <b>The default, and the one that matters.</b> Presence of a file in the
    /// workspace grants nothing; an unregistered source is refused everywhere.
    /// </summary>
    NoRegistryEntry,

    /// <summary>Registered, but not for the environment being asked about.</summary>
    EnvironmentNotGranted,

    /// <summary>Granted once, and the recorded permission has lapsed.</summary>
    RightExpired,

    /// <summary>A stored grant with no licence or reviewer behind it. Never honoured.</summary>
    ProofMissing,
}

public sealed record ContentRightsDecision(
    bool Allowed, ContentRightsDenial? Denial, string? SourceId, string Explanation)
{
    public static ContentRightsDecision Permit(ContentSourceId id, ContentEnvironment environment) =>
        new(true, null, id.Value,
            $"Source '{id}' holds a recorded right for {Name(environment)}.");

    public static ContentRightsDecision Refuse(
        ContentRightsDenial denial, ContentSourceId? id, string explanation) =>
        new(false, denial, id?.Value, explanation);

    internal static string Name(ContentEnvironment environment) => environment switch
    {
        ContentEnvironment.Fixture => "fixture",
        ContentEnvironment.InternalReview => "internal-review",
        ContentEnvironment.LearnerProduction => "learner-production",
        _ => environment.ToString(),
    };
}

/// <summary>
/// The rights rule itself: one pure function, so the decision is testable
/// without a database, a clock or an HTTP pipeline.
///
/// <b><c>null</c> is a valid input and its answer is "no".</b> That is
/// deliberate and is the single most important line in this file — a source
/// with no registry entry must be treated as having no rights, never as
/// unrestricted. Making the empty state a parameter rather than an early
/// return at each call site means every caller gets it right by construction.
/// </summary>
public static class ContentRightsPolicy
{
    public static ContentRightsDecision Evaluate(
        ContentSource? source, ContentEnvironment environment, DateTimeOffset now)
    {
        var wanted = ContentRightsDecision.Name(environment);

        if (source is null)
        {
            return ContentRightsDecision.Refuse(
                ContentRightsDenial.NoRegistryEntry, null,
                $"No content rights entry covers this material, so it has no {wanted} right. "
                + "A file being present in the workspace is not permission to use it.");
        }

        if (!source.AllowedEnvironments.Contains(environment))
        {
            return ContentRightsDecision.Refuse(
                ContentRightsDenial.EnvironmentNotGranted, source.Id,
                $"Source '{source.Id}' is registered for "
                + $"{string.Join(", ", source.AllowedEnvironments.Select(ContentRightsDecision.Name).Order())} "
                + $"and not for {wanted}.");
        }

        if (source.IsExpiredAt(now))
        {
            return ContentRightsDecision.Refuse(
                ContentRightsDenial.RightExpired, source.Id,
                $"The recorded right for source '{source.Id}' expired on "
                + $"{source.ExpiresAt:yyyy-MM-dd}.");
        }

        if (environment == ContentEnvironment.LearnerProduction && source.Proof is null)
        {
            return ContentRightsDecision.Refuse(
                ContentRightsDenial.ProofMissing, source.Id,
                $"Source '{source.Id}' records a {wanted} grant with no licence reference or "
                + "reviewer behind it. A grant nobody can point at is not honoured.");
        }

        return ContentRightsDecision.Permit(source.Id, environment);
    }
}

/// <summary>What a look at the filesystem found for one recorded file.</summary>
/// <param name="Exists">False when the file is not there — the normal state in CI.</param>
/// <param name="Sha256">The hash actually computed, or null when nothing was read.</param>
public sealed record ContentFileObservation(bool Exists, string? Sha256);

public enum ContentFileState
{
    /// <summary>The recorded hash and the observed hash agree.</summary>
    Matches,

    /// <summary>The file is there and is not the file that was recorded.</summary>
    Changed,

    /// <summary>Not present, or nobody looked. Never treated as agreement.</summary>
    Missing,

    /// <summary>Present, and no hash was ever recorded to compare against.</summary>
    NotHashed,
}

public sealed record ContentFileVerification(
    string RelativePath, ContentFileState State, string? RecordedSha256, string? ObservedSha256);

public sealed record ContentIntegrityReport(
    string SourceId, IReadOnlyList<ContentFileVerification> Files)
{
    public bool AnyChanged => Files.Any(f => f.State == ContentFileState.Changed);

    public bool AnyMissing => Files.Any(f => f.State == ContentFileState.Missing);

    /// <summary>
    /// True only when every recorded file was found and matched a recorded
    /// hash. An empty file list is not "verified" — there was nothing to check.
    /// </summary>
    public bool FullyVerified =>
        Files.Count > 0 && Files.All(f => f.State == ContentFileState.Matches);
}

/// <summary>
/// Compares what was recorded with what is on disk. Pure — the reading of
/// files happens in Infrastructure and arrives here as observations.
/// </summary>
public static class ContentIntegrity
{
    public static ContentIntegrityReport Compare(
        ContentSource source, IReadOnlyDictionary<string, ContentFileObservation> observed)
    {
        var files = source.Files.Select(file =>
        {
            // No observation at all is silence, not agreement. A prober that
            // skipped a path has verified nothing about it.
            if (!observed.TryGetValue(file.RelativePath, out var seen) || !seen.Exists)
                return new ContentFileVerification(
                    file.RelativePath, ContentFileState.Missing, file.Sha256, null);

            if (file.Sha256 is null)
                return new ContentFileVerification(
                    file.RelativePath, ContentFileState.NotHashed, null, seen.Sha256);

            var state = string.Equals(file.Sha256, seen.Sha256, StringComparison.Ordinal)
                ? ContentFileState.Matches
                : ContentFileState.Changed;

            return new ContentFileVerification(
                file.RelativePath, state, file.Sha256, seen.Sha256);
        }).ToList();

        return new ContentIntegrityReport(source.Id.Value, files);
    }
}
