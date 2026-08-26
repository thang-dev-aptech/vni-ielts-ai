using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Infrastructure.Persistence.Exams;

/// <summary>
/// Domain to document and back.
///
/// <b>Every <c>DateTimeOffset</c> crosses here explicitly.</b> Mongo stores a
/// BSON date as UTC milliseconds with no offset, so a round trip through
/// <c>DateTime</c> loses the offset unless the kind is stated on the way back.
/// Reading a deadline back as <c>Unspecified</c> and comparing it to
/// <c>DateTimeOffset.UtcNow</c> shifts every exam deadline by the server's
/// timezone — which is invisible on a UTC machine and seven hours wrong in
/// Hanoi.
/// </summary>
internal static class ExamMappers
{
    private static DateTime Utc(DateTimeOffset value) => value.UtcDateTime;

    private static DateTimeOffset Offset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    // ── Exam version ─────────────────────────────────────────────────────

    public static ExamVersionDocument ToDocument(this ExamVersion version) => new()
    {
        Id = version.Id.Value,
        DefinitionId = version.DefinitionId.Value,
        VersionNumber = version.VersionNumber,
        Title = version.Title,
        Variant = version.Variant.ToString(),
        Status = version.Status.ToString(),
        CreatedBy = version.CreatedBy.Value,
        SubmittedBy = version.SubmittedBy?.Value,
        SubmittedAt = version.SubmittedAt is { } sa ? Utc(sa) : null,
        ReviewedBy = version.ReviewedBy?.Value,
        ReviewedAt = version.ReviewedAt is { } ra ? Utc(ra) : null,
        PublishedAt = version.PublishedAt is { } at ? Utc(at) : null,
        ReviewNotes =
        [
            .. version.ReviewNotes.Select(n => new ReviewNoteDocument
            {
                Id = n.Id, AuthorId = n.AuthorId.Value, Body = n.Body, Anchor = n.Anchor, At = Utc(n.At),
            }),
        ],
        Timing = new TimingDocument
        {
            Sections =
            [
                .. version.Timing.SectionDurationSeconds.Select(kv =>
                    new ModuleSecondsDocument { Module = kv.Key.ToString(), Seconds = kv.Value }),
            ],
            ListeningTransferSeconds = version.Timing.ListeningTransferSeconds,
            SpeakingParts =
            [
                .. version.Timing.SpeakingParts.Select(p => new SpeakingPartDocument
                {
                    Part = p.Part, PrepSeconds = p.PrepSeconds, ResponseSeconds = p.ResponseSeconds,
                }),
            ],
        },
        Scoring = new ScoringDocument
        {
            RawToBand =
            [
                .. version.Scoring.RawToBand.Select(kv => new ModuleBandTableDocument
                {
                    Module = kv.Key.ToString(),
                    Boundaries =
                    [
                        .. kv.Value.Select(b => new BandBoundaryDocument
                        {
                            MinRaw = b.MinRaw, Band = b.Band.Value,
                        }),
                    ],
                }),
            ],
            Matching = version.Scoring.Matching.ToDocument(),
            WritingTask1Weight = version.Scoring.WritingTask1Weight,
            WritingTask2Weight = version.Scoring.WritingTask2Weight,
        },
        Sections =
        [
            .. version.Sections.Select(s => new SectionDocument
            {
                Module = s.Module.ToString(),
                Order = s.Order,
                Parts = [.. s.Parts.Select(ToDocument)],
            }),
        ],
    };

    private static MatchingRulesDocument ToDocument(this AnswerMatchingRules rules) => new()
    {
        CaseSensitive = rules.CaseSensitive,
        TrimWhitespace = rules.TrimWhitespace,
        CollapseInnerWhitespace = rules.CollapseInnerWhitespace,
        AllowSpellingVariants = rules.AllowSpellingVariants,
    };

    private static PartDocument ToDocument(SectionPart part) => new()
    {
        Order = part.Order,
        Kind = part.Kind,
        Title = part.Title,
        Body = part.Body,
        AudioKey = part.AudioKey,
        ImageKey = part.ImageKey,
        Transcript = part.Transcript,
        TaskNumber = part.TaskNumber,
        PartNumber = part.PartNumber,
        CueCard = part.CueCard is { } c
            ? new CueCardDocument { Topic = c.Topic, Bullets = [.. c.Bullets] }
            : null,
        MinWords = part.MinWords,
        Questions =
        [
            .. part.Questions.Select(q => new QuestionDocument
            {
                Id = q.Id,
                Order = q.Order,
                Type = q.Type.ToString(),
                Prompt = q.Prompt,
                Options = [.. q.Options.Select(o => new OptionDocument { Key = o.Key, Text = o.Text })],
                MaxWords = q.MaxWords,
                AnswerKey = q.AnswerKey is { } key
                    ? new AnswerKeyDocument
                    {
                        Accepted =
                        [
                            .. key.Accepted.Select(a => new AcceptedAnswerDocument
                            {
                                Single = a.Single,
                                All = a.All is null ? null : [.. a.All],
                                PairLeft = a.Pair?.Left,
                                PairRight = a.Pair?.Right,
                            }),
                        ],
                        Overrides = key.Overrides?.ToDocument(),
                    }
                    : null,
            }),
        ],
    };

