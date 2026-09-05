using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Closing a section, when two callers try to close it at once.
///
/// <b>Against a real server, because the guard is a query and only a query
/// engine can lose a race.</b> An in-memory fake compares two values under one
/// lock and always produces a single winner, whatever the filter says — so a
/// filter that had stopped naming the open section, or that matched a missing
/// field the wrong way, would pass every unit test in the suite and let both
/// callers through in production.
///
/// The thing at stake is not a corrupt document. Two racing advances mostly
/// write the same document. It is that both of them go on to <i>mark</i> the
/// section, and marking is where the band and the bill are.
/// </summary>
public sealed class ExamSessionTransitionTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 27, 9, 0, 0, TimeSpan.Zero);

    private IServiceScope Scope() => app.Services.CreateScope();

    private static IExamSessionRepository RepositoryIn(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IExamSessionRepository>();

    /// <summary>A Full Test with Reading open and nothing else attempted yet.</summary>
    private static ExamSession OpenAtReading() =>
        ExamSession.Rehydrate(
            ExamSessionId.New(), UserId.New(), new ExamVersionId("version-1"),
            SessionMode.Full, SessionStatus.InProgress, T0, null,
            // `runningSince: T0` because that is what `SectionAttempt.Open`
            // produces — an attempt the engine opened is running. A fixture
            // that left it null would be testing the guard against a state the
            // engine never creates.
            [SectionAttempt.Rehydrate(
                ExamModule.Reading, T0, T0.AddHours(1), null, runningSince: T0)]);

    private static ExamSession Copy(ExamSession session) =>
        ExamSession.Rehydrate(
            session.Id, session.UserId, session.ExamVersionId, session.Mode, session.Status,
            session.StartedAt, session.SubmittedAt,
            session.Attempts.Select(a => SectionAttempt.Rehydrate(
                a.Module, a.StartedAt, a.DeadlineAt, a.SubmittedAt,
                // The stopwatch travels with the copy. Dropping it here would
                // make every copy read as paused at zero, and the guard would
                // then be compared against a state nothing produced.
                a.AccumulatedSeconds, a.RunningSince, a.TargetSeconds)));

    /// <summary>
    /// Two "Tiếp theo" presses at the same instant: one wins.
    ///
    /// <b>A barrier, not two sequential calls.</b> Sequential writes exercise
    /// the filter; they do not exercise the race. Both threads are held until
    /// both are ready, so only the database's own atomicity separates them —
    /// which is the guarantee under test, and the one that read-check-write in
    /// application code does not have.
    /// </summary>
    [SkippableFact]
    public async Task Two_transitions_out_of_one_open_section_produce_a_single_winner()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var sessions = RepositoryIn(scope);

        var session = OpenAtReading();
        await sessions.AddAsync(session, default);

        // What both callers read, before either of them decided anything.
        var from = new SessionState(SessionStatus.InProgress, ExamModule.Reading, true);

        using var barrier = new Barrier(2);

        async Task<bool> Racer(Func<ExamSession, ExamSession> decide)
        {
            var mine = decide(Copy(session));
            barrier.SignalAndWait();
            return await sessions.TrySaveAsync(mine, from, default);
        }

        var results = await Task.WhenAll(
            // One tab presses "Tiếp theo": Reading closes, Listening opens.
            Task.Run(() => Racer(s =>
            {
                s.Attempts[0].Submit(T0.AddMinutes(30));
                return s;
            })),
            // The other presses "Nộp bài": the whole sitting closes.
            Task.Run(() => Racer(s =>
            {
                s.Submit(T0.AddMinutes(30));
                return s;
            })));

        Assert.Single(results, won => won);
        Assert.Single(results, won => !won);
    }

    /// <summary>
    /// A transition naming a section that has already been closed matches
    /// nothing.
    ///
    /// This is the shape a late retry has: a phone that changed network and
    /// re-sent "Tiếp theo" for Reading while the learner is already in
    /// Listening. Without the section in the filter, a guard on status alone
    /// would let it through — and it would rewrite the sitting from a view
    /// taken before Listening existed.
    /// </summary>
    [SkippableFact]
    public async Task A_transition_out_of_a_section_that_is_already_closed_is_refused()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var sessions = RepositoryIn(scope);

        var session = OpenAtReading();
        await sessions.AddAsync(session, default);

        var advanced = Copy(session);
        advanced.Attempts[0].Submit(T0.AddMinutes(30));

        Assert.True(await sessions.TrySaveAsync(
            advanced, new SessionState(SessionStatus.InProgress, ExamModule.Reading, true), default));

        // The retry, still believing Reading is open.
        var late = Copy(session);
        late.Submit(T0.AddMinutes(31));

        Assert.False(await sessions.TrySaveAsync(
            late, new SessionState(SessionStatus.InProgress, ExamModule.Reading, true), default));

        var stored = await sessions.FindAsync(session.Id, default);
        Assert.Equal(SessionStatus.InProgress, stored!.Status);
    }

    /// <summary>
    /// A transition out of a sitting that is no longer in progress is refused.
    ///
    /// The expiry sweep and a learner's own submit reach this from opposite
    /// directions and must not both land — one of them would overwrite the
    /// other's status, and the difference between "submitted" and "expired" is
    /// the difference between being in time and not.
    /// </summary>
    [SkippableFact]
    public async Task A_transition_out_of_a_sitting_that_is_already_closed_is_refused()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var sessions = RepositoryIn(scope);

        var session = OpenAtReading();
        await sessions.AddAsync(session, default);

        var expired = Copy(session);
        expired.Expire(T0.AddHours(2));

        Assert.True(await sessions.TrySaveAsync(
            expired, new SessionState(SessionStatus.InProgress, ExamModule.Reading, true), default));

        var submitted = Copy(session);
        submitted.Submit(T0.AddHours(2));

        Assert.False(await sessions.TrySaveAsync(
            submitted, new SessionState(SessionStatus.InProgress, ExamModule.Reading, true), default));

        var stored = await sessions.FindAsync(session.Id, default);
        Assert.Equal(SessionStatus.Expired, stored!.Status);
    }

    /// <summary>
    /// A sitting that is in progress with every attempt already closed.
    ///
    /// <b>The only case that exercises the null-module branch of the filter,
    /// and nothing did.</b> No transition produces this state, so it comes from
    /// a bad write — and the expiry sweep still has to be able to close it, or
    /// the learner has a sitting they can never finish and never leave. The
    /// filter for it is a `$not` around an `$elemMatch`, which is the shape most
    /// likely to be silently wrong, and it was untested against a real server.
    ///
    /// It also pins <c>MatchedCount</c> rather than <c>ModifiedCount</c>: the
    /// second write here replaces the document with itself, so a repository
    /// asking "did anything change" would report a race that never happened.
    /// </summary>
    [SkippableFact]
    public async Task A_sitting_with_no_open_section_can_still_be_closed()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var scope = Scope();
        var sessions = RepositoryIn(scope);

        var session = ExamSession.Rehydrate(
            ExamSessionId.New(), UserId.New(), new ExamVersionId("version-1"),
            SessionMode.Full, SessionStatus.InProgress, T0, null,
            [SectionAttempt.Rehydrate(
                ExamModule.Reading, T0, T0.AddHours(1), T0.AddMinutes(30))]);

        await sessions.AddAsync(session, default);

        var noOpenSection = new SessionState(SessionStatus.InProgress, null, false);

        // A write that changes nothing still matched, so it is not a lost race.
        Assert.True(await sessions.TrySaveAsync(Copy(session), noOpenSection, default));

        var expired = Copy(session);
        expired.Expire(T0.AddHours(2));

        Assert.True(await sessions.TrySaveAsync(expired, noOpenSection, default));

        var stored = await sessions.FindAsync(session.Id, default);
        Assert.Equal(SessionStatus.Expired, stored!.Status);

        // And once it is closed, the same guard no longer matches.
        Assert.False(await sessions.TrySaveAsync(Copy(session), noOpenSection, default));
    }
}
