using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Application.Tests.Exams;

public sealed class SaveAnswersSlotTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 9, 0, 0, TimeSpan.Zero);
    private static readonly UserId Learner = UserId.New();

    private static ExamVersion SlottedVersion()
    {
        var scoring = new ScoringProfile(
            new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>
            {
                [ExamModule.Reading] =
                [
                    new BandBoundary(0, BandScore.Create(4.0m)),
                    new BandBoundary(2, BandScore.Create(7.0m)),
                ],
            },
            AnswerMatchingRules.Default);

        var timing = new TimingProfile(
            new Dictionary<ExamModule, int> { [ExamModule.Reading] = 3600 },
            null,
            []);

        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Slot autosave", ExamVariant.Academic, scoring, timing,
            [
                new Section(ExamModule.Reading, 1,
                [
                    new SectionPart(1, "passage", "P", "body", null, null, null, null, null, null, null,
                    [
                        new Question("q-multi", 1, QuestionType.MultipleSelect, "Choose TWO", [], null,
                            new AnswerKey([new AcceptedAnswer(null, ["A", "B"], null)], null),
                            null,
                            2,
                            [
                                new ResponseSlot("slot-1", 1,
                                    new AnswerKey([new AcceptedAnswer("A", null, null)], null)),
                                new ResponseSlot("slot-2", 2,
                                    new AnswerKey([new AcceptedAnswer("B", null, null)], null)),
                            ]),
                    ]),
                ]),
            ],
            new ListeningPlaybackProfile(
                new AudioPlaybackRule(false, true),
                new AudioPlaybackRule(true, false)));

        version.Publish(T0.AddDays(-1));
        return version;
    }

    [Fact]
    public async Task SaveAnswers_accepts_direct_slot_ids_and_returns_slot_sequences_on_conflict()
    {
        var version = SlottedVersion();
        var catalogue = new FakeExamCatalogue(version);
        var sessions = new FakeSessionRepository();
        var answers = new Assessment.FakeAnswerSheetStore();
        var save = new SaveAnswers(catalogue, sessions, answers, new MovableClock(T0));

        var started = await new StartExamSession(catalogue, sessions, new MovableClock(T0))
            .HandleAsync(
                new StartExamSessionCommand(Learner, version.Id, SessionMode.Single, ExamModule.Reading),
                default);
        var sessionId = new ExamSessionId(started.SessionId);

        await save.HandleAsync(
            new SaveAnswersCommand(
                Learner, sessionId, ExamModule.Reading,
                new Dictionary<string, string?> { ["slot-1"] = "A" },
                BaseRevision: 0,
                Sequences: new Dictionary<string, long> { ["slot-1"] = 1 }),
            default);

        var merged = await save.HandleAsync(
            new SaveAnswersCommand(
                Learner, sessionId, ExamModule.Reading,
                new Dictionary<string, string?>
                {
                    ["slot-1"] = "stale",
                    ["slot-2"] = "B",
                },
                BaseRevision: 0,
                Sequences: new Dictionary<string, long>
                {
                    ["slot-1"] = 1,
                    ["slot-2"] = 2,
                }),
            default);

        Assert.NotNull(merged.Answers);
        Assert.Equal("A|B", merged.Answers["q-multi"]);
        Assert.NotNull(merged.Sequences);
        Assert.Equal(1, merged.Sequences["slot-1"]);
        Assert.Equal(2, merged.Sequences["slot-2"]);
    }

    [Fact]
    public async Task SaveAnswers_refuses_unknown_slot_ids()
    {
        var version = SlottedVersion();
        var catalogue = new FakeExamCatalogue(version);
        var sessions = new FakeSessionRepository();
        var answers = new Assessment.FakeAnswerSheetStore();
        var save = new SaveAnswers(catalogue, sessions, answers, new MovableClock(T0));

        var started = await new StartExamSession(catalogue, sessions, new MovableClock(T0))
            .HandleAsync(
                new StartExamSessionCommand(Learner, version.Id, SessionMode.Single, ExamModule.Reading),
                default);

        await Assert.ThrowsAsync<UnknownQuestionException>(() =>
            save.HandleAsync(
                new SaveAnswersCommand(
                    Learner, new ExamSessionId(started.SessionId), ExamModule.Reading,
                    new Dictionary<string, string?> { ["slot-unknown"] = "x" }),
                default));
    }
}
