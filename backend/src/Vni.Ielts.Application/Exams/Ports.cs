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

    /// <summary>
    /// Writes a version, refusing to change the content of a published one.
    ///
    /// <b>The entity has said "immutable once published" since it was written,
    /// and until 2026-08-28 nothing enforced it.</b> This was a replace-or-
    /// insert, so any caller could rewrite a published version wholesale — and
    /// one did, on every restart: the development seeder loads the fixtures and
    /// publishes them under a deterministic id. Editing a fixture and
    /// restarting therefore changed the exam <i>underneath</i> every sitting
    /// running it. The learner's screen kept the old passage; the marker used
    /// the new answer key.
    ///
    /// That is invisible until somebody disputes a band, which is the worst
    /// moment to find it.
    ///
    /// <b>Status still moves.</b> Publishing and unpublishing change what a
    /// version is <i>for</i>, not what it <i>says</i>, and both have to keep
    /// working on a published one.
    /// </summary>
    /// <exception cref="PublishedExamVersionIsImmutableException">
    /// The version is published and the content differs from what is stored.
    /// </exception>
    Task UpsertAsync(ExamVersion version, CancellationToken ct);
}

/// <summary>
/// Somebody tried to change what a published exam says.
///
/// <b>Refused rather than versioned automatically.</b> Turning an edit into a
/// new version silently would be the same class of surprise in the other
/// direction: an author would believe they had corrected an exam and the
/// learners sitting it would be on the old one. Correcting a published exam is
/// a deliberate act with a version number attached, and this is what makes the
/// author perform it.
/// </summary>
public sealed class PublishedExamVersionIsImmutableException(ExamVersionId id)
    : Exception(
        $"Exam version {id.Value} is published and its content cannot be changed. "
        + "Publish a new version instead — sittings and results reference the exact version "
        + "they used, so rewriting one would silently change historical scores.")
{
    public ExamVersionId VersionId { get; } = id;
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

    /// <summary>
    /// Writes the sitting back, but only while it is still in the state the
    /// caller found it in. Returns false when it has moved on.
    ///
    /// <b>Every write to a sitting is a transition, and a transition read three
    /// statements ago is a guess.</b> Advance, submit and the expiry sweep all
    /// worked the same way: read the sitting, decide, replace the whole
    /// document. Two of them at once — two tabs, a retry from a phone that
    /// changed network, the sweep meeting a learner pressing "Nộp bài" — and
    /// both decisions were made against the same state and both were written.
    ///
    /// The damage was not a corrupt document; the replaces mostly agreed. It
    /// was that <i>both callers went on to mark the section</i>, and marking is
    /// where the money and the band are. It also let a submit and an advance
    /// interleave into a sitting that had been marked as finished and still had
    /// a section open.
    ///
    /// <b><paramref name="from"/> is the guard, and it is deliberately not a
    /// revision counter.</b> A counter would answer "has anything changed",
    /// which is too strong — it would refuse a submit because an autosave
    /// landed. What matters is narrower and says itself: is this sitting still
    /// in progress, and is the section I am closing still the open one. Two
    /// racing advances both name Reading; the first closes it, and the second
    /// no longer matches anything.
    /// </summary>
    Task<bool> TrySaveAsync(ExamSession session, SessionState from, CancellationToken ct);
}

