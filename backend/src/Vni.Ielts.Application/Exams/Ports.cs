using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// Repository ports for exam content and sittings.
///
/// Same split as <see cref="Identity.IUserRepository"/>: the interface lives
/// here, every persistence model lives in Infrastructure, and there is no
/// generic repository — see the note on that file for why. → ADR-0004
/// </summary>
public interface IExamCatalogue
{
    /// <summary>Only published versions. A draft has not been reviewed and cannot be sat.</summary>
    Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct);

    /// <summary>Everything, drafts included. The CMS surface, never a learner one.</summary>
    Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct);

    Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct);

    /// <summary>Used by the development seeder and, later, by CMS publishing.</summary>
    Task UpsertAsync(ExamVersion version, CancellationToken ct);
}

public interface IExamSessionRepository
{
    Task<ExamSession?> FindAsync(ExamSessionId id, CancellationToken ct);

    /// <summary>
    /// The learner's unfinished sitting, if any. The dashboard's "bài đang làm
    /// dở" reads this, and starting a new session while one is open is a
    /// product decision that has not been made — so this exists to make the
    /// state visible rather than to enforce anything yet.
    /// </summary>
    Task<ExamSession?> FindOpenForUserAsync(UserId userId, CancellationToken ct);

    Task<IReadOnlyList<ExamSession>> ListForUserAsync(UserId userId, int limit, CancellationToken ct);

    Task AddAsync(ExamSession session, CancellationToken ct);
    Task SaveAsync(ExamSession session, CancellationToken ct);
}

/// <summary>
/// Saved answers, one sheet per section attempt.
///
/// <b>Separate from the session aggregate on purpose.</b> Answers are written
/// far more often than the session changes — every few keystrokes, from a
/// phone on a flaky connection — and folding them into the aggregate would
/// mean rewriting the whole sitting on each autosave, which is both wasteful
/// and a lost-update race between two tabs.
///
/// A save is a whole-sheet replace rather than a per-question append: the
/// client holds the authoritative draft for the section it is in, and merging
/// partial writes server-side would need a per-question version vector nobody
/// has asked for.
/// </summary>
public interface IAnswerSheetStore
{
    Task<IReadOnlyDictionary<string, string?>> LoadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct);

    Task SaveAsync(
        ExamSessionId sessionId, ExamModule module,
        IReadOnlyDictionary<string, string?> answers, DateTimeOffset at, CancellationToken ct);
}

/// <summary>
/// Marked sections.
///
/// Written once, at submission, and never updated: a result references the
/// exam version it was produced from, so correcting a conversion table
/// produces a new version rather than silently rewriting history.
/// </summary>
public interface ISectionResultStore
{
    Task SaveAsync(ExamSessionId sessionId, SectionScore score, CancellationToken ct);

    Task<IReadOnlyList<SectionScore>> ListAsync(ExamSessionId sessionId, CancellationToken ct);
}

/// <summary>
/// Exam media — Listening audio, and later images for labelling questions.
///
/// A port rather than a file path so the development fixture store and the
/// eventual object-storage reader are the same shape to everything above.
/// </summary>
public interface IExamAssetStore
{
    /// <summary>Null when the reference resolves to nothing. Never throws on a bad path.</summary>
    ExamAsset? Open(string reference);
}

/// <summary>The caller owns the stream and must dispose it.</summary>
public sealed record ExamAsset(Stream Content, string ContentType);

/// <summary>
/// Speaking answers, which are audio rather than text.
///
/// <b>Why a separate port and not just another answer value.</b> A recording
/// is megabytes and arrives once; an answer sheet is kilobytes and is rewritten
/// every few seconds. Storing audio in the sheet would rewrite the audio on
/// every autosave. What lands in the sheet is the id this returns, so the two
/// stay the shape each of them needs.
///
/// <b>The key is server-generated.</b> Never the uploaded filename — a
/// client-supplied name is how an upload chooses where it lands.
/// → `zip-ingestion-security.md`, threat `T9`
/// </summary>
public interface IRecordingStore
{
    Task<string> SaveAsync(
        ExamSessionId sessionId, string questionId, Stream content, string contentType,
        CancellationToken ct);
}
