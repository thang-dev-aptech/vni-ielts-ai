using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Exams;

/// <summary>Raised when a request names a session this caller may not touch, or none at all.</summary>
public sealed class SessionNotFoundException() : Exception("No such exam session.");

/// <summary>
/// The deadline has passed, and the learner is the one asking.
///
/// <b>Distinct from the server closing an expired session.</b> The domain's
/// <c>Submit</c> deliberately does not police the deadline so this layer can
/// tell the two apart: a late write from a client is refused, while the server
/// noticing an expiry marks the sitting <c>Expired</c> and keeps what was
/// already saved. Folding them together loses a learner's work whenever their
/// connection drops near the end. → `key-flows.md` §2
/// </summary>
public sealed class SessionExpiredException() : Exception("This exam session has expired.");

public sealed class SessionNotInProgressException(string status)
    : Exception($"This exam session is {status}.")
{
    public string Status { get; } = status;
}

/// <summary>
/// A write naming a section the learner is not in.
///
/// <b>Split out of <see cref="SessionExpiredException"/>, which it borrowed
/// until 27/08/2026.</b> An autosave for Reading arriving while the sitting
/// had moved on to Listening was answered with `SESSION_EXPIRED` — a code
/// whose documented client handling is to stop the exam and show the results
/// screen. The sitting was not expired and nothing was wrong with it; a
/// mis-routed request would have ended a live one.
/// </summary>
public sealed class SectionNotOpenException(ExamModule requested, ExamModule? open)
    : Exception(open is { } module
        ? $"This sitting is on {module}, not {requested}."
        : $"This sitting has no open section, so {requested} cannot be written to.")
{
    public ExamModule Requested { get; } = requested;
    public ExamModule? Open { get; } = open;
}

/// <summary>
/// A Speaking answer sent as text.
///
/// Speaking's artifact is audio, and its answer sheet is the server-owned
/// index of what was uploaded. A client write there is never a correct request:
/// at best it is a no-op, at worst it overwrites a recording id with one the
/// caller chose — which is a way to be marked on somebody else's performance.
/// </summary>
public sealed class SpeakingIsNotWrittenException()
    : Exception(
        "A Speaking answer is a recording, not text. Upload it to "
        + "POST /api/v1/sessions/{id}/recordings; the server files it against the question.");

public sealed record ListExamsQuery();

public sealed class ListExams(IExamCatalogue catalogue)
{
    public async Task<IReadOnlyList<ExamCatalogueItem>> HandleAsync(
        ListExamsQuery _, CancellationToken ct) =>
        [.. (await catalogue.ListSittableAsync(ct)).Select(v => v.ToCatalogueItem())];
}

/// <param name="Timing">
/// Whether this is thi thử (a deadline the server enforces) or luyện đề (an
/// open-ended stopwatch the learner can pause). → <see cref="SessionTiming"/>
/// </param>
/// <param name="TargetSeconds">
/// The learner's own goal for an open-ended sitting — 20, 40, 60, 90 minutes or
/// whatever they typed.
///
/// <b>Ignored entirely for a deadlined sitting.</b> There the paper's timing
/// profile decides, and a caller that could shorten or lengthen its own exam is
/// the whole of threat `T6`.
/// </param>
public sealed record StartExamSessionCommand(
    UserId UserId, ExamVersionId ExamVersionId, SessionMode Mode, ExamModule? Module,
    SessionTiming Timing = SessionTiming.Deadline, int? TargetSeconds = null);

/// <summary>
/// Opens a sitting.
///
/// <b>Nothing about the clock is negotiable by the caller.</b> The command
/// carries no start time and no duration; both are derived inside the
/// aggregate from the server clock and the version's timing profile. That is
/// the whole of ADR-0007 at this layer — there is no parameter through which a
/// client could extend its own exam.
///
/// <b>Entitlement is a seam, not an omission.</b> `B-4` and `B-5a` have not
/// decided whether starting a session spends a token or what it costs, so
/// there is no charge here and no invented default. When the rule exists it
/// goes at the top of this method, inside the same transaction as the insert —
/// which is why the database is a replica set. → `G-11`, threat `T22`
/// </summary>
public sealed class StartExamSession(
    IExamCatalogue catalogue, IExamSessionRepository sessions, IClock clock)
{
    public async Task<SessionView> HandleAsync(StartExamSessionCommand command, CancellationToken ct)
    {
        var version = await catalogue.FindAsync(command.ExamVersionId, ct)
            ?? throw new SessionNotFoundException();

        if (!version.IsSittable) throw new SessionNotFoundException();

        // Full Test starts at the version's first module and advances itself;
        // Single Skill starts where it was asked to and never advances. The
        // two are not interchangeable. → `E-11`…`E-13`
        var firstModule = command.Mode == SessionMode.Full
            ? version.FirstModule()
            : command.Module ?? throw new ArgumentException(
                "A single-skill session must name its module.", nameof(command));

        var now = clock.UtcNow;
        var session = ExamSession.Start(
            command.UserId, version, command.Mode, command.Timing, firstModule, now,
            command.TargetSeconds);

        await sessions.AddAsync(session, ct);

        return session.ToView(version, now, SessionProjection.Empty, []);
    }
}

public sealed record GetExamSessionQuery(UserId UserId, ExamSessionId SessionId);

public sealed class GetExamSession(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    IAnswerSheetStore answers,
    ISectionResultStore results,
    SectionMarkingRunner marker,
    IClock clock)
{
    public async Task<SessionView> HandleAsync(GetExamSessionQuery query, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(catalogue, query.SessionId, query.UserId, ct);
        var now = clock.UtcNow;

        // The server noticing an expiry, not a learner submitting late: keep
        // everything already saved, mark it, and close the sitting.
        var sweep = await ExpiredSittings.CloseIfOverdueAsync(
            session, version, sessions, answers, results, marker, now, ct);

        // Somebody else closed it first. `Expire` has already run on this copy,
        // so what is in memory describes a transition that was never written —
        // reporting it would show the learner an outcome nothing recorded.
        if (sweep is ExpirySweep.LostToAnotherWriter)
            (session, version) = await sessions.LoadOwnedAsync(
                catalogue, query.SessionId, query.UserId, ct);

        var saved = session.Current is { } current
            ? await answers.ReadAsync(session.Id, current.Module, ct)
            : SessionProjection.Empty;

        return session.ToView(version, now, saved, []);
    }
}

