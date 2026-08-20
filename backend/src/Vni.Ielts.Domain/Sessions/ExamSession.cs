using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Sessions;

public readonly record struct ExamSessionId(string Value)
{
    public override string ToString() => Value;
    public static ExamSessionId New() => new(Guid.NewGuid().ToString("n"));
}

/// <summary>
/// Full Test and Single Skill are the same entity in a different shape — one
/// session with four section attempts, or one with a single attempt that never
/// auto-advances. They do not need two entities, but they are <b>not</b>
/// interchangeable: "Tiếp theo" continues an attempt, "làm đề mới" starts and
/// charges for a new one. → `E-11`…`E-13`, CLAUDE.md rule 10
/// </summary>
public enum SessionMode { Full, Single }

public enum SessionStatus { InProgress, Submitted, Expired, Abandoned }

/// <summary>
/// One sitting by one learner against one exam version.
///
/// <b>Every timestamp on this aggregate comes from the server clock.</b> The
/// client never supplies one, and no method here accepts a client-provided
/// time — a client-controlled timer can be paused, reset or rewound by anyone
/// with developer tools, and unlimited time invalidates every score the
/// platform produces. → ADR-0007, threat `T6`
/// </summary>
public sealed class ExamSession
{
    private readonly List<SectionAttempt> _attempts;

    private ExamSession(
        ExamSessionId id, UserId userId, ExamVersionId examVersionId, SessionMode mode,
        SessionStatus status, DateTimeOffset startedAt, DateTimeOffset? submittedAt,
        List<SectionAttempt> attempts)
    {
        Id = id; UserId = userId; ExamVersionId = examVersionId; Mode = mode;
        Status = status; StartedAt = startedAt; SubmittedAt = submittedAt; _attempts = attempts;
    }

    public ExamSessionId Id { get; }
    public UserId UserId { get; }
    public ExamVersionId ExamVersionId { get; }
    public SessionMode Mode { get; }
    public SessionStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? SubmittedAt { get; private set; }

    public IReadOnlyList<SectionAttempt> Attempts => _attempts;

    public SectionAttempt? Current =>
        _attempts.FirstOrDefault(a => a.SubmittedAt is null);

    public SectionAttempt? AttemptFor(ExamModule module) =>
        _attempts.FirstOrDefault(a => a.Module == module);

    /// <summary>
    /// Starts a session and opens its first section.
    ///
    /// Both deadlines are derived here from <paramref name="now"/> and the
    /// version's timing profile. Nothing about them is negotiable by a caller.
    /// </summary>
    public static ExamSession Start(
        UserId userId, ExamVersion version, SessionMode mode, ExamModule firstModule,
        DateTimeOffset now)
    {
        if (!version.IsSittable)
            throw new InvalidOperationException(
                "Only a published exam version can be sat. A draft has not been reviewed.");

        if (version.Section(firstModule) is null)
            throw new InvalidOperationException($"This version has no {firstModule} section.");

        var attempt = SectionAttempt.Open(firstModule, now, version.Timing.DurationFor(firstModule));

        return new ExamSession(
            ExamSessionId.New(), userId, version.Id, mode,
            SessionStatus.InProgress, now, null, [attempt]);
    }

    public static ExamSession Rehydrate(
        ExamSessionId id, UserId userId, ExamVersionId examVersionId, SessionMode mode,
        SessionStatus status, DateTimeOffset startedAt, DateTimeOffset? submittedAt,
        IEnumerable<SectionAttempt> attempts) =>
        new(id, userId, examVersionId, mode, status, startedAt, submittedAt, [.. attempts]);