/// <summary>
/// The state a transition believed it was leaving.
///
/// <b>Captured before the aggregate is mutated, never after.</b> Reading it
/// off a session that has already been advanced describes where it is going,
/// which matches nothing and refuses every write — or, worse, matches the state
/// a competing writer has just produced and lets both through.
/// </summary>
public sealed record SessionState(
    SessionStatus Status, ExamModule? OpenModule, bool OpenSectionRunning)
{
    public static SessionState Of(ExamSession session) =>
        new(session.Status, session.Current?.Module, session.Current?.RunningSince is not null);
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
/// <b>A save writes the questions that changed, not the whole sheet.</b> That
/// was the other way round until 27/08/2026, and the reason it changed is worth
/// keeping: a whole-sheet write cannot merge, only replace, so any client whose
/// view was a few seconds old erased everything typed since. Adding a
/// compare-and-swap made the erase *detectable* and left it just as fatal — the
/// client took the new revision and re-sent the same whole sheet.
///
/// A patch has nothing to detect. Two writers on different questions both land;
/// two writers on the same question resolve to the later one. No version vector
/// was needed for that, only the right unit of write.
/// </summary>
public interface IAnswerSheetStore
{
    Task<IReadOnlyDictionary<string, string?>> LoadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct);

    /// <summary>
    /// The sheet together with the revision it is currently at.
    ///
    /// A caller that intends to write needs both. A caller that only marks
    /// does not, which is why <see cref="LoadAsync"/> stays.
    /// </summary>
    Task<AnswerSheet> ReadAsync(
        ExamSessionId sessionId, ExamModule module, CancellationToken ct);

    /// <summary>
    /// Applies <paramref name="changes"/> question by question and returns the
    /// merged sheet.
    ///
    /// <b>Every entry is written; none is compared.</b> A key present with a
    /// null value is an answer the learner cleared, and clearing it is the
    /// intent — which is exactly why a whole-sheet write could not be made safe.
    /// In a full sheet a blank is indistinguishable from "I never knew about
    /// this question", so a stale client's blanks deleted a live client's
    /// answers and no rule could tell the two apart. In a patch a blank appears
    /// only because someone cleared it.
    ///
    /// An empty patch is a no-op and must not move the revision: a write that
    /// changed nothing would still tell every other reader it was behind.
    ///
    /// The comparison that remains belongs to the database, not to application
    /// code. Read, check, then write is three statements a competing writer
    /// fits between.
    /// </summary>
    /// <param name="sequences">
    /// A per-question ordering token, or null to keep arrival order.
    ///
    /// <b>Mongo's arrival order is not the learner's edit order, and treating
    /// it as such loses the newer answer.</b> Two writes for the same question
    /// can be reordered by anything between the keyboard and the database — a
    /// retry on a changed network, a proxy, two tabs, a request that stalled
    /// while its successor went straight through. The stored value then becomes
    /// whichever one the server happened to apply last, which is the older
    /// answer as often as the newer one, and the learner watches their
    /// correction revert.
    ///
    /// A revision cannot fix this: it is one number for the whole sheet, so it
    /// says whether a caller was behind, not which of two edits to one question
    /// came second. The token is per question and monotonic, and an entry whose
    /// token is not greater than the stored one is <b>ignored rather than
    /// refused</b> — it is not an error for an older answer to arrive late, it
    /// is just no longer the answer.
    ///
    /// <b>A bare client timestamp would not do.</b> Clocks disagree between two
    /// tabs on one machine and between two machines, and a client that is
    /// behind would have every edit ignored for as long as the skew lasts. The
    /// token is a counter the client raises past whatever the server last told
    /// it — a Lamport clock, not a wall clock.
    ///
    /// Null keeps the previous behaviour, which is what a client that has not
    /// been updated sends.
    /// </param>
    Task<PatchedSheet> PatchAsync(
        ExamSessionId sessionId, ExamModule module,
        IReadOnlyDictionary<string, string?> changes,
        DateTimeOffset at, CancellationToken ct,
        IReadOnlyDictionary<string, long>? sequences = null);

    /// <summary>
    /// Writes one entry without touching the rest of the sheet.
    ///
    /// <b>Kept even though <see cref="PatchAsync"/> could now express it.</b>
    /// The two have different callers and different rules: a patch carries what
    /// a learner composed and is refused outright for Speaking, whose sheet
    /// holds recording ids the <i>server</i> wrote. Routing the server's own
    /// index through the learner's endpoint is how a caller ends up able to
    /// point their marking at any recording id they can name.
    /// </summary>
    /// <para>
    /// <b>It takes no revision, and that is not an omission.</b> A field-level
    /// update cannot lose a neighbour's write, so there is no conflict for a
    /// caller to resolve — a compare here would invent a failure and then have
    /// nothing to do about it.
    /// </para>
    Task SetAnswerAsync(
        ExamSessionId sessionId, ExamModule module, string questionId, string? value,
        DateTimeOffset at, CancellationToken ct);

    /// <summary>
    /// Freezes the sheet and returns it as frozen. Idempotent.
    ///
    /// <b>The answer sheet and the sitting live in different collections, and
    /// until 27/08/2026 nothing joined them.</b> The transition compare-and-swap
    /// guards the session document; a patch guards nothing, because two writers
    /// touching different questions are both right. So this interleaving lost
    /// work and reported success:
    ///
    ///   1. an autosave loads the sitting and finds its section open
    ///   2. a submit wins the transition CAS and marks the sheet at revision R
    ///   3. the autosave's patch lands — revision R+1
    ///
    /// The learner's chip reads "Đã lưu" and the result was computed without
    /// that answer. Nothing throws, nothing is logged, and the only evidence is
    /// a band that is one mark low.
    ///
    /// <b>Closing is a single atomic statement on the sheet, and it happens
    /// before the transition.</b> After it, <see cref="PatchAsync"/> refuses —
    /// so a patch either commits before the freeze or is refused before the
    /// client is told anything landed. There is no third outcome, which is the
    /// property the whole protocol exists to provide.
    ///
    /// <b>Before, not after, the transition CAS.</b> Closing afterwards leaves
    /// exactly the window above: a patch that has already passed its section
    /// check is in flight while the CAS runs. Closing first means the loser of
    /// the CAS has closed a sheet the winner was closing anyway — the same
    /// section, by definition — so the redundant close costs one idempotent
    /// write and no correctness.
    ///
    /// <b>Idempotent, and it returns what is already frozen.</b> Two tabs on
    /// "Nộp bài", a submit meeting the expiry sweep, and a retried request all
    /// arrive here. Re-freezing at a later revision would be the bug: the
    /// content marking read must not change under it.
    ///
    /// A section with no answers at all still closes. Otherwise a learner who
    /// wrote nothing would leave a sheet that a late write could still reach.
    /// </summary>
    Task<AnswerSheet> CloseAsync(
        ExamSessionId sessionId, ExamModule module, DateTimeOffset at, CancellationToken ct);
}