/// <param name="Changes">
/// The questions this autosave touched, and nothing else.
///
/// <b>A key with a null value clears that answer; a key that is absent is left
/// alone.</b> The distinction is the whole fix. A whole sheet cannot draw it —
/// a blank there means both "the learner rubbed this out" and "this client has
/// never heard of this question" — so a tab whose view was ten seconds old
/// deleted everything another tab had typed, and no rule applied to a full
/// sheet could have told the two cases apart.
/// </param>
/// <param name="BaseRevision">
/// The sheet version this patch was composed against, when the caller knows it.
///
/// <b>It cannot refuse the write, and is not meant to.</b> Two patches on
/// different questions are both correct whatever order they land in; two on the
/// same question resolve to the later one, which is what the learner typed
/// last. What the revision buys is the other half — a caller whose base does
/// not match is told so and handed the merged sheet, so a second tab takes in
/// what the first one wrote instead of drifting until someone reloads.
///
/// Null means "I did not look", which is treated as behind: the caller gets the
/// sheet back. That is cheap and always safe, so there is no migration ramp
/// here and nothing to deprecate.
/// </param>
/// <param name="Sequences">
/// A per-question ordering token, one for each entry in <paramref name="Changes"/>.
///
/// The client raises it past whatever the server last reported, so it is
/// monotonic per question without depending on a clock. An entry whose token is
/// not greater than the stored one is ignored — an older answer arriving late is
/// not an error, it is simply no longer the answer.
/// → <see cref="IAnswerSheetStore.PatchAsync"/>
/// </param>
public sealed record SaveAnswersCommand(
    UserId UserId, ExamSessionId SessionId, ExamModule Module,
    IReadOnlyDictionary<string, string?> Changes,
    int? BaseRevision = null,
    IReadOnlyDictionary<string, long>? Sequences = null);

/// <param name="Answers">
/// The merged sheet, present only when the caller was working from an older
/// revision than the one this patch amended.
///
/// Null is the ordinary case — one tab, nothing missed, nothing to send back.
/// Returning the sheet on every autosave would put the whole section on the
/// wire every few seconds to tell the learner what they already have.
/// </param>
/// <param name="Sequences">
/// The ordering tokens the sheet now holds, sent back whenever
/// <paramref name="Answers"/> is. A caller that has taken in another writer's
/// answers has to raise its own counters past theirs, or its next edit to one
/// of those questions carries a token the server will ignore — and the learner
/// watches their correction do nothing.
/// </param>
public sealed record SaveAnswersResult(
    int Revision,
    IReadOnlyDictionary<string, string?>? Answers,
    IReadOnlyDictionary<string, long>? Sequences = null);

/// <summary>
/// An autosave from the section the learner is in.
///
/// Five refusals, and each one is a way the sheet could otherwise be
/// corrupted: a finished sitting cannot take writes, a section that is not the
/// open one cannot take writes — that is how a Full Test candidate would edit
/// Reading while sitting Writing — a write after the deadline is refused
/// outright rather than quietly accepted, Speaking is not written at all, and a
/// question id the exam does not contain is refused rather than stored.
///
/// <b>The wrong-section refusal reports itself as the wrong section.</b> It
/// used to raise <see cref="SessionExpiredException"/>, which tells a client
/// its sitting is over. The sitting was fine; the request had gone to the
/// wrong place, and the client would have ended a live exam on the strength of
/// it. → <see cref="SectionNotOpenException"/>
/// </summary>
public sealed class SaveAnswers(
    IExamCatalogue catalogue, IExamSessionRepository sessions, IAnswerSheetStore answers, IClock clock)
{
    /// <summary>
    /// Roughly twenty times the longest answer anybody writes, and forty
    /// questions of it still fit inside a BSON document many times over.
    /// </summary>
    private const int MaxAnswerCharacters = 60_000;

    public async Task<SaveAnswersResult> HandleAsync(SaveAnswersCommand command, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(catalogue, command.SessionId, command.UserId, ct);

        if (session.Status != SessionStatus.InProgress)
            throw new SessionNotInProgressException(session.Status.ToString());

        // Speaking's sheet holds recording ids the server wrote, not answers a
        // client composed. Letting an autosave replace it would let a caller
        // point their own marking at any recording id they can name.
        if (command.Module == ExamModule.Speaking) throw new SpeakingIsNotWrittenException();

        var current = session.Current;
        if (current is null || current.Module != command.Module)
            throw new SectionNotOpenException(command.Module, current?.Module);

        var now = clock.UtcNow;

        // <b>Asked in the positive.</b> An open-ended sitting has no deadline,
        // and `serverNow <= null` is false — so the old phrasing would have
        // refused every autosave in luyện đề as late.
        if (current.IsPastDeadline(now)) throw new SessionExpiredException();

        /*
         * <b>A question id is now part of a database path, so it stops being
         * free text.</b>
         *
         * The sheet stores answers keyed by question id, and a patch addresses
         * one key at a time — so an id containing `.` would be read as a path
         * into a nested object and one beginning with `$` as an operator. Both
         * write somewhere other than where they were addressed.
         *
         * Checking the *shape* would be enough for that and is not enough on
         * its own: an id of the right shape that no question uses is still a
         * caller writing arbitrary keys into a document the marker reads. The
         * exam already names every question it contains, so that is what the
         * patch is checked against, and the shape check underneath it in the
         * store stays for the callers that are not this handler.
         */
        var section = version.Section(command.Module)
            ?? throw new SectionNotOpenException(command.Module, current.Module);

        var known = section.Questions.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = command.Changes.Keys.Where(id => !known.Contains(id)).ToList();
        if (unknown.Count > 0) throw new UnknownQuestionException(unknown);

        /*
         * <b>A patch grows the sheet; a whole-sheet write could not.</b>
         *
         * The old contract replaced the document, so its size was bounded by
         * one request body and the 1 MB request cap bounded that. A patch
         * `$set`s keys into a document that stays, so the document accumulates
         * across requests with no ceiling anywhere — and a BSON document stops
         * at 16 MB. Forty questions at 900 KB each reaches it in about eighteen
         * autosaves, and the failure is `BSONObjectTooLarge` from the driver:
         * not caught, a 500, and from then on *no* autosave for that section
         * can ever succeed. Reads keep working, so nothing surfaces until the
         * learner cannot save and cannot submit.
         *
         * <b>This is a storage bound, not a word limit.</b> The paper's own
         * limit is <c>MaxWords</c> on the question and is a different rule with
         * a different owner. This one exists so that no sequence of requests
         * can put a section beyond saving, and it is set far above any answer a
         * person writes: an IELTS Task 2 response is around 2,600 characters.
         */
        var oversized = command.Changes
            .Where(c => c.Value is { Length: > MaxAnswerCharacters })
            .Select(c => c.Key)
            .ToList();

        if (oversized.Count > 0) throw new AnswerTooLongException(oversized, MaxAnswerCharacters);

        var patched = await answers.PatchAsync(
            command.SessionId, command.Module, command.Changes, now, ct, command.Sequences);

        /*
         * <b>Behind, so take the whole sheet back.</b>
         *
         * The caller composed this patch against a revision older than the one
         * it just amended, which means somebody wrote in between — another tab,
         * or this tab's own earlier request landing late. Their answers are in
         * the merged sheet and not in the caller's copy, so the caller gets it.
         *
         * The old code raised a conflict here instead, and the client answered
         * it by re-sending its whole local sheet against the new revision —
         * completing the overwrite the conflict existed to prevent, one beat
         * later. There is nothing to refuse now; both patches are already in.
         */
        var caughtUp = command.BaseRevision is { } expected && expected == patched.PreviousRevision;

        return new SaveAnswersResult(
            patched.Sheet.Revision,
            caughtUp ? null : patched.Sheet.Answers,
            caughtUp ? null : patched.Sheet.Sequences);
    }
}

