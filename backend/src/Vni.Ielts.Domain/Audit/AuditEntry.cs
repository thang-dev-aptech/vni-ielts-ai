using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Domain.Audit;

/// <summary>
/// One administrative act, recorded.
///
/// <b>Append-only, and the type enforces it.</b> Every property is
/// get-only and there is no mutating method — not because a setter would be
/// inconvenient, but because an audit trail that can be edited is not an audit
/// trail. Threat `T21` is an operator covering their own action; the defence
/// starts here and continues in a repository with no update or delete.
///
/// <b>It records what was decided, not what the request looked like.</b> No
/// headers, no request body, no IP. An audit log that accumulates personal
/// data becomes a second thing to protect under PDPL, and the question it
/// exists to answer — "who published this exam, and when" — needs none of it.
/// </summary>
public sealed record AuditEntry(
    string Id,
    DateTimeOffset At,
    UserId ActorId,
    string ActorEmail,
    AuditAction Action,
    /// <summary>What was acted on: an exam version id, a user id.</summary>
    string TargetType,
    string TargetId,
    /// <summary>A human-readable label so the log reads without a second lookup.</summary>
    string TargetLabel,
    /// <summary>
    /// Anything that would otherwise need the reader to guess — the previous
    /// status, the role granted. Short strings only; this is not a diff.
    /// </summary>
    IReadOnlyDictionary<string, string> Detail)
{
    public static AuditEntry Record(
        UserId actorId, string actorEmail, AuditAction action,
        string targetType, string targetId, string targetLabel,
        DateTimeOffset now, IReadOnlyDictionary<string, string>? detail = null) =>
        new(Guid.NewGuid().ToString("n"), now, actorId, actorEmail, action,
            targetType, targetId, targetLabel, detail ?? new Dictionary<string, string>());
}

/// <summary>
/// A closed set, not free text.
///
/// A log filtered by a string somebody typed is a log nobody can filter: two
/// spellings of "publish" and the history splits in half. Adding a value here
/// is a deliberate act, which is the point.
/// </summary>
public enum AuditAction
{
    ExamPublished,
    ExamUnpublished,
    UserSuspended,
    UserReinstated,

    /// <summary>
    /// An operator set another account's password.
    ///
    /// <b>The detail carries no password and never will.</b> It records that
    /// the act happened and to whom — which is the whole point, because this is
    /// the one action that lets one person sign in as another. Without a row
    /// here, an operator who takes over an account leaves the account's own
    /// activity looking like the account holder's. → threat `T21`
    /// </summary>
    UserPasswordReset,

    RoleAssigned,
    RoleRemoved,

    /// <summary>
    /// Speaking audio removed because an account or attempt was deleted.
    /// Detail carries recording/session/question ids only — never a URL.
    /// </summary>
    SpeakingRecordingPurged,

    // The two libraries (P-22). Target type is "article" / "library-document",
    // target id the record id, label the title; detail carries `slug` and
    // the previous status.
    ArticlePublished,
    ArticleUnpublished,
    DocumentPublished,
    DocumentUnpublished,

    // The exam review lifecycle (P-20). Target type "exam-version".
    /// <summary>Draft → InReview.</summary>
    ExamSubmittedForReview,
    /// <summary>InReview → Approved, by someone other than the author.</summary>
    ExamApproved,
    /// <summary>InReview → Draft. Detail carries `reason`.</summary>
    ExamReturnedToDraft,

    /// <summary>
    /// An operator proceeded past a non-blocking warning instead of fixing
    /// it. First needed by `P-19` ("cảnh báo được phép tồn tại" on an exam
    /// version entering review); deliberately generic rather than
    /// `ExamWarningOverridden`, because `S6`/`T8` reuses this exact value for
    /// ZIP import warnings against a target type of "import-draft" — one
    /// value, two target types, no merge conflict on this enum between the
    /// two slices.
    /// </summary>
    WarningOverridden,

    // The CMS media library. Target type "media-asset", target id the media
    // id, label the file name; detail carries `kind`, `bytes` and `checksum`.
    /// <summary>A file was stored into the library.</summary>
    MediaUploaded,
    /// <summary>An asset was withdrawn from the upload pickers; it still resolves.</summary>
    MediaRetired,
    /// <summary>An asset and its stored bytes were removed. Detail carries `fileName`.</summary>
    MediaDeleted,

    // Package-import lifecycle. Target type "package-import-history" (or
    // "import-draft" for approval / warning override). Detail carries
    // identifiers, counts and finding *codes* — never archive bytes, package
    // JSON, finding messages or an archive key.
    //
    // Vietnamese CMS labels for these values are owned by
    // admin-shared-contracts (AuditPage), not this enum.
    /// <summary>The door accepted a ZIP and enqueued an import.</summary>
    PackageUploadAccepted,
    /// <summary>The door refused a ZIP before archive persistence.</summary>
    PackageUploadRejected,
    /// <summary>The worker refused a parked archive after validation.</summary>
    PackageImportRejected,
    /// <summary>An import draft was approved for review publication.</summary>
    PackageImportApproved,
    /// <summary>
    /// An admin placed or moved hotspot positions on a group's image. Detail
    /// carries the group id and a position count only — never coordinate
    /// values or option text.
    /// </summary>
    GroupPositionsSet,
    /// <summary>
    /// An admin confirmed (or unconfirmed) review-checklist categories on an
    /// import draft. Detail carries the draft id and a confirmed count only.
    /// </summary>
    ImportChecklistConfirmed,

    // Evaluation operations are deliberately distinct from a normal score
    // read: the former grants access to learner/model content and the latter
    // can spend provider money. Details stay identifier/count metadata only.
    /// <summary>Protected learner submission or evaluator output was viewed.</summary>
    EvaluationContentAccessed,
    /// <summary>A failed evaluator job was reopened for another provider call.</summary>
    EvaluationRerunRequested,
}
