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
    ExamSubmitted,
    ExamWithdrawn,
    ExamReturned,
    ExamApproved,
    ExamUnapproved,
    ExamResumed,
    UserSuspended,
    UserReinstated,
    RoleAssigned,
    RoleRemoved,
}