/// <summary>
/// One answer is long enough to threaten the document it is stored in.
///
/// Refused per question rather than per sheet, because a caller told only that
/// "the sheet is too big" has nothing to shorten.
/// </summary>
public sealed class AnswerTooLongException(IReadOnlyList<string> questionIds, int limit)
    : Exception(
        "The answer to "
        + string.Join(", ", questionIds.Take(5).Select(id => $"'{id}'"))
        + (questionIds.Count > 5 ? $" (and {questionIds.Count - 5} more)" : "")
        + $" is longer than {limit:N0} characters.")
{
    public IReadOnlyList<string> QuestionIds { get; } = questionIds;
}

/// <summary>
/// An autosave named a question this exam does not have.
///
/// <b>Refused rather than stored.</b> The ids are keys in the answer document
/// and inputs to the marker, so accepting one the paper never asked lets a
/// caller grow a sheet that no section can account for — and, before the ids
/// were checked at all, choose the database path their value landed on.
/// </summary>
public sealed class UnknownQuestionException(IReadOnlyList<string> questionIds)
    : Exception(
        "This section has no question "
        + string.Join(", ", questionIds.Take(5).Select(id => $"'{id}'"))
        + (questionIds.Count > 5 ? $" (and {questionIds.Count - 5} more)" : "")
        + ".")
{
    public IReadOnlyList<string> QuestionIds { get; } = questionIds;
}

/// <summary>
/// The sitting kept moving out from under a transition that had to land.
///
/// <b>Reported rather than papered over.</b> A submit that loses the race is
/// retried against the state it lost to, because that state is one it can be
/// made from. This is what is left when that keeps failing — and the one thing
/// it must not do is answer 200 with a results view, which is what it did
/// before: the learner was shown their results, their sitting was still
/// running, and the idempotency guard stored the lie as the answer for their
/// key so no retry could ever get past it.
/// </summary>
public sealed class SessionMovedOnException()
    : Exception("This sitting kept changing while it was being submitted.");

/// <summary>
/// What the learner asked the stopwatch to do.
///
/// <b>An intent, and never a time.</b> The client says "pause" and the server
/// reads its own clock; a payload carrying "I paused at 14:32" is a payload a
/// learner can write, and a stopwatch they can write is one they can wind back.
/// That is the whole of ADR-0007 applied to a clock that counts up rather than
/// down. → threat `T6`
/// </summary>
public enum StopwatchIntent { Pause, Resume }

public sealed record SetStopwatchCommand(
    UserId UserId, ExamSessionId SessionId, StopwatchIntent Intent);

/// <summary>
/// Starts and stops the luyện đề clock.
///
/// <b>Refused outright for a deadlined sitting.</b> "Thi thử pausable" is not a
/// feature with a switch missing; it is the exam stopping being an exam. The
/// refusal is here rather than left to the absence of a button, because a
/// button is not a control — anyone can issue the request without one.
/// </summary>
public sealed class SetStopwatch(
    IExamCatalogue catalogue, IExamSessionRepository sessions, IClock clock)
{
    public async Task<SessionView> HandleAsync(SetStopwatchCommand command, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        if (session.Status != SessionStatus.InProgress)
            throw new SessionNotInProgressException(session.Status.ToString());

        if (session.Timing != SessionTiming.OpenEnded)
            throw new StopwatchNotAvailableException();

        var now = clock.UtcNow;
        var current = session.Current
            ?? throw new SectionNotOpenException(ExamModule.Reading, null);

        var from = SessionState.Of(session);

        if (command.Intent == StopwatchIntent.Pause) current.Pause(now);
        else current.Resume(now);

        /*
         * <b>A lost guard here is success, not failure.</b>
         *
         * The only way to lose it is that the stopwatch is already in the state
         * this call asked for — another tab, or this learner's own double-tap.
         * Pausing a paused clock is what they wanted; reporting an error for it
         * would put a red message on a screen where nothing went wrong.
         *
         * Re-read either way, so what comes back describes the stored sitting
         * rather than the copy this call mutated.
         */
        await sessions.TrySaveAsync(session, from, ct);

        var (stored, itsVersion) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        return stored.ToView(itsVersion, now, SessionProjection.Empty, []);
    }
}

/// <summary>
/// The clock cannot be stopped on this sitting.
///
/// <b>Thi thử, and it is the whole difference between the two modes.</b> A
/// deadline that can be paused is not a deadline.
/// </summary>
public sealed class StopwatchNotAvailableException()
    : Exception("This sitting is timed by the server and its clock does not pause.");

public sealed record SetTargetTimeCommand(
    UserId UserId, ExamSessionId SessionId, int? TargetSeconds);

/// <summary>
/// The learner's own goal for the section they are in.
///
/// <b>Stored, displayed, and read by no rule.</b> The moment anything branches
/// on it — refusing a save, closing a section, colouring a result — it has
/// become a deadline the learner set for themselves, and luyện đề has become
/// the exam with the added twist that the candidate writes the rules. It exists
/// so the review screen can say "bạn nhắm 40 phút, bạn làm 47".
/// </summary>
public sealed class SetTargetTime(
    IExamCatalogue catalogue, IExamSessionRepository sessions, IClock clock)
{
    /// <summary>
    /// Six hours. Not a product rule — a bound on a number that is written to a
    /// document and rendered into a clock, so that neither has to cope with a
    /// value nobody meant.
    /// </summary>
    private const int MaxTargetSeconds = 6 * 60 * 60;

    public async Task<SessionView> HandleAsync(SetTargetTimeCommand command, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        if (session.Status != SessionStatus.InProgress)
            throw new SessionNotInProgressException(session.Status.ToString());

        if (session.Timing != SessionTiming.OpenEnded)
            throw new StopwatchNotAvailableException();

        if (command.TargetSeconds is { } seconds && (seconds <= 0 || seconds > MaxTargetSeconds))
            throw new ArgumentOutOfRangeException(
                nameof(command),
                $"A target time must be between one second and {MaxTargetSeconds / 3600} hours.");

        var now = clock.UtcNow;
        var current = session.Current
            ?? throw new SectionNotOpenException(ExamModule.Reading, null);

        var from = SessionState.Of(session);
        current.AimFor(command.TargetSeconds);

        await sessions.TrySaveAsync(session, from, ct);

        var (stored, itsVersion) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        return stored.ToView(itsVersion, now, SessionProjection.Empty, []);
    }
}

public sealed record AdvanceSectionCommand(UserId UserId, ExamSessionId SessionId);

