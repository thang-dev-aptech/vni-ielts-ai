using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// In-memory ports for the sitting lifecycle.
///
/// The answer sheet, marking store, rubric source and stub evaluator are the
/// ones in <c>Assessment/Fakes.cs</c> — deliberately reused rather than
/// duplicated, because these tests are about the two halves of marking meeting
/// in one pipeline and a second set of fakes would let them drift apart.
/// </summary>
internal sealed class FakeExamCatalogue(params ExamVersion[] versions) : IExamCatalogue
{
    private readonly List<ExamVersion> _versions = [.. versions];

    public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExamVersion>>([.. _versions.Where(v => v.IsSittable)]);

    public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExamVersion>>(_versions);

    public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
        Task.FromResult(_versions.FirstOrDefault(v => v.Id == id));

    public Task UpsertAsync(ExamVersion version, CancellationToken ct)
    {
        _versions.RemoveAll(v => v.Id == version.Id);
        _versions.Add(version);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Sittings held by reference.
///
/// <b>Every read is a fresh copy, because the version that handed back the same
/// object could not be raced.</b> It kept one instance per sitting, so a
/// handler that mutated the aggregate had already changed what the store would
/// hand to the next reader — including to its own re-read after a refused
/// write. A test for "the transition lost, so report what was actually stored"
/// then read back the very mutation that was refused, and agreed with the bug
/// it was written to catch.
///
/// Rehydrating on the way in and out costs four lines and makes the fake behave
/// the way the Mongo repository does: what you wrote is what is there, and what
/// you are holding is yours alone.
/// </summary>
internal sealed class FakeSessionRepository : IExamSessionRepository
{
    private readonly Dictionary<string, ExamSession> _sessions = [];

    /// <summary>
    /// The state each sitting was last <i>written</i> in.
    ///
    /// <b>Tracked separately because this fake hands back the same object it
    /// was given.</b> A handler mutates the aggregate and then asks to write
    /// it, so reading the guard off the stored reference would be reading it
    /// off the very change being written — the compare would pass for every
    /// caller, including one that had stopped guarding at all, and these tests
    /// would say the race was closed while it was open.
    /// </summary>
    private readonly Dictionary<string, SessionState> _states = [];

    public int Saves { get; private set; }

    /// <summary>
    /// Moves the sitting on behind the handler's back, the way another tab
    /// would. The handler is still holding the state it read; this is what it
    /// finds when it tries to write.
    /// </summary>
    public void MovedOn(ExamSessionId id, SessionState to) => _states[id.Value] = to;

    /// <summary>
    /// Another writer, arriving between the handler's read and its write.
    ///
    /// <b>Faithful where <see cref="MovedOn"/> is only indicative.</b> `MovedOn`
    /// rewrites the guard state and leaves the stored sitting alone, so a test
    /// using it describes a database that cannot exist. This runs a real
    /// transition against the store at the exact moment a handler is about to
    /// write, which is what a second tab does — and it fires once, so the
    /// handler's retry meets the state the interference produced rather than
    /// losing for ever.
    /// </summary>
    public Action? Interfere { get; set; }

    /// <summary>
    /// A detached copy, the way a document round trip produces one.
    ///
    /// <c>Rehydrate</c> is the aggregate's own reconstruction path, so this
    /// copies exactly what persistence would and nothing else — a copy built by
    /// hand would drift the first time the aggregate grew a field.
    /// </summary>
    private static ExamSession Copy(ExamSession session) =>
        ExamSession.Rehydrate(
            session.Id, session.UserId, session.ExamVersionId, session.Mode, session.Status,
            session.StartedAt, session.SubmittedAt,
            session.Attempts.Select(a => SectionAttempt.Rehydrate(
                a.Module, a.StartedAt, a.DeadlineAt, a.SubmittedAt,
                // <b>The stopwatch travels with the copy.</b> Left out, every
                // read would hand back an attempt paused at zero — so a handler
                // that paused, saved and re-read would see its own write undone
                // and these tests would agree that it worked.
                a.AccumulatedSeconds, a.RunningSince, a.TargetSeconds, a.PartId)),
            session.Timing,
            session.PracticeUnitId,
            session.PartIds);

    public Task<ExamSession?> FindAsync(ExamSessionId id, CancellationToken ct) =>
        Task.FromResult(
            _sessions.TryGetValue(id.Value, out var held) ? Copy(held) : null);

    public Task<ExamSession?> FindOpenForUserAsync(UserId userId, CancellationToken ct) =>
        Task.FromResult(
            _sessions.Values
                .FirstOrDefault(s => s.UserId == userId && s.Status == SessionStatus.InProgress)
                is { } open ? Copy(open) : null);

    public Task<IReadOnlyList<ExamSession>> ListForUserAsync(
        UserId userId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExamSession>>(
        [
            .. _sessions.Values
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.StartedAt)
                .Take(limit)
                .Select(Copy),
        ]);

    public Task AddAsync(ExamSession session, CancellationToken ct)
    {
        _sessions[session.Id.Value] = Copy(session);
        _states[session.Id.Value] = SessionState.Of(session);
        return Task.CompletedTask;
    }

    public Task<bool> TrySaveAsync(ExamSession session, SessionState from, CancellationToken ct)
    {
        if (Interfere is { } other)
        {
            Interfere = null;
            other();
        }

        if (!_states.TryGetValue(session.Id.Value, out var stored) || stored != from)
            return Task.FromResult(false);

        _sessions[session.Id.Value] = Copy(session);
        _states[session.Id.Value] = SessionState.Of(session);
        Saves++;

        return Task.FromResult(true);
    }
}

/// <summary>
/// Insert-if-absent, like the real one. A section is marked once, and a test
/// that let the second write win would hide a re-scoring bug rather than
/// catch it.
/// </summary>
internal sealed class FakeSectionResultStore : ISectionResultStore
{
    private readonly Dictionary<string, SectionScore> _scores = [];

    public int Writes { get; private set; }

    public Task SaveAsync(ExamSessionId sessionId, SectionScore score, CancellationToken ct)
    {
        Writes++;
        var key = $"{sessionId.Value}:{score.Module}";
        if (!_scores.ContainsKey(key)) _scores[key] = score;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SectionScore>> ListAsync(
        ExamSessionId sessionId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SectionScore>>(
            [.. _scores.Where(kv => kv.Key.StartsWith($"{sessionId.Value}:", StringComparison.Ordinal))
                .Select(kv => kv.Value)]);
}

/// <summary>
/// Speaking audio, kept as the bytes it was handed.
///
/// It records the session and question it was filed under, because the whole
/// point of the upload use case is that those are the server's answers rather
/// than the caller's.
/// </summary>
internal sealed class FakeRecordingStore : IRecordingStore
{
    public sealed record Stored(ExamSessionId SessionId, string QuestionId, string Id, long Bytes);

    public List<Stored> Saved { get; } = [];

    public async Task<string> SaveAsync(
        ExamSessionId sessionId, string questionId, Stream content, string contentType,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        var id = $"rec-{Saved.Count + 1}";
        Saved.Add(new Stored(sessionId, questionId, id, buffer.Length));
        return id;
    }

    /// <summary>
    /// <b>Modelled, because "no orphan is left behind" is the property.</b> A
    /// fake that ignored the delete would let a test assert the refusal and say
    /// nothing about the bytes — and the bytes are a learner's voice, which is
    /// personal data.
    /// </summary>
    public List<string> Deleted { get; } = [];

    public Task DeleteAsync(string recordingId, CancellationToken ct)
    {
        Deleted.Add(recordingId);
        Saved.RemoveAll(s => s.Id == recordingId);
        return Task.CompletedTask;
    }

    public Task DeleteForSessionAsync(ExamSessionId sessionId, CancellationToken ct)
    {
        foreach (var row in Saved.Where(s => s.SessionId == sessionId).ToList())
        {
            Deleted.Add(row.Id);
            Saved.Remove(row);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredRecording>> ListOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredRecording>>(
            [.. Saved
                .Take(limit)
                .Select(s => new StoredRecording(
                    s.Id, s.SessionId, s.QuestionId, DateTimeOffset.UnixEpoch))]);
}

/// <summary>
/// A clock the test moves.
///
/// <b>Not <c>FixedClock</c>, and the difference is the point of half these
/// tests.</b> A deadline is only interesting once time has passed it, and
/// nothing in the exam pipeline will let a caller say what time it is.
/// </summary>
internal sealed class MovableClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public void Advance(TimeSpan by) => UtcNow += by;
}
