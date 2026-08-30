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
/// Whether the clock is a limit or a stopwatch.
///
/// <b>A second axis, not a member of <see cref="SessionMode"/>.</b> Mode says
/// how much of the paper is being sat — the whole thing or one skill. This says
/// whether the sitting is bounded. All four combinations mean something, and
/// folding them into one enum would produce a list where "Full" and "Practice"
/// sit side by side as though they answered the same question.
///
/// <b>Not a <c>bool IsPractice</c> either.</b> A boolean names the product
/// label; this names the rule that actually varies. "Luyện đề" and "thi thử"
/// are what the owner calls them, and product labels move — the rule that a
/// sitting either has a server-enforced deadline or does not is the thing the
/// engine branches on.
///
/// <b>Recorded on the sitting and never looked up from configuration.</b> The
/// same reason the band table is versioned: a score earned untimed and pausable
/// is not comparable to one earned under exam conditions, and a sitting that
/// cannot say which it was is a result nobody can interpret later.
/// </summary>
public enum SessionTiming
{
    /// <summary>Thi thử. The server derives a deadline and refuses late writes. → ADR-0007</summary>
    Deadline,

    /// <summary>Luyện đề. No deadline, and the clock can be paused. → owner instruction 27/08/2026</summary>
    OpenEnded,
}

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
        SessionTiming timing, SessionStatus status, DateTimeOffset startedAt,
        DateTimeOffset? submittedAt, List<SectionAttempt> attempts,
        string? practiceUnitId = null, IReadOnlyList<string>? partIds = null)
    {
        Id = id; UserId = userId; ExamVersionId = examVersionId; Mode = mode; Timing = timing;
        Status = status; StartedAt = startedAt; SubmittedAt = submittedAt; _attempts = attempts;
        PracticeUnitId = practiceUnitId; PartIds = partIds ?? [];
    }

    public ExamSessionId Id { get; }
    public UserId UserId { get; }
    public ExamVersionId ExamVersionId { get; }
    public string? PracticeUnitId { get; }
    public IReadOnlyList<string> PartIds { get; }
    public SessionMode Mode { get; }

    /// <summary>
    /// Whether this sitting is bounded by a deadline the server enforces, or is
    /// an open-ended stopwatch the learner can pause. → <see cref="SessionTiming"/>
    /// </summary>
    public SessionTiming Timing { get; }
    public SessionStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? SubmittedAt { get; private set; }

    public IReadOnlyList<SectionAttempt> Attempts => _attempts;

    public SectionAttempt? Current =>
        _attempts.FirstOrDefault(a => a.SubmittedAt is null);

    public SectionAttempt? AttemptFor(ExamModule module) =>
        _attempts.FirstOrDefault(a => a.Module == module);

    public string? CurrentPartId => Current?.PartId;
    public IReadOnlyList<string> CompletedPartIds =>
        [.. _attempts.Where(a => a.SubmittedAt is not null && a.PartId is not null).Select(a => a.PartId!)];

    /// <summary>
    /// Starts a session and opens its first section.
    ///
    /// Both deadlines are derived here from <paramref name="now"/> and the
    /// version's timing profile. Nothing about them is negotiable by a caller.
    /// </summary>
    /// <param name="targetSeconds">
    /// The learner's own goal for an open-ended sitting. Ignored for a
    /// deadlined one, where the paper's timing profile decides and nothing a
    /// caller sends may touch it.
    /// </param>
    public static ExamSession Start(
        UserId userId, ExamVersion version, SessionMode mode, SessionTiming timing,
        ExamModule firstModule, DateTimeOffset now, int? targetSeconds = null,
        string? practiceUnitId = null, IReadOnlyList<string>? partIds = null)
    {
        if (!version.IsSittable)
            throw new InvalidOperationException(
                "Only a published exam version can be sat. A draft has not been reviewed.");

        if (version.Section(firstModule) is null)
            throw new InvalidOperationException($"This version has no {firstModule} section.");

        var firstPart = mode == SessionMode.Single
            ? partIds?.FirstOrDefault(id => id.StartsWith(
                $"{firstModule.ToString().ToLowerInvariant()}-part-", StringComparison.Ordinal))
            : null;
        var attempt = timing == SessionTiming.OpenEnded
            ? SectionAttempt.OpenEnded(firstModule, now, targetSeconds, firstPart)
            : SectionAttempt.Open(firstModule, now, version.Timing.DurationFor(firstModule), firstPart);

        return new ExamSession(
            ExamSessionId.New(), userId, version.Id, mode, timing,
            SessionStatus.InProgress, now, null, [attempt], practiceUnitId, partIds);
    }

    public static ExamSession Rehydrate(
        ExamSessionId id, UserId userId, ExamVersionId examVersionId, SessionMode mode,
        SessionStatus status, DateTimeOffset startedAt, DateTimeOffset? submittedAt,
        IEnumerable<SectionAttempt> attempts, SessionTiming timing = SessionTiming.Deadline,
        string? practiceUnitId = null, IReadOnlyList<string>? partIds = null) =>
        new(id, userId, examVersionId, mode, timing, status, startedAt, submittedAt,
            [.. attempts], practiceUnitId, partIds);

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

        _attempts.Add(
            Timing == SessionTiming.OpenEnded
                ? SectionAttempt.OpenEnded(next.Value, now, null)
                : SectionAttempt.Open(next.Value, now, version.Timing.DurationFor(next.Value)));

        return AdvanceOutcome.Advanced;
    }

    public PartAdvanceOutcome AdvanceToNextPart(ExamVersion version, DateTimeOffset now)
    {
        if (Status != SessionStatus.InProgress) return PartAdvanceOutcome.SessionNotInProgress;
        var current = Current;
        if (PracticeUnitId is null || current?.PartId is null || PartIds.Count == 0)
            return PartAdvanceOutcome.NotPartScoped;

        current.Submit(now);
        var moduleParts = PartIds.Where(id => id.StartsWith(
            $"{current.Module.ToString().ToLowerInvariant()}-part-", StringComparison.Ordinal)).ToArray();
        var index = Array.IndexOf(moduleParts, current.PartId);
        if (index < 0 || index + 1 >= moduleParts.Length)
        {
            Status = SessionStatus.Submitted;
            SubmittedAt = now;
            return PartAdvanceOutcome.ScopeComplete;
        }

        var nextPartId = moduleParts[index + 1];
        var partOrder = int.Parse(nextPartId[(nextPartId.LastIndexOf('-') + 1)..]);
        var section = version.Section(current.Module)!;
        var part = section.Parts.Single(p => p.Order == partOrder);
        var duration = TimeSpan.FromSeconds(part.Timing?.DurationSeconds
            ?? (int)version.Timing.DurationFor(current.Module).TotalSeconds / section.Parts.Count);
        _attempts.Add(Timing == SessionTiming.OpenEnded
            ? SectionAttempt.OpenEnded(current.Module, now, current.TargetSeconds, nextPartId)
            : SectionAttempt.Open(current.Module, now, duration, nextPartId));
        return PartAdvanceOutcome.Advanced;
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
    /// Whether the open section's deadline has passed, according to the
    /// <b>server's</b> clock.
    ///
    /// <b>Three answers, not two, and that is the correction.</b> This used to
    /// be <c>IsWithinDeadline</c>, whose <c>false</c> meant either "the
    /// deadline has passed" or "there is no open section at all" — and the
    /// expiry sweep read both as "close it". That conflation was survivable
    /// while every attempt had a deadline. It stopped being survivable the
    /// moment one could have none: an untimed sitting would compare
    /// <c>serverNow &lt;= null</c>, get false, and be closed the first time
    /// anybody looked at it.
    ///
    /// So the question is asked in the positive and the two other cases are
    /// separated: <see cref="Current"/> is null when there is no open section,
    /// which is a different repair and still needs doing.
    /// </summary>
    public bool IsPastDeadline(DateTimeOffset serverNow) =>
        Current is { } current && current.IsPastDeadline(serverNow);
}