/// <summary>
/// "Tiếp theo" inside a Full Test.
///
/// The section being left is marked here, before the next one opens, so a
/// learner who abandons the sitting halfway still keeps the bands they earned.
/// Single Skill never reaches this: its call to action is "làm đề mới", a
/// different operation with a different entitlement effect. → CLAUDE.md rule 10
/// </summary>
public sealed class AdvanceSection(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    IAnswerSheetStore answers,
    ISectionResultStore results,
    SectionMarkingRunner marker,
    IMarkingOutbox outbox,
    IRubricSource rubrics,
    IClock clock)
{
    public async Task<SessionView> HandleAsync(AdvanceSectionCommand command, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(catalogue, command.SessionId, command.UserId, ct);

        if (session.Status != SessionStatus.InProgress)
            throw new SessionNotInProgressException(session.Status.ToString());

        var now = clock.UtcNow;
        var leaving = session.Current;
        var from = SessionState.Of(session);

        var outcome = session.AdvanceToNextSection(version, now);
        if (outcome is AdvanceOutcome.NotAFullTest)
            throw new InvalidOperationException(
                "A single-skill session does not advance. Its next step is a new test.");

        /*
         * <b>The section closes before it is marked, and it used to be the
         * other way round.</b>
         *
         * Marking first meant two callers arriving together — two tabs on
         * "Tiếp theo", or a retry from a phone that changed network — both
         * marked the section and only then wrote. The write was guarded
         * afterwards, which is too late: the evaluation had been bought twice
         * and the second result was thrown away by an insert-if-absent that had
         * no way to refund it.
         *
         * Now only the caller whose transition actually landed goes on to mark.
         * The cost of that order is a section that is closed and unmarked if
         * this process dies in between — for Reading and Listening that is
         * recovered on the next results read; for Writing and Speaking it is
         * <b>not</b>, and see <see cref="MarkSection.CatchUpAsync"/> for why
         * that is a stated hole rather than a fixed one.
         */
        /*
         * <b>The sheet is frozen before the transition, not after it.</b>
         *
         * The compare-and-swap below guards the session document; the answer
         * sheet is a different collection and nothing joined them. So an
         * autosave that had already passed its "is this section open" check
         * could land <i>after</i> this transition and after marking read the
         * sheet — the learner's chip said "Đã lưu" and the result did not
         * contain the answer. Closing afterwards leaves exactly that window
         * open, because the losing patch is already in flight while the CAS
         * runs.
         *
         * Freezing first costs one idempotent write when this caller then
         * loses the CAS — and the winner was closing the same section anyway,
         * by definition, since it is the section this copy saw open.
         * → <see cref="IAnswerSheetStore.CloseAsync"/>
         */
        var frozen = leaving is null
            ? null
            : await answers.CloseAsync(session.Id, leaving.Module, now, ct);

        if (!await sessions.TrySaveAsync(session, from, ct))
        {
            /*
             * Another writer advanced or submitted this sitting first. That is
             * not an error to report: both tabs pressed the same button and one
             * of them was going to be second. Read where the sitting actually
             * is and answer with that, so the loser lands on the same section
             * as the winner rather than on a message.
             */
            var (moved, itsVersion) = await sessions.LoadOwnedAsync(
                catalogue, command.SessionId, command.UserId, ct);

            var elsewhere = moved.Current is { } open
                ? await answers.ReadAsync(moved.Id, open.Module, ct)
                : SessionProjection.Empty;

            return moved.ToView(
                itsVersion, now, elsewhere, await results.ListAsync(moved.Id, ct));
        }

        if (leaving is not null)
            await MarkSection.RunAsync(
                version, leaving.Module, session.Id, answers, results, marker, ct, frozen,
                outbox, rubrics, clock);

        var saved = session.Current is { } next
            ? await answers.ReadAsync(session.Id, next.Module, ct)
            : SessionProjection.Empty;

        return session.ToView(version, now, saved, await results.ListAsync(session.Id, ct));
    }
}

public sealed record SubmitExamSessionCommand(UserId UserId, ExamSessionId SessionId);

public sealed class SubmitExamSession(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    IAnswerSheetStore answers,
    ISectionResultStore results,
    ISectionMarkingStore markings,
    SectionMarkingRunner marker,
    IMarkingOutbox outbox,
    IRubricSource rubrics,
    IClock clock)
{
    public async Task<SessionResultsView> HandleAsync(
        SubmitExamSessionCommand command, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(catalogue, command.SessionId, command.UserId, ct);
        var now = clock.UtcNow;

        /*
         * <b>A submit that loses the race is retried, not reported as done.</b>
         *
         * The first version discarded `TrySaveAsync`'s answer entirely: it
         * re-read and returned the results view whatever had happened. So a
         * learner pressing "Nộp bài" while their other tab pressed "Tiếp theo"
         * lost the race, submitted nothing, and was handed a 200 and a results
         * screen for a sitting that was still running on Listening. The
         * idempotency guard then stored that 200 as the answer for their key,
         * so every retry replayed "here are your results, status inprogress"
         * and the paper could not be handed in at all.
         *
         * Losing to an advance is ordinary and its result is a state this
         * submit can be made from — so make it from there. Three attempts,
         * because the only thing that can keep taking the sitting away is
         * another writer doing it repeatedly, and at that point saying so is
         * better than looping.
         */
        for (var attempt = 0; attempt < 3 && session.Status == SessionStatus.InProgress; attempt++)
        {
            var open = session.Current;
            var from = SessionState.Of(session);

            // The learner pressing submit after the deadline is refused; the
            // server closing an expired sitting is not. Same instant, two
            // different events — and the refusal still marks what was saved
            // before the deadline, so being one second late costs the learner
            // the request and nothing else.
            var sweep = await ExpiredSittings.CloseIfOverdueAsync(
                session, version, sessions, answers, results, marker, now, ct);

            if (sweep is ExpirySweep.Closed) throw new SessionExpiredException();

            if (sweep is ExpirySweep.NotOverdue)
            {
                session.Submit(now);

                // Frozen, then guarded, then marked. The freeze is what stops
                // an autosave already in flight from landing after this section
                // is marked; the guard is what stops two tabs from marking it
                // twice. → `IAnswerSheetStore.CloseAsync`
                var frozen = open is null
                    ? null
                    : await answers.CloseAsync(session.Id, open.Module, now, ct);

                if (await sessions.TrySaveAsync(session, from, ct))
                {
                    if (open is not null)
                        await MarkSection.RunAsync(
                            version, open.Module, session.Id, answers, results, marker, ct, frozen,
                            outbox, rubrics, clock);

                    break;
                }
            }

            /*
             * Lost, or the sweep lost. Either way `Submit` or `Expire` has
             * already run on this copy and it describes a transition nobody
             * recorded, so nothing it holds may be reported or decided from.
             */
            (session, version) = await sessions.LoadOwnedAsync(
                catalogue, command.SessionId, command.UserId, ct);
        }

        // Read back what was actually stored rather than reporting the copy
        // this call mutated. One query, on a screen that is not a hot path.
        (session, version) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        if (session.Status == SessionStatus.InProgress)
            throw new SessionMovedOnException();

        // Read once and hand it on. The catch-up needs the same list the view
        // does, and fetching it twice per results screen is a query nobody is
        // buying anything with.
        var scored = await results.ListAsync(session.Id, ct);
        await MarkSection.CatchUpAsync(version, session, answers, results, marker, scored, ct);

        return session.ToResults(
            version,
            await results.ListAsync(session.Id, ct),
            await markings.ListAsync(session.Id, ct),
            await outbox.ListAsync(session.Id, ct));
    }
}

public sealed record ListMySittingsQuery(UserId UserId, int Limit);

