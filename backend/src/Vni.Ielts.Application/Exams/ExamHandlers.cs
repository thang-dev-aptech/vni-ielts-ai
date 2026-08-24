using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
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

public sealed record ListExamsQuery();

public sealed class ListExams(IExamCatalogue catalogue)
{
    public async Task<IReadOnlyList<ExamCatalogueItem>> HandleAsync(
        ListExamsQuery _, CancellationToken ct) =>
        [.. (await catalogue.ListSittableAsync(ct)).Select(v => v.ToCatalogueItem())];
}

public sealed record StartExamSessionCommand(
    UserId UserId, ExamVersionId ExamVersionId, SessionMode Mode, ExamModule? Module);

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
        var session = ExamSession.Start(command.UserId, version, command.Mode, firstModule, now);

        await sessions.AddAsync(session, ct);

        return session.ToView(version, now, SessionProjection.Empty, []);
    }
}

public sealed record GetExamSessionQuery(UserId UserId, ExamSessionId SessionId);

public sealed class GetExamSession(
    IExamCatalogue catalogue,
    IExamSessionRepository sessions,
    IAnswerSheetStore answers,
    IClock clock)
{
    public async Task<SessionView> HandleAsync(GetExamSessionQuery query, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(catalogue, query.SessionId, query.UserId, ct);
        var now = clock.UtcNow;

        // The server noticing an expiry, not a learner submitting late: keep
        // everything already saved and close the sitting.
        if (session.Status == SessionStatus.InProgress && !session.IsWithinDeadline(now))
        {
            session.Expire(now);
            await sessions.SaveAsync(session, ct);
        }

        var saved = session.Current is { } current
            ? await answers.LoadAsync(session.Id, current.Module, ct)
            : SessionProjection.Empty;

        return session.ToView(version, now, saved, []);
    }
}

public sealed record SaveAnswersCommand(
    UserId UserId, ExamSessionId SessionId, ExamModule Module,
    IReadOnlyDictionary<string, string?> Answers);

/// <summary>
/// An autosave from the section the learner is in.
///
/// Three refusals, and each one is a way the sheet could otherwise be
/// corrupted: a finished sitting cannot take writes, a section that is not the
/// open one cannot take writes — that is how a Full Test candidate would edit
/// Reading while sitting Writing — and a write after the deadline is refused
/// outright rather than quietly accepted.
/// </summary>
public sealed class SaveAnswers(
    IExamCatalogue catalogue, IExamSessionRepository sessions, IAnswerSheetStore answers, IClock clock)
{
    public async Task HandleAsync(SaveAnswersCommand command, CancellationToken ct)
    {
        var (session, _) = await sessions.LoadOwnedAsync(catalogue, command.SessionId, command.UserId, ct);

        if (session.Status != SessionStatus.InProgress)
            throw new SessionNotInProgressException(session.Status.ToString());

        var current = session.Current;
        if (current is null || current.Module != command.Module)
            throw new SessionExpiredException();

        var now = clock.UtcNow;
        if (!current.IsWithinDeadline(now)) throw new SessionExpiredException();

        await answers.SaveAsync(command.SessionId, command.Module, command.Answers, now, ct);
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
    IClock clock)
{
    public async Task<SessionView> HandleAsync(AdvanceSectionCommand command, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(catalogue, command.SessionId, command.UserId, ct);

        if (session.Status != SessionStatus.InProgress)
            throw new SessionNotInProgressException(session.Status.ToString());

        var now = clock.UtcNow;
        var leaving = session.Current;

        var outcome = session.AdvanceToNextSection(version, now);
        if (outcome is AdvanceOutcome.NotAFullTest)
            throw new InvalidOperationException(
                "A single-skill session does not advance. Its next step is a new test.");

        if (leaving is not null)
            await ScoreIfDeterministic.RunAsync(
                version, leaving.Module, session.Id, answers, results, ct);

        await sessions.SaveAsync(session, ct);

        var saved = session.Current is { } next
            ? await answers.LoadAsync(session.Id, next.Module, ct)
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
    IClock clock)
{
    public async Task<SessionResultsView> HandleAsync(
        SubmitExamSessionCommand command, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(catalogue, command.SessionId, command.UserId, ct);
        var now = clock.UtcNow;

        if (session.Status == SessionStatus.InProgress)
        {
            var open = session.Current;

            // The learner pressing submit after the deadline is refused; the
            // server closing an expired sitting is not. Same instant, two
            // different events.
            if (open is not null && !open.IsWithinDeadline(now))
            {
                session.Expire(now);
                await sessions.SaveAsync(session, ct);
                throw new SessionExpiredException();
            }

            session.Submit(now);

            if (open is not null)
                await ScoreIfDeterministic.RunAsync(
                    version, open.Module, session.Id, answers, results, ct);

            await sessions.SaveAsync(session, ct);
        }

        return session.ToResults(version, await results.ListAsync(session.Id, ct));
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

public sealed class GetSessionResults(IExamCatalogue catalogue, IExamSessionRepository sessions, ISectionResultStore results)
{
    public async Task<SessionResultsView> HandleAsync(GetSessionResultsQuery query, CancellationToken ct)
    {
        var (session, version) = await sessions.LoadOwnedAsync(catalogue, query.SessionId, query.UserId, ct);
        return session.ToResults(version, await results.ListAsync(session.Id, ct));
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
    public static async Task RunAsync(
        ExamVersion version, ExamModule module, ExamSessionId sessionId,
        IAnswerSheetStore answers, ISectionResultStore results, CancellationToken ct)
    {
        if (module is not (ExamModule.Reading or ExamModule.Listening)) return;
        if (version.Section(module) is not { } section) return;

        var sheet = await answers.LoadAsync(sessionId, module, ct);
        var score = DeterministicScorer.Score(section, version.Scoring, sheet);

        await results.SaveAsync(sessionId, score, ct);
    }
}

internal static class SessionProjection
{
    /// <summary>A section not yet started has no sheet. Shared so the empty case allocates once.</summary>
    public static readonly IReadOnlyDictionary<string, string?> Empty =
        new Dictionary<string, string?>();

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
        IReadOnlyDictionary<string, string?> answers, IReadOnlyList<SectionScore> _)
    {
        CurrentSectionView? current = null;

        if (session.Status == SessionStatus.InProgress
            && session.Current is { } attempt
            && version.Section(attempt.Module) is { } section)
        {
            var remaining = (int)Math.Max(0, (attempt.DeadlineAt - now).TotalSeconds);

            current = new CurrentSectionView(
                attempt.Module.ToString().ToLowerInvariant(),
                attempt.StartedAt,
                attempt.DeadlineAt,
                remaining,
                [.. section.Parts.OrderBy(p => p.Order).Select(p => p.ToView())],
                answers,
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
        this ExamSession session, ExamVersion version, IReadOnlyList<SectionScore> scores)
    {
        // Four bands or none. A mean over two sections is not an overall band,
        // and presenting one would be inventing a number. → product law L3
        var bands = scores.Select(s => s.Band).ToList();
        decimal? overall = bands.Count == ExamVersion.FullTestOrder.Count
            ? BandScore.Overall(bands).Value
            : null;

        return new SessionResultsView(
            session.Id.Value,
            version.Title,
            session.Mode.ToString().ToLowerInvariant(),
            session.Status.ToString().ToLowerInvariant(),
            session.SubmittedAt,
            [.. scores.OrderBy(s => s.Module).Select(s => s.ToView())],
            overall);
    }
}
