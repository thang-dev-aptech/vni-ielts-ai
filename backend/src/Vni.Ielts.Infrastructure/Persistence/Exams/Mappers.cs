using Vni.Ielts.Domain.Assessment;
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

    /// <summary>
    /// Everything a sitting is scored against, as one string.
    ///
    /// <b>Composed from the domain object rather than from the document.</b>
    /// Serialising the document would make the fingerprint depend on the BSON
    /// mapping, so adding a field or renaming one would change every published
    /// version's hash and lock the catalogue. This depends on the content and
    /// nothing else.
    ///
    /// <b>Status and `publishedAt` are excluded, deliberately.</b> Publishing
    /// and unpublishing change what a version is <i>for</i>, not what it
    /// <i>says</i>, and both must keep working on a published version.
    /// </summary>
    internal static string ContentFingerprint(this ExamVersion version)
    {
        var text = new System.Text.StringBuilder();

        text.Append(version.DefinitionId.Value).Append('\u0000')
            .Append(version.VersionNumber).Append('\u0000')
            .Append(version.Title).Append('\u0000')
            .Append(version.Variant).Append('\u0000')
            .Append(version.Timing.ListeningTransferSeconds).Append('\u0000');

        foreach (var (module, seconds) in version.Timing.SectionDurationSeconds.OrderBy(kv => kv.Key))
            text.Append(module).Append('=').Append(seconds).Append('\u0000');

        foreach (var part in version.Timing.SpeakingParts.OrderBy(p => p.Part))
            text.Append(part.Part).Append(':').Append(part.PrepSeconds).Append(':')
                .Append(part.ResponseSeconds).Append('\u0000');

        foreach (var (module, table) in version.Scoring.RawToBand.OrderBy(kv => kv.Key))
        {
            text.Append(module).Append('#');
            foreach (var row in table.OrderByDescending(r => r.MinRaw))
                text.Append(row.MinRaw).Append('>').Append(row.Band.Value).Append(',');
            text.Append('\u0000');
        }

        foreach (var section in version.Sections.OrderBy(s => s.Order))
        {
            text.Append(section.Module).Append('|').Append(section.Order).Append('|');

            foreach (var part in section.Parts.OrderBy(p => p.Order))
            {
                text.Append(part.Order).Append('|').Append(part.Kind).Append('|')
                    .Append(part.Body).Append('|').Append(part.AudioKey).Append('|')
                    .Append(part.ImageKey).Append('|').Append(part.TaskNumber).Append('|');

                foreach (var question in part.Questions.OrderBy(q => q.Order))
                {
                    text.Append(question.Id).Append('~').Append(question.Type).Append('~')
                        .Append(question.Prompt).Append('~').Append(question.Marks).Append('~');

                    foreach (var option in question.Options)
                        text.Append(option.Key).Append('=').Append(option.Text).Append(';');

                    // The answer key is the half a silent edit is most damaging
                    // to: the passage looks the same and the marking changes.
                    foreach (var accepted in question.AnswerKey?.Accepted ?? [])
                        text.Append(accepted).Append('!');

                    text.Append('\u0000');
                }
            }
        }

        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(text.ToString())));
    }

    public static ExamVersionDocument ToDocument(this ExamVersion version) => new()
    {
        ContentHash = version.ContentFingerprint(),
        Id = version.Id.Value,
        DefinitionId = version.DefinitionId.Value,
        VersionNumber = version.VersionNumber,
        Title = version.Title,
        Variant = version.Variant.ToString(),
        Status = version.Status.ToString(),
        PublishedAt = version.PublishedAt is { } at ? Utc(at) : null,
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
                Marks = q.Marks,
                Group = q.Group is { } g
                    ? new QuestionGroupDocument
                    {
                        Id = g.Id,
                        Title = g.Title,
                        Instruction = g.Instruction,
                        Image = g.Image,
                        Text = g.Text,
                        EachLetterOnce = g.EachLetterOnce,
                    }
                    : null,
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
            doc.PublishedAt is { } at ? Offset(at) : null,
            scoring,
            timing,
            [.. doc.Sections.Select(ToDomain)]);
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
                        : null,
                    q.Group is { } g
                        ? new QuestionGroup(g.Id, g.Title, g.Instruction, g.Image, g.Text, g.EachLetterOnce)
                        : null,
                    q.Marks)),
            ]);

    // ── Session ──────────────────────────────────────────────────────────

    public static ExamSessionDocument ToDocument(this ExamSession session) => new()
    {
        Id = session.Id.Value,
        UserId = session.UserId.Value,
        ExamVersionId = session.ExamVersionId.Value,
        Mode = session.Mode.ToString(),
        Timing = session.Timing.ToString(),
        Status = session.Status.ToString(),
        StartedAt = Utc(session.StartedAt),
        SubmittedAt = session.SubmittedAt is { } at ? Utc(at) : null,
        Attempts =
        [
            .. session.Attempts.Select(a => new AttemptDocument
            {
                Module = a.Module.ToString(),
                StartedAt = Utc(a.StartedAt),
                DeadlineAt = a.DeadlineAt is { } d ? Utc(d) : null,
                SubmittedAt = a.SubmittedAt is { } s ? Utc(s) : null,
                AccumulatedSeconds = a.AccumulatedSeconds,
                RunningSince = a.RunningSince is { } r ? Utc(r) : null,
                TargetSeconds = a.TargetSeconds,
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
                a.DeadlineAt is { } d ? Offset(d) : null,
                a.SubmittedAt is { } s ? Offset(s) : null,
                a.AccumulatedSeconds,
                a.RunningSince is { } r ? Offset(r) : null,
                a.TargetSeconds)),
            // Absent means Deadline: every sitting written before this field
            // existed was a timed one, because there was no other kind.
            Enum.TryParse<SessionTiming>(doc.Timing, out var timing)
                ? timing
                : SessionTiming.Deadline);

    // ── Results ──────────────────────────────────────────────────────────

    /// <summary>
    /// <b>The band is read back, not recomputed.</b> It was recomputed from the
    /// criterion bands when the marking was made and stored as the result of
    /// that; recomputing on read would silently re-derive a learner's band
    /// under whatever the aggregation rule says today. A stored band is a
    /// historical fact about an evaluation, not a cache of a calculation.
    /// </summary>
    public static SectionMarking ToDomain(this SectionMarkingDocument doc) =>
        new(
            Enum.Parse<ExamModule>(doc.Module),
            doc.RubricVersion,
            [
                .. doc.Criteria.Select(c => CriterionAssessment.Create(
                    c.Criterion, BandScore.Create(c.Band), c.Feedback, [.. c.Evidence])),
            ],
            BandScore.Create(doc.Band),
            doc.ReportedBand is { } reported ? BandScore.Create(reported) : null,
            [.. doc.Flags.Select(Enum.Parse<MarkingFlag>)],
            [.. doc.UngroundedEvidence],
            doc.TaskNumber);

    public static SectionScore ToDomain(this SectionResultDocument doc) =>
        new(
            Enum.Parse<ExamModule>(doc.Module),
            doc.RawScore,
            doc.MaxScore,
            BandScore.Create(doc.Band),
            [.. doc.Questions.Select(q => new QuestionResult(q.QuestionId, q.Submitted, q.IsCorrect))]);
}