/// <summary>
/// A learner's own recent sittings.
///
/// <b>The query behind the overview screen.</b> Everything it returns already
/// existed in the database — `ListForUserAsync` was written with the
/// repository and then never called, so the dashboard had nothing to show and
/// became a page of links to start something new. A learner with a sitting
/// half-finished had no way back to it except a URL they no longer had.
///
/// <b>Scoped to the caller, and not by a filter the caller supplies.</b> The
/// user id comes from the token; there is no parameter through which one
/// learner could ask for another's history.
///
/// <b>Two lookups per sitting, bounded by the limit.</b> The version for its
/// title and the stored section results. That is an N+1 by construction, and
/// acceptable only because N is capped at <see cref="MaxLimit"/> — versions
/// are deduplicated because a learner re-sitting the same exam is the common
/// case, not the rare one.
/// </summary>
public sealed class ListMySittings(
    IExamCatalogue catalogue, IExamSessionRepository sessions, ISectionResultStore results)
{
    /// <summary>
    /// The most a single request will return.
    ///
    /// This is a dashboard and a history list, not an export. Raising it would
    /// multiply the per-sitting lookups below; a learner who genuinely needs
    /// their whole history needs a paged screen, not a bigger number here.
    /// </summary>
    public const int MaxLimit = 20;

    public async Task<IReadOnlyList<SittingSummaryView>> HandleAsync(
        ListMySittingsQuery query, CancellationToken ct)
    {
        var mine = await sessions.ListForUserAsync(
            query.UserId, Math.Clamp(query.Limit, 1, MaxLimit), ct);

        var versions = new Dictionary<string, ExamVersion>(StringComparer.Ordinal);
        var summaries = new List<SittingSummaryView>(mine.Count);

        foreach (var session in mine)
        {
            if (!versions.TryGetValue(session.ExamVersionId.Value, out var version))
            {
                // A sitting whose version has been deleted is not an error the
                // learner can act on, and dropping the row silently would make
                // their history quietly incomplete. Neither is good, so: skip
                // it here and leave the sitting reachable by its own URL.
                if (await catalogue.FindAsync(session.ExamVersionId, ct) is not { } found) continue;

                version = found;
                versions[session.ExamVersionId.Value] = version;
            }

            var scored = await results.ListAsync(session.Id, ct);
            var byModule = scored.ToDictionary(r => r.Module, r => r.Band);

            // Lower-cased like every other view on this surface. The clients
            // compare these against their own `ExamModule` union, which is
            // lower-case; one PascalCase field here would silently fail every
            // comparison rather than fail to compile.
            var sections = session.Attempts
                .Select(a => new SittingSectionView(
                    a.Module.ToString().ToLowerInvariant(),
                    byModule.TryGetValue(a.Module, out var band) ? band.Value : null))
                .ToList();

            var current = session.Status == SessionStatus.InProgress ? session.Current : null;

            summaries.Add(new SittingSummaryView(
                session.Id.Value,
                version.Id.Value,
                version.Title,
                version.Variant.ToString().ToLowerInvariant(),
                session.Mode.ToString().ToLowerInvariant(),
                session.Status.ToString().ToLowerInvariant(),
                session.StartedAt,
                session.SubmittedAt,
                current?.Module.ToString().ToLowerInvariant(),
                current?.DeadlineAt,
                sections,
                SittingBand.Overall(sections)));
        }

        return summaries;
    }
}

public sealed record GetSessionResultsQuery(UserId UserId, ExamSessionId SessionId);

/// <summary>
/// The results screen.
///
/// <b>It closes an overdue sitting too, and that is not tidiness.</b> A
/// learner reaches this screen straight from their history — nothing forces
/// them back through `GET /sessions/{id}` first. If expiry only happened
/// there, a sitting that ran out of time and was never reopened would show an
/// empty results page over a full answer sheet, indefinitely.
/// </summary>
public sealed class GetSessionResults(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    IAnswerSheetStore answers,
    ISectionResultStore results,
    ISectionMarkingStore markings,
    SectionMarkingRunner marker,
    IMarkingOutbox outbox,
    IRubricSource rubrics,
    IClock clock)
{
    public async Task<SessionResultsView> HandleAsync(GetSessionResultsQuery query, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(catalogue, query.SessionId, query.UserId, ct);

        var sweep = await ExpiredSittings.CloseIfOverdueAsync(
            session, version, sessions, answers, results, marker, clock.UtcNow, ct,
            outbox, rubrics, clock);

        if (sweep is ExpirySweep.LostToAnotherWriter)
            (session, version) = await sessions.LoadOwnedAsync(
                catalogue, query.SessionId, query.UserId, ct);

        var scored = await results.ListAsync(session.Id, ct);
        await MarkSection.CatchUpAsync(version, session, answers, results, marker, scored, ct);

        // <b>Both stores, every time.</b> Reading and Listening produce a
        // SectionScore; Writing and Speaking produce a SectionMarking. A
        // results screen that read one of them would show two skills and call
        // it a result.
        return session.ToResults(
            version,
            await results.ListAsync(session.Id, ct),
            await markings.ListAsync(session.Id, ct),
            await outbox.ListAsync(session.Id, ct));
    }
}

public sealed record SubmitSpeakingRecordingCommand(
    UserId UserId,
    ExamSessionId SessionId,
    string QuestionId,
    Stream Content,
    string ContentType);