public enum AdvanceOutcome
{
    Advanced,
    SessionComplete,
    NotAFullTest,
    NoOpenSection,
    SessionNotInProgress,
}

public enum PartAdvanceOutcome
{
    Advanced,
    ScopeComplete,
    NotPartScoped,
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
        ExamModule module, DateTimeOffset startedAt, DateTimeOffset? deadlineAt,
        DateTimeOffset? submittedAt, int accumulatedSeconds, DateTimeOffset? runningSince,
        int? targetSeconds, string? partId = null)
    {
        Module = module; StartedAt = startedAt; DeadlineAt = deadlineAt; SubmittedAt = submittedAt;
        AccumulatedSeconds = accumulatedSeconds; RunningSince = runningSince;
        TargetSeconds = targetSeconds;
        PartId = partId;
    }

    public ExamModule Module { get; }
    public string? PartId { get; }
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// Derived on the server as <c>StartedAt + timingProfile</c>. Never
    /// client-supplied.
    ///
    /// <b>Null for an open-ended sitting, and null is not "no limit yet".</b>
    /// It is the whole difference between luyện đề and thi thử, so every read
    /// of it has to decide what null means rather than fall through a
    /// comparison. <c>serverNow &lt;= null</c> is false in C#, which would make
    /// an untimed sitting read as overdue and hand it straight to the expiry
    /// sweep — see <see cref="IsPastDeadline"/> for why the question is now
    /// asked the other way round.
    /// </summary>
    public DateTimeOffset? DeadlineAt { get; }

    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>
    /// Seconds this attempt has been running, over all the intervals that have
    /// already closed.
    ///
    /// <b>An accumulator rather than an event log.</b> A list of pause and
    /// resume events would be an unbounded array on a document read on every
    /// autosave, and elapsed time is wanted on every one of those reads. The
    /// honest cost is that an accumulator cannot be re-derived if it is ever
    /// corrupted; a log could. That trade is taken deliberately.
    /// </summary>
    public int AccumulatedSeconds { get; private set; }

    /// <summary>
    /// The server clock at the last resume, or null while paused.
    ///
    /// <b>The client never sends a time, only an intent.</b> That is what keeps
    /// a pausable stopwatch from being a hole in ADR-0007: a caller can ask to
    /// pause and ask to resume, and cannot say when either happened. The most a
    /// determined learner gets from tampering is a stopwatch that reads low,
    /// which is a self-assessment they have spoiled for themselves.
    /// </summary>
    public DateTimeOffset? RunningSince { get; private set; }

    /// <summary>
    /// The time the learner said they were aiming for, in seconds.
    ///
    /// <b>Display only. No rule may read this.</b> The moment anything branches
    /// on it, it has stopped being a goal and become a deadline, and luyện đề
    /// has quietly become the exam — with the difference that this deadline is
    /// one the learner chose and can change. It is stored so the review screen
    /// can say "40 phút, bạn làm 47" and for nothing else.
    /// </summary>
    public int? TargetSeconds { get; private set; }

    public static SectionAttempt Open(
        ExamModule module, DateTimeOffset now, TimeSpan duration, string? partId = null) =>
        new(module, now, now + duration, null, 0, now, null, partId);

    /// <summary>
    /// An attempt with no deadline, running from the moment it opened.
    ///
    /// It starts running rather than paused: a learner who pressed "bắt đầu" is
    /// working, and making them press play as well would be a stopwatch that
    /// lies about the first thing they did.
    /// </summary>
    public static SectionAttempt OpenEnded(
        ExamModule module, DateTimeOffset now, int? targetSeconds, string? partId = null) =>
        new(module, now, null, null, 0, now, targetSeconds, partId);

    public static SectionAttempt Rehydrate(
        ExamModule module, DateTimeOffset startedAt, DateTimeOffset? deadlineAt,
        DateTimeOffset? submittedAt, int accumulatedSeconds = 0,
        DateTimeOffset? runningSince = null, int? targetSeconds = null, string? partId = null) =>
        new(module, startedAt, deadlineAt, submittedAt, accumulatedSeconds, runningSince,
            targetSeconds, partId);

    /// <summary>
    /// Closes the attempt, and stops the clock with it.
    ///
    /// <b>The stopwatch has to be closed here or elapsed keeps growing after
    /// submission.</b> A learner who submits and comes back to the review
    /// screen an hour later would otherwise be told they took an hour longer
    /// than they did.
    /// </summary>
    public void Submit(DateTimeOffset now)
    {
        if (SubmittedAt is not null) return;

        Pause(now);
        SubmittedAt = now;
    }

    /// <summary>
    /// Stops the clock. Idempotent — pausing a paused attempt is not an error,
    /// because two tabs pressing pause together is an ordinary thing and
    /// neither of them should see a failure for it.
    /// </summary>
    public void Pause(DateTimeOffset now)
    {
        if (RunningSince is not { } since) return;

        // Never negative. A clock that stepped backwards between resume and
        // pause must not subtract from what was already counted.
        var ran = (int)Math.Max(0, (now - since).TotalSeconds);

        AccumulatedSeconds += ran;
        RunningSince = null;
    }

    /// <summary>
    /// Starts the clock again. Idempotent, and refused once the attempt is
    /// closed — a submitted paper does not resume.
    /// </summary>
    public void Resume(DateTimeOffset now)
    {
        if (SubmittedAt is not null) return;
        if (RunningSince is not null) return;

        RunningSince = now;
    }

    /// <summary>The learner's own goal. Advisory, and it may be cleared.</summary>
    public void AimFor(int? seconds) =>
        TargetSeconds = seconds is > 0 ? seconds : null;

    /// <summary>
    /// How long this attempt has been worked on, by the server's clock.
    /// </summary>
    public int ElapsedSeconds(DateTimeOffset serverNow) =>
        AccumulatedSeconds
        + (RunningSince is { } since ? (int)Math.Max(0, (serverNow - since).TotalSeconds) : 0);

    /// <summary>
    /// Whether this attempt's deadline has passed.
    ///
    /// <b>Asked in the positive, where it used to be asked in the negative.</b>
    /// <c>IsWithinDeadline</c> returned false for two unrelated situations —
    /// the deadline has passed, and there is no deadline at all — and callers
    /// treated the false as "sweep it". With an open-ended attempt that is not
    /// an edge case: it would close every practice sitting the instant anybody
    /// looked at one.
    ///
    /// An attempt with no deadline is never past it. That is the whole rule,
    /// and phrasing it this way is what makes it impossible to get wrong by
    /// accident.
    /// </summary>
    public bool IsPastDeadline(DateTimeOffset serverNow) =>
        DeadlineAt is { } deadline && serverNow > deadline;
}