    /// <summary>
    /// Closes the current section and opens the next one in a Full Test.
    ///
    /// Three things this must never do, each a real exploit if got wrong:
    /// <list type="number">
    /// <item><b>Never let the caller choose the next module.</b> The order is a
    /// property of the version, derived here. A client-supplied "next" is a way
    /// to skip Writing.</item>
    /// <item><b>Never carry the previous deadline forward.</b> Each attempt gets
    /// a fresh server-derived deadline; reusing the session's would silently
    /// shorten every later section.</item>
    /// <item><b>Never do this for a Single Skill session.</b> Its call to
    /// action is "làm đề mới", which is a different operation with a different
    /// entitlement effect.</item>
    /// </list>
    /// </summary>
    public AdvanceOutcome AdvanceToNextSection(ExamVersion version, DateTimeOffset now)
    {
        if (Status != SessionStatus.InProgress)
            return AdvanceOutcome.SessionNotInProgress;

        if (Mode != SessionMode.Full)
            return AdvanceOutcome.NotAFullTest;

        var current = Current;
        if (current is null)
            return AdvanceOutcome.NoOpenSection;

        current.Submit(now);

        var next = version.NextModuleAfter(current.Module);
        if (next is null)
        {
            Status = SessionStatus.Submitted;
            SubmittedAt = now;
            return AdvanceOutcome.SessionComplete;
        }

        _attempts.Add(SectionAttempt.Open(next.Value, now, version.Timing.DurationFor(next.Value)));
        return AdvanceOutcome.Advanced;
    }

    /// <summary>
    /// Submits the session.
    ///
    /// Deliberately <b>does not</b> reject a late submission — that decision
    /// belongs to the application layer, which must distinguish "the learner
    /// pressed submit after the deadline" (reject) from "the deadline passed
    /// and the server is closing the session" (accept, mark expired). Folding
    /// both into the entity would lose that distinction.
    /// </summary>
    public void Submit(DateTimeOffset now)
    {
        if (Status != SessionStatus.InProgress)
            throw new InvalidOperationException($"This session is already {Status}.");

        foreach (var attempt in _attempts.Where(a => a.SubmittedAt is null))
            attempt.Submit(now);

        Status = SessionStatus.Submitted;
        SubmittedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status != SessionStatus.InProgress) return;

        foreach (var attempt in _attempts.Where(a => a.SubmittedAt is null))
            attempt.Submit(now);

        Status = SessionStatus.Expired;
        SubmittedAt = now;
    }

    /// <summary>
    /// Whether the currently open section is still within its deadline,
    /// according to the <b>server's</b> clock.
    /// </summary>
    public bool IsWithinDeadline(DateTimeOffset serverNow) =>
        Current is { } current && serverNow <= current.DeadlineAt;
}

public enum AdvanceOutcome
{
    Advanced,
    SessionComplete,
    NotAFullTest,
    NoOpenSection,
    SessionNotInProgress,
}

/// <summary>
/// One module inside a sitting, with <b>its own</b> deadline. IELTS modules
/// are timed independently, so a single session-level deadline cannot express
/// the rule.
/// </summary>
public sealed class SectionAttempt
{
    private SectionAttempt(
        ExamModule module, DateTimeOffset startedAt, DateTimeOffset deadlineAt,
        DateTimeOffset? submittedAt)
    {
        Module = module; StartedAt = startedAt; DeadlineAt = deadlineAt; SubmittedAt = submittedAt;
    }

    public ExamModule Module { get; }
    public DateTimeOffset StartedAt { get; }

    /// <summary>Derived on the server as <c>StartedAt + timingProfile</c>. Never client-supplied.</summary>
    public DateTimeOffset DeadlineAt { get; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    public static SectionAttempt Open(ExamModule module, DateTimeOffset now, TimeSpan duration) =>
        new(module, now, now + duration, null);

    public static SectionAttempt Rehydrate(
        ExamModule module, DateTimeOffset startedAt, DateTimeOffset deadlineAt,
        DateTimeOffset? submittedAt) =>
        new(module, startedAt, deadlineAt, submittedAt);

    public void Submit(DateTimeOffset now) => SubmittedAt ??= now;

    public bool IsWithinDeadline(DateTimeOffset serverNow) => serverNow <= DeadlineAt;
}