/// <summary>
/// One Speaking answer arriving as audio.
///
/// <b>This is Speaking's <see cref="SaveAnswers"/>, and until 27/08/2026 it
/// enforced none of the same rules.</b> The upload lived entirely in the API
/// endpoint, which checked that the sitting was in progress and stopped there
/// — so a recording could be filed while the learner was sitting Reading, or
/// twenty minutes after the Speaking deadline had passed. Every other way of
/// answering a question refuses both. Speaking's did not, in the one module
/// whose answer does not travel through the autosave. → ADR-0007
///
/// <b>The question id is checked against the exam version, not accepted.</b>
/// It becomes a key on the answer sheet and later the thing marking looks up;
/// an unchecked one is a way to write rows nobody will read and to hide a
/// recording from the section it belonged to.
///
/// <b>And the id is written to the sheet here, by the server.</b> That is the
/// step that was missing altogether: the audio was stored and the id handed
/// back, with nothing on the server connecting the two. Asking the client to
/// echo it back on an autosave would have been worse than nothing — an id a
/// caller supplies is an id they can take from another sitting.
/// </summary>
public sealed class SubmitSpeakingRecording(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    IAnswerSheetStore answers,
    IRecordingStore recordings,
    IClock clock)
{
    public async Task<string> HandleAsync(
        SubmitSpeakingRecordingCommand command, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(
            catalogue, command.SessionId, command.UserId, ct);

        if (session.Status != SessionStatus.InProgress)
            throw new SessionNotInProgressException(session.Status.ToString());

        var current = session.Current;
        if (current is null || current.Module != ExamModule.Speaking)
            throw new SectionNotOpenException(ExamModule.Speaking, current?.Module);

        var now = clock.UtcNow;
        if (current.IsPastDeadline(now)) throw new SessionExpiredException();

        var section = version.Section(ExamModule.Speaking)
            ?? throw new SectionNotOpenException(ExamModule.Speaking, current.Module);

        if (!section.Questions.Any(q => q.Id == command.QuestionId))
            throw new ArgumentException(
                "That question is not part of this exam's Speaking section.",
                nameof(command));

        var recordingId = await recordings.SaveAsync(
            command.SessionId, command.QuestionId, command.Content, command.ContentType, ct);

        /*
         * ── Finalisation, and what happens when it is refused ──────────────
         *
         * <b>An upload is two writes, and the checks above were made before the
         * first one.</b> Twelve megabytes over a phone network is not a short
         * window, and a submit or an expiry can close Speaking inside it. The
         * closure protocol then refuses this second write, correctly: a
         * recording filed after marking has read the sheet would exist, be
         * indexed, and never be marked — and for a spoken answer that object is
         * often the learner's only copy of it.
         *
         * <b>So the bytes go too.</b> Leaving them is an object nothing
         * references, holding personal data under PDPL, accumulating one per
         * unlucky upload. Deleting it is what makes this a refusal rather than
         * a half-write, and it is what leaves the two outcomes the caller is
         * promised: the recording is in the frozen sheet, or the upload failed.
         * There is no third.
         *
         * <b>The delete is not allowed to change the answer.</b> If cleaning up
         * fails, the refusal still stands — the caller must not be told the
         * upload worked because the tidying did not. What is left then is an
         * orphan, which is what the reconciliation sweep is for.
         */
        try
        {
            // One entry, not a sheet replace: Part 1 and Part 2 can finish
            // uploading within a second of each other, and read-modify-write
            // would lose whichever landed first.
            await answers.SetAnswerAsync(
                command.SessionId, ExamModule.Speaking, command.QuestionId, recordingId, now, ct);
        }
        catch (SectionSheetClosedException)
        {
            try
            {
                await recordings.DeleteAsync(recordingId, ct);
            }
            catch (Exception)
            {
                // Swallowed on purpose. See above: the refusal is the answer.
            }

            throw;
        }

        return recordingId;
    }
}

/// <summary>
/// Marks one section, whichever kind of marking it needs.
///
/// <b>Both markers run, and which one does the work is decided by the module
/// rather than by the caller.</b> <see cref="ScoreIfDeterministic"/> returns
/// immediately for Writing and Speaking; the runner returns immediately for
/// Reading and Listening. Neither knows about the other, which is what keeps
/// "all four skills are marked when their section closes" one statement about
/// one pipeline.
///
/// <b>Every path that closes a section calls this one.</b> It exists because
/// they did not: advance and submit each carried their own copy of the pair,
/// and expiry — the third way a section closes — carried neither.
/// </summary>
internal static class MarkSection
{
    /// <param name="frozen">
    /// The sheet as the freeze returned it, when this call follows one.
    ///
    /// <b>Handed on rather than re-read, so that "marking read the frozen
    /// content" is guaranteed by construction.</b> No patch can land after the
    /// freeze, so a second read would return the same thing — but it would be
    /// the same thing by argument, and an invariant that rests on an argument
    /// is one a later change can break without anything noticing. Passing the
    /// bytes removes the question, and it removes a query.
    ///
    /// Null on the catch-up path, which has no freeze of its own to hand on:
    /// it runs against sections closed long ago, and reads them.
    /// </param>
    public static async Task RunAsync(
        ExamVersion version, ExamModule module, ExamSessionId sessionId,
        IAnswerSheetStore answers, ISectionResultStore results,
        SectionMarkingRunner marker, CancellationToken ct,
        AnswerSheet? frozen = null,
        IMarkingOutbox? outbox = null,
        IRubricSource? rubrics = null,
        IClock? clock = null)
    {
        await ScoreIfDeterministic.RunAsync(
            version, module, sessionId, answers, results, ct, frozen);

        /*
         * <b>The intent is made durable before the attempt is made.</b>
         *
         * A section closes and then it is marked, and the transition has to
         * come first — otherwise two callers arriving together both mark it and
         * the evaluation is bought twice. The cost of that order is a window: a
         * process that dies between the transition and the marking leaves a
         * section closed and unmarked.
         *
         * For Reading and Listening that is survivable, because a deterministic
         * score can be recomputed from the answer key. For Writing and Speaking
         * it was <b>permanent</b> — a re-entered submit short-circuits on a
         * sitting that is no longer in progress, and the catch-up pass skips
         * the non-deterministic modules deliberately. The band was gone for the
         * life of the sitting and nothing anywhere said so.
         *
         * Enqueuing first means a crash costs an attempt rather than a band.
         * → `IMarkingOutbox`, `I3.1`
         */
        if (outbox is not null && rubrics is not null && clock is not null)
            await MarkingWork.EnqueueAsync(version, module, sessionId, outbox, rubrics, clock, ct);

        await marker.RunAsync(version, module, sessionId, answers, ct);
    }

    /// <summary>
    /// Scores any closed section that has no score, on the way to showing
    /// results.
    ///
    /// <b>This is what pays for closing a section before marking it.</b> The
    /// transition is written first so that only one caller marks; the cost is a
    /// window in which a section is closed and unmarked, if the process dies
    /// between the two. Without a net that hole is permanent and silent — the
    /// results screen simply has no Reading band, for ever, and nothing
    /// anywhere says why.
    ///
    /// <b>Deterministic sections only, and that leaves a real hole rather than
    /// closing one.</b> Reading and Listening are scored from the answer key,
    /// so recomputing one costs a comparison and always produces the same
    /// number. Writing and Speaking are not. A process that dies between the
    /// transition and the marking leaves those sections closed, unmarked, and
    /// unreachable: a re-entered submit short-circuits on a sitting that is no
    /// longer in progress, and this method skips them. The band is gone for the
    /// life of the sitting.
    ///
    /// <b>It is left open on purpose.</b> Catching them up here means re-running
    /// an evaluation on every visit to the results screen for any section that
    /// has no marking — which is free today, because no evaluator is wired and
    /// the runner returns "awaiting evaluator" without calling anything, and
    /// becomes an unbounded paid retry the moment one is. Bounding it needs a
    /// retry budget and a failure policy, and inventing either would be
    /// inventing a rule nobody has decided. → `G-11`, and the entry in
    /// `docs/development/next-actions.md`
    ///
    /// <b>Which makes this a blocker on the first evaluator, not a nice to
    /// have.</b> The hole is invisible while marking is free and becomes silent
    /// data loss on the day it is not.
    /// </summary>
    public static async Task CatchUpAsync(
        ExamVersion version, ExamSession session,
        IAnswerSheetStore answers, ISectionResultStore results,
        SectionMarkingRunner marker, IReadOnlyList<SectionScore> scored, CancellationToken ct)
    {
        foreach (var attempt in session.Attempts)
        {
            if (attempt.SubmittedAt is null) continue;
            if (attempt.Module is not (ExamModule.Reading or ExamModule.Listening)) continue;
            if (scored.Any(s => s.Module == attempt.Module)) continue;

            await RunAsync(version, attempt.Module, session.Id, answers, results, marker, ct);
        }
    }
}