/// <summary>
/// A write arrived for a section whose sheet has already been frozen.
///
/// <b>Refused, and refused before the client is told anything landed.</b> The
/// alternative is the failure this exception exists to make impossible: an
/// answer accepted after the section it belongs to has been marked, so the
/// learner is told it was saved and the result does not contain it.
/// </summary>
public sealed class SectionSheetClosedException(ExamModule module)
    : Exception($"The {module} answer sheet has been closed and can take no more writes.")
{
    public ExamModule Module { get; } = module;
}

/// <summary>
/// A sheet, and the version of it that was read.
///
/// <b>The revision is the sheet's own, never the sitting's.</b> The two are
/// written at completely different rates — an autosave every few seconds
/// against a handful of transitions in an hour — so one shared counter would
/// mean every keystroke invalidated a submit already in flight, and the learner
/// would be told to retry the thing they cannot retry.
/// </summary>
public sealed record AnswerSheet(
    IReadOnlyDictionary<string, string?> Answers,
    int Revision,
    IReadOnlyDictionary<string, long>? Sequences = null)
{
    /// <summary>
    /// A section nobody has written to yet.
    ///
    /// Revision zero is what a first write expects, so an absent sheet and a
    /// sheet at zero are the same thing to a caller — which is what lets the
    /// first autosave of a section use the same code path as the hundredth.
    /// </summary>
    public static readonly AnswerSheet Empty = new(new Dictionary<string, string?>(), 0);

    /// <summary>The stored ordering token for one question, or -1 if it has none.</summary>
    public long SequenceOf(string questionId) =>
        Sequences is not null && Sequences.TryGetValue(questionId, out var seq) ? seq : -1;
}

/// <summary>
/// What a patch produced, and what it was applied to.
///
/// <b><paramref name="PreviousRevision"/> is the whole reason a revision still
/// exists.</b> The write itself never fails on it, so it is not a lock — it is
/// how a caller learns that somebody else wrote between its last read and this
/// one, and therefore that the sheet it is holding is missing answers. Told
/// that, a second tab can take in what the first tab typed. Not told, it stays
/// silently out of date until the learner reloads the page.
///
/// It is stated rather than left for the caller to work out as
/// <c>Sheet.Revision - 1</c>. That arithmetic is right only while the increment
/// is one, and the day it stops being one is the day every caller doing it
/// starts being wrong without anything failing.
/// </summary>
public sealed record PatchedSheet(AnswerSheet Sheet, int PreviousRevision);

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
    /// <summary>
    /// Null when the reference resolves to nothing. Never throws on a bad path.
    ///
    /// <b>Asynchronous, and it was not.</b> A synchronous signature is fine for
    /// a file on the same disk and impossible for object storage: opening an S3
    /// object is a network round trip, and a blocking one on a request thread
    /// is how a slow bucket becomes a thread-pool exhaustion. The port had to
    /// change shape before an adapter could exist behind it.
    /// </summary>
    Task<ExamAsset?> OpenAsync(string reference, CancellationToken ct);
}