    public static ExamVersion ToDomain(this ExamVersionDocument doc)
    {
        var timing = new TimingProfile(
            doc.Timing.Sections.ToDictionary(
                s => Enum.Parse<ExamModule>(s.Module), s => s.Seconds),
            doc.Timing.ListeningTransferSeconds,
            [.. doc.Timing.SpeakingParts.Select(p =>
                new SpeakingPartTiming(p.Part, p.PrepSeconds, p.ResponseSeconds))]);

        var scoring = new ScoringProfile(
            doc.Scoring.RawToBand.ToDictionary(
                t => Enum.Parse<ExamModule>(t.Module),
                t => (IReadOnlyList<BandBoundary>)
                    [.. t.Boundaries.Select(b => new BandBoundary(b.MinRaw, BandScore.Create(b.Band)))]),
            doc.Scoring.Matching.ToDomain(),
            doc.Scoring.WritingTask1Weight,
            doc.Scoring.WritingTask2Weight);

        return ExamVersion.Rehydrate(
            new ExamVersionId(doc.Id),
            new ExamDefinitionId(doc.DefinitionId),
            doc.VersionNumber,
            doc.Title,
            Enum.Parse<ExamVariant>(doc.Variant),
            Enum.Parse<ExamVersionStatus>(doc.Status),
            new UserId(doc.CreatedBy),
            doc.SubmittedBy is { } sb ? new UserId(sb) : null,
            doc.SubmittedAt is { } sa ? Offset(sa) : null,
            doc.ReviewedBy is { } rb ? new UserId(rb) : null,
            doc.ReviewedAt is { } ra ? Offset(ra) : null,
            doc.PublishedAt is { } at ? Offset(at) : null,
            scoring,
            timing,
            [.. doc.Sections.Select(ToDomain)],
            [.. doc.ReviewNotes.Select(n =>
                new ReviewNote(n.Id, new UserId(n.AuthorId), n.Body, n.Anchor, Offset(n.At)))]);
    }

    private static AnswerMatchingRules ToDomain(this MatchingRulesDocument doc) =>
        new(doc.CaseSensitive, doc.TrimWhitespace, doc.CollapseInnerWhitespace, doc.AllowSpellingVariants);

    private static Section ToDomain(SectionDocument doc) =>
        new(Enum.Parse<ExamModule>(doc.Module), doc.Order, [.. doc.Parts.Select(ToDomain)]);

    private static SectionPart ToDomain(PartDocument doc) =>
        new(
            doc.Order, doc.Kind, doc.Title, doc.Body, doc.AudioKey, doc.ImageKey, doc.Transcript,
            doc.TaskNumber, doc.PartNumber,
            doc.CueCard is { } c ? new CueCard(c.Topic, [.. c.Bullets]) : null,
            doc.MinWords,
            [
                .. doc.Questions.Select(q => new Question(
                    q.Id,
                    q.Order,
                    Enum.Parse<QuestionType>(q.Type),
                    q.Prompt,
                    [.. q.Options.Select(o => new QuestionOption(o.Key, o.Text))],
                    q.MaxWords,
                    q.AnswerKey is { } key
                        ? new AnswerKey(
                            [
                                .. key.Accepted.Select(a => new AcceptedAnswer(
                                    a.Single,
                                    a.All is null ? null : [.. a.All],
                                    a.PairLeft is not null && a.PairRight is not null
                                        ? (a.PairLeft, a.PairRight)
                                        : null)),
                            ],
                            key.Overrides?.ToDomain())
                        : null)),
            ]);

    // ── Session ──────────────────────────────────────────────────────────

    public static ExamSessionDocument ToDocument(this ExamSession session) => new()
    {
        Id = session.Id.Value,
        UserId = session.UserId.Value,
        ExamVersionId = session.ExamVersionId.Value,
        Mode = session.Mode.ToString(),
        Status = session.Status.ToString(),
        StartedAt = Utc(session.StartedAt),
        SubmittedAt = session.SubmittedAt is { } at ? Utc(at) : null,
        Attempts =
        [
            .. session.Attempts.Select(a => new AttemptDocument
            {
                Module = a.Module.ToString(),
                StartedAt = Utc(a.StartedAt),
                DeadlineAt = Utc(a.DeadlineAt),
                SubmittedAt = a.SubmittedAt is { } s ? Utc(s) : null,
            }),
        ],
    };

    public static ExamSession ToDomain(this ExamSessionDocument doc) =>
        ExamSession.Rehydrate(
            new ExamSessionId(doc.Id),
            new UserId(doc.UserId),
            new ExamVersionId(doc.ExamVersionId),
            Enum.Parse<SessionMode>(doc.Mode),
            Enum.Parse<SessionStatus>(doc.Status),
            Offset(doc.StartedAt),
            doc.SubmittedAt is { } at ? Offset(at) : null,
            doc.Attempts.Select(a => SectionAttempt.Rehydrate(
                Enum.Parse<ExamModule>(a.Module),
                Offset(a.StartedAt),
                Offset(a.DeadlineAt),
                a.SubmittedAt is { } s ? Offset(s) : null)));

    // ── Results ──────────────────────────────────────────────────────────

    public static SectionScore ToDomain(this SectionResultDocument doc) =>
        new(
            Enum.Parse<ExamModule>(doc.Module),
            doc.RawScore,
            doc.MaxScore,
            BandScore.Create(doc.Band),
            [.. doc.Questions.Select(q => new QuestionResult(q.QuestionId, q.Submitted, q.IsCorrect))]);
}