/// <summary>
/// Closes a sitting whose deadline has passed, and marks what the learner had
/// already saved before it did.
///
/// <b>The marking half was missing, and its absence was silent.</b> Expiring
/// set the status and left the answer sheet alone, which reads as "nothing was
/// lost" — and nothing was, from storage. The band was. Scoring only ever ran
/// from "advance" and from "submit", and an expiry is neither: a learner who
/// ran out of time on Reading ended with forty saved answers, no result, and a
/// results screen that said so. The sheet sat there, complete and unread, for
/// the life of the sitting.
///
/// That also made the lenient branch pointless. The whole reason the domain's
/// <c>Submit</c> refuses to police the deadline is so this layer can be
/// generous when the server is the one closing up — generous with the answers
/// and then not with the bands is not generous at all.
///
/// <b>Marking runs after the save, and the save is what makes it happen
/// once.</b> A second reader finds the sitting already <c>Expired</c> and
/// returns false here — which matters because marking Writing costs money.
/// The runner asks the marking store what is already done for the same reason,
/// as the belt to this braces.
/// </summary>
/// <summary>
/// What a sweep did, which is three outcomes and used to be two.
///
/// <b><see cref="LostToAnotherWriter"/> is not a failure and not a no-op.</b>
/// It means somebody else closed this sitting between the read and the write —
/// the learner's other tab, a retry, or a submit racing the sweep. Their
/// transition stands, so nothing is wrong; but the copy in memory has already
/// been mutated by <c>Expire</c> and no longer describes anything real, so the
/// caller must re-read rather than report what it is holding.
///
/// Folding this into "false" would have made a caller carry on and submit a
/// sitting that was already expired; folding it into "true" would have made
/// the submit path report an expiry to a learner whose other tab had simply
/// handed the paper in.
/// </summary>
internal enum ExpirySweep
{
    /// <summary>
    /// Nothing to do — <b>and that covers two different situations</b>: the
    /// sitting is still inside its deadline, or it was never in progress to
    /// begin with. Every caller checks the second one before it gets here, so
    /// merging them costs nothing today; a caller that does not would submit a
    /// sitting that is already closed.
    /// </summary>
    NotOverdue,

    /// <summary>This call closed it, and marked the section it was on.</summary>
    Closed,

    /// <summary>Somebody else closed it first. See the note above.</summary>
    LostToAnotherWriter,
}

internal static class ExpiredSittings
{
    public static async Task<ExpirySweep> CloseIfOverdueAsync(
        ExamSession session, ExamVersion version,
        IExamSessionRepository sessions, IAnswerSheetStore answers,
        ISectionResultStore results, SectionMarkingRunner marker,
        DateTimeOffset now, CancellationToken ct,
        IMarkingOutbox? outbox = null, IRubricSource? rubrics = null, IClock? clock = null)
    {
        if (session.Status != SessionStatus.InProgress) return ExpirySweep.NotOverdue;

        /*
         * <b>Three cases, and they used to be two.</b>
         *
         * `IsWithinDeadline` answered false both for "the deadline has passed"
         * and for "there is no open section at all", and this line read both as
         * "close it". That was survivable while every attempt had a deadline.
         * It stopped being survivable the moment one could have none: an
         * untimed sitting compares against a null and would be swept away the
         * first time anybody opened it.
         *
         * So the deadline is asked about in the positive, and the repair case
         * — in progress with every attempt already closed, which no transition
         * here produces and so came from a bad write — is named separately. It
         * still has to close, or the learner has a sitting they can never
         * finish and never leave.
         */
        var pastDeadline = session.IsPastDeadline(now);
        var nothingOpen = session.Current is null;

        if (!pastDeadline && !nothingOpen) return ExpirySweep.NotOverdue;

        var open = session.Current;
        var from = SessionState.Of(session);

        session.Expire(now);

        // <b>Frozen, closed, then marked, and the order is the whole point.</b>
        // It used to mark and then save, so two callers arriving together —
        // this sweep and a learner pressing "Nộp bài" at the same second — both
        // marked the section before either wrote. The guard cannot help a
        // decision that has already been acted on. The freeze is the newer half
        // of the same lesson: a guard on the session document says nothing
        // about a write to the sheet. → `IAnswerSheetStore.CloseAsync`
        var frozen = open is null
            ? null
            : await answers.CloseAsync(session.Id, open.Module, now, ct);

        if (!await sessions.TrySaveAsync(session, from, ct))
            return ExpirySweep.LostToAnotherWriter;

        if (open is not null)
            await MarkSection.RunAsync(
                version, open.Module, session.Id, answers, results, marker, ct, frozen,
                            outbox, rubrics, clock);

        return ExpirySweep.Closed;
    }
}

/// <summary>
/// Marks a section when — and only when — its band comes from the answer key.
///
/// Writing and Speaking pass through untouched: their bands come from a
/// validated evaluation that does not exist yet, and writing a placeholder
/// result for them would put a number where product law L3 requires a dash.
/// → `A-11`, `A-13a`
/// </summary>
internal static class ScoreIfDeterministic
{
    /// <param name="frozen">
    /// The sheet the freeze returned, when this call follows one. Marking the
    /// bytes the freeze produced is what makes "the result contains everything
    /// that was saved, and nothing that arrived after" true by construction
    /// rather than by argument. → <see cref="IAnswerSheetStore.CloseAsync"/>
    /// </param>
    public static async Task RunAsync(
        ExamVersion version, ExamModule module, ExamSessionId sessionId,
        IAnswerSheetStore answers, ISectionResultStore results, CancellationToken ct,
        AnswerSheet? frozen = null)
    {
        if (module is not (ExamModule.Reading or ExamModule.Listening)) return;
        if (version.Section(module) is not { } section) return;

        var sheet = frozen?.Answers ?? await answers.LoadAsync(sessionId, module, ct);
        var score = DeterministicScorer.Score(section, version.Scoring, sheet);

        await results.SaveAsync(sessionId, score, ct);
    }
}

internal static class SessionProjection
{
    /// <summary>
    /// A section nobody has written to. Shared so the empty case allocates once.
    ///
    /// Revision zero, which is what a first write expects — so a section that
    /// has never been saved and one saved back to zero are the same thing to a
    /// caller, and the first autosave uses the same path as the hundredth.
    /// </summary>
    public static readonly AnswerSheet Empty = AnswerSheet.Empty;

    /// <summary>
    /// Loads a session the caller owns, or reports it missing.
    ///
    /// <b>Someone else's session is "not found", never "forbidden".</b> A 403
    /// confirms the id exists, which turns an id space into an oracle for
    /// enumerating other learners' sittings.
    /// </summary>
    public static async Task<(ExamSession Session, ExamVersion Version)> LoadOwnedAsync(
        this IExamSessionRepository sessions,
        IExamCatalogue catalogue, ExamSessionId id, UserId userId, CancellationToken ct)
    {
        var session = await sessions.FindAsync(id, ct);
        if (session is null || session.UserId != userId) throw new SessionNotFoundException();

        var version = await catalogue.FindAsync(session.ExamVersionId, ct)
            ?? throw new SessionNotFoundException();

        return (session, version);
    }