/// <summary>The caller owns the stream and must dispose it.</summary>
/// <param name="ContentLength">
/// Null when the store cannot say. Present, it lets the response carry a
/// `Content-Length`, which is what makes an audio element's seek bar appear
/// before the file has finished arriving — and a Listening section with no seek
/// bar is one a learner cannot navigate.
/// </param>
/// <param name="ETag">
/// The store's own identifier for these exact bytes, when it has one.
///
/// <b>What makes a re-listen free.</b> Exam audio does not change — a published
/// version is immutable — so a browser that already has the file should be able
/// to ask "is it still this one" and be told yes in a header rather than
/// downloading megabytes again. It is also the integrity check: an object whose
/// ETag has moved is one that was replaced, which for a published exam should
/// never happen.
/// </param>
public sealed record ExamAsset(
    Stream Content, string ContentType, long? ContentLength = null, string? ETag = null);

/// <summary>
/// Speaking answers, which are audio rather than text.
///
/// <b>Why a separate port and not just another answer value.</b> A recording
/// is megabytes and arrives once; an answer sheet is kilobytes and is rewritten
/// every few seconds. Storing audio in the sheet would rewrite the audio on
/// every autosave. What lands in the sheet is the id this returns, so the two
/// stay the shape each of them needs.
///
/// <b><see cref="SubmitSpeakingRecording"/> is what puts it there, and it took
/// until 27/08/2026 for anything to.</b> The upload endpoint stored the audio
/// and handed the id back to the client, and that was the end of it: no
/// server-held record connected a recording to the section it answered, so
/// <see cref="Assessment.SectionMarkingRunner"/> had nothing to look up and
/// Speaking would have reported "awaiting transcript" forever — including for
/// a learner who recorded nothing at all. The chain has to be closed on the
/// server, not by trusting the client to echo the id back on an autosave: an
/// id it can choose is an id it can borrow from another sitting.
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

    /// <summary>
    /// Removes a recording whose id will never reach an answer sheet.
    ///
    /// <b>An upload is two writes, and only the second one can be refused.</b>
    /// The bytes go to storage first; the id is then filed into the sheet. If
    /// the section froze while the audio was streaming — twelve megabytes over
    /// a phone network is not a short window — the second write is refused by
    /// the closure protocol, correctly, because a recording filed after marking
    /// has read the sheet would never be marked.
    ///
    /// Without this the bytes stay: an object nothing references, holding a
    /// learner's voice, which is personal data under PDPL and therefore not
    /// something to leave lying around by accident. Deleting it is what makes
    /// the refusal a refusal rather than a half-write.
    ///
    /// Idempotent, and silent about an id that is already gone: the caller is
    /// cleaning up after a failure and has nothing to do about a second one.
    /// → ADR-0015, <c>SubmitSpeakingRecording</c>
    /// </summary>
    Task DeleteAsync(string recordingId, CancellationToken ct);

    /// <summary>
    /// Every stored recording older than <paramref name="olderThan"/>, with its
    /// sitting and question.
    ///
    /// <b>For the sweep that reconciles storage against the answer sheets.</b>
    /// Four things leave a recording nothing references, and only a sweep can
    /// see any of them:
    ///
    ///   • an upload that streamed its bytes and was then refused because the
    ///     section had frozen — the handler deletes it, and the delete can fail;
    ///   • a crash between writing the new revision and removing the old one;
    ///   • a sitting deleted, or a learner's account closed, while audio for it
    ///     is still on disk;
    ///   • anything written before the key became derived, when every re-record
    ///     stranded its predecessor.
    ///
    /// <b>The age bound is what makes the sweep safe.</b> A recording that has
    /// just been written may be seconds away from being filed into a sheet;
    /// deleting it because the sheet does not name it <i>yet</i> would destroy
    /// the learner's only copy of a spoken answer while they were still
    /// uploading it. → `I2.4`
    /// </summary>
    Task<IReadOnlyList<StoredRecording>> ListOlderThanAsync(
        DateTimeOffset olderThan, int limit, CancellationToken ct);
}

/// <summary>One recording as storage holds it, for the reconciliation sweep.</summary>
public sealed record StoredRecording(
    string RecordingId,
    ExamSessionId SessionId,
    string QuestionId,
    DateTimeOffset UploadedAt);