    public static SessionView ToView(
        this ExamSession session, ExamVersion version, DateTimeOffset now,
        AnswerSheet sheet, IReadOnlyList<SectionScore> _)
    {
        CurrentSectionView? current = null;

        if (session.Status == SessionStatus.InProgress
            && session.Current is { } attempt
            && version.Section(attempt.Module) is { } section)
        {
            /*
             * <b>Null where there is no deadline, not zero.</b> Zero is what a
             * countdown reads when time is up, and a practice sitting that
             * reported zero would render as an exam whose clock had run out —
             * inputs disabled, footer saying the section is over.
             */
            int? remaining = attempt.DeadlineAt is { } deadline
                ? (int)Math.Max(0, (deadline - now).TotalSeconds)
                : null;

            current = new CurrentSectionView(
                attempt.Module.ToString().ToLowerInvariant(),
                attempt.StartedAt,
                attempt.DeadlineAt,
                remaining,
                attempt.ElapsedSeconds(now),
                attempt.RunningSince is not null,
                attempt.TargetSeconds,
                [.. section.Parts.OrderBy(p => p.Order).Select(p => p.ToView())],
                sheet.Answers,
                sheet.Revision,
                sheet.Sequences ?? new Dictionary<string, long>(),
                attempt.Module == ExamModule.Speaking
                    ? [.. version.Timing.SpeakingParts.Select(p =>
                        new SpeakingPartTimingView(p.Part, p.PrepSeconds, p.ResponseSeconds))]
                    : [],
                attempt.Module == ExamModule.Listening
                    ? version.Timing.ListeningTransferSeconds
                    : null);
        }

        return new SessionView(
            session.Id.Value,
            session.ExamVersionId.Value,
            version.Title,
            session.Mode.ToString().ToLowerInvariant(),
            session.Status.ToString().ToLowerInvariant(),
            session.StartedAt,
            now,
            [.. session.Attempts.Where(a => a.SubmittedAt is not null)
                .Select(a => a.Module.ToString().ToLowerInvariant())],
            current);
    }

    public static SessionResultsView ToResults(
        this ExamSession session, ExamVersion version,
        IReadOnlyList<SectionScore> scores,
        IReadOnlyList<SectionMarking> markings,
        IReadOnlyList<MarkingJob>? jobs = null)
    {
        // <b>Writing's two task bands do not become a Writing band here.</b>
        // IELTS marks Task 1 and Task 2 separately and combines them on a ratio
        // it does not publish; `ScoringProfile.RequireWritingTaskWeights`
        // refuses to guess one (`H-8b`). So Writing contributes a module band
        // only when the exam version carries the weighting — otherwise the two
        // task bands are reported as what they are, and Writing has no band.
        var moduleBands = new List<BandScore>();
        moduleBands.AddRange(scores.Select(s => s.Band));

        if (WritingBand(version, markings) is { } writing) moduleBands.Add(writing);

        if (markings.FirstOrDefault(m => m.Module == ExamModule.Speaking) is { } speaking)
            moduleBands.Add(speaking.Band);

        // Four bands or none. A mean over two sections is not an overall band,
        // and presenting one would be inventing a number. → product law L3
        decimal? overall = moduleBands.Count == ExamVersion.FullTestOrder.Count
            ? BandScore.Overall(moduleBands).Value
            : null;

        return new SessionResultsView(
            session.Id.Value,
            version.Title,
            session.Mode.ToString().ToLowerInvariant(),
            session.Status.ToString().ToLowerInvariant(),
            session.SubmittedAt,
            [.. scores.OrderBy(s => s.Module).Select(s => s.ToView())],
            [.. markings.OrderBy(m => m.Module).ThenBy(m => m.TaskNumber).Select(m => m.ToView())],
            [.. (jobs ?? []).OrderBy(j => j.Module).Select(ToStatusView)],
            overall);
    }

    /// <summary>
    /// A job's state, and a sentence a learner may read.
    ///
    /// <b>The reason is mapped, not passed through.</b> A provider's raw error
    /// can carry a prompt fragment, a request id, or the learner's own words
    /// back at them; none of that belongs on a results screen. What the learner
    /// needs is which of four situations they are in, because each has a
    /// different answer to "what do I do now".
    /// </summary>
    private static MarkingStatusView ToStatusView(MarkingJob job) => new(
        job.Module.ToString().ToLowerInvariant(),
        job.State.ToString().ToLowerInvariant(),
        job.Attempts,
        job.State switch
        {
            MarkingJobState.Completed => null,

            // Nothing has gone wrong; it simply has not happened yet.
            MarkingJobState.Pending or MarkingJobState.Running => null,

            MarkingJobState.Retryable => "Chấm bài chưa xong. Hệ thống sẽ thử lại.",

            MarkingJobState.Failed when job.LastError?.Contains(
                nameof(MarkingAvailability.AwaitingEvaluator), StringComparison.Ordinal) == true =>
                "Chấm tự động chưa được bật cho phần này.",

            MarkingJobState.Failed when job.LastError?.Contains(
                nameof(MarkingAvailability.AwaitingTranscript), StringComparison.Ordinal) == true =>
                "Bản ghi của bạn chưa được chuyển thành văn bản để chấm.",

            MarkingJobState.Failed when job.LastError?.Contains(
                nameof(MarkingAvailability.AwaitingRubric), StringComparison.Ordinal) == true =>
                "Chưa có thang chấm cho phần này.",

            // Everything else that reached the end of its attempts. Deliberately
            // vague about the cause and specific about the consequence: the
            // learner cannot fix a provider outage, and telling them which one
            // it was helps nobody.
            MarkingJobState.Failed => "Chấm bài không thành công. Đội ngũ VNI đã được thông báo.",

            _ => null,
        });

    /// <summary>
    /// Writing's module band, or null while the ratio is unknown.
    ///
    /// <b>Two task bands are not a Writing band, and averaging them would be
    /// the invented default this codebase refuses everywhere else.</b> Task 2
    /// weighs more than Task 1 — that much is known — but IELTS does not
    /// publish the ratio the way it publishes the overall-band rounding rule.
    /// An exam version may carry one as data; when it does not, Writing has no
    /// module band and the results screen shows the two task bands instead of
    /// a number nobody can defend. → `H-8b`, `G-11`
    /// </summary>
    private static BandScore? WritingBand(
        ExamVersion version, IReadOnlyList<SectionMarking> markings)
    {
        var task1 = markings.FirstOrDefault(m => m.Module == ExamModule.Writing && m.TaskNumber == 1);
        var task2 = markings.FirstOrDefault(m => m.Module == ExamModule.Writing && m.TaskNumber == 2);

        if (task1 is null || task2 is null) return null;

        // Ask, and accept the refusal. `RequireWritingTaskWeights` throws when
        // the version carries no ratio; that is the correct behaviour for a
        // caller that must have one, and the wrong behaviour for a results
        // screen, which simply has one fewer band to show.
        if (version.Scoring.WritingTask1Weight is not { } w1) return null;
        if (version.Scoring.WritingTask2Weight is not { } w2) return null;
        if (w1 <= 0m || w2 <= 0m) return null;

        return BandScore.Weighted([(task1.Band, w1), (task2.Band, w2)]);
    }
}
