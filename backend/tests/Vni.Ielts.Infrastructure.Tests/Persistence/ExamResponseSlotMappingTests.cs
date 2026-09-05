using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Content;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Persistence;

public sealed class ExamResponseSlotMappingTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static ExamVersion V2()
    {
        var reader = ExamPackageReader.FromSchemaFile(
            Path.Combine(RepoRoot(), "contracts", "schemas", "exam.schema.json"));
        var result = reader.Read("""
        {
          "formatVersion": "2.0", "formatProfile": "vni-practice", "scoringProfileRef": "mapping-v1",
          "contentSourceRef": { "sourceId": "synthetic-mapping", "sourceHash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
          "title": "Mapping", "variant": "academic",
          "timingProfile": { "sections": { "reading": { "durationSeconds": 60 } } },
          "policyProfile": { "listeningPlayback": {
            "practice": { "playOnce": false, "allowSeek": true },
            "mock": { "playOnce": true, "allowSeek": false }
          } },
          "scoringProfile": { "rawToBand": { "reading": [
            { "minRaw": 0, "band": 0 }, { "minRaw": 1, "band": 1 }, { "minRaw": 2, "band": 2 } ] } },
          "sections": [{ "module": "reading", "order": 1, "parts": [{
            "order": 1, "kind": "passage", "body": "Evidence here.", "timing": { "durationSeconds": 45 },
            "questions": [{ "id": "q-1", "order": 1, "type": "multiple-select", "marks": 2,
              "options": [{ "key": "A", "text": "A" }, { "key": "B", "text": "B" }],
              "group": { "id": "bank-1", "instruction": "Choose two." },
              "slots": [
                { "id": "slot-1", "number": 1, "answerKey": { "accepted": ["A"] } },
                { "id": "slot-2", "number": 2, "answerKey": { "accepted": ["B"] } }
              ],
              "explanation": { "shortReason": "Both are stated.", "evidence": ["Evidence here."] }
            }]
          }]}]
        }
        """, ExamDefinitionId.New(), 1);

        Assert.True(result.IsValid, string.Join("; ", result.Findings.Select(f => f.Message)));
        return result.Version!;
    }

    [Fact]
    public void Full_question_hierarchy_round_trips_without_losing_slots()
    {
        var original = V2();
        var roundTrip = original.ToDocument().ToDomain();
        var part = roundTrip.Sections.Single().Parts.Single();
        var question = part.Questions.Single();

        Assert.Equal(original.ContentFingerprint(), roundTrip.ContentFingerprint());
        Assert.Equal(45, part.Timing!.DurationSeconds);
        Assert.Equal("bank-1", question.Group!.Id);
        var slots = Assert.IsAssignableFrom<IReadOnlyList<ResponseSlot>>(question.Slots);
        Assert.Equal(new[] { "slot-1", "slot-2" }, slots.Select(s => s.Id));
        Assert.Equal(new[] { 1, 2 }, slots.Select(s => s.Number));
        Assert.Equal("A", slots[0].AnswerKey!.Accepted[0].Single);
        Assert.Equal("Evidence here.", question.Explanation!.Evidence[0]);
        Assert.Equal(new AudioPlaybackRule(false, true), roundTrip.ListeningPlayback.Practice);
        Assert.Equal(new AudioPlaybackRule(true, false), roundTrip.ListeningPlayback.Mock);
    }

    [Fact]
    public void Slot_answer_is_part_of_the_published_content_fingerprint()
    {
        var original = V2();
        var question = original.Sections[0].Parts[0].Questions[0];
        var changed = question with
        {
            Slots =
            [
                question.Slots![0] with
                {
                    AnswerKey = new AnswerKey([new AcceptedAnswer("B", null, null)], null),
                },
                question.Slots[1],
            ],
        };
        var changedVersion = ExamVersion.Rehydrate(
            original.Id, original.DefinitionId, original.VersionNumber, original.Title, original.Variant,
            original.Status, original.PublishedAt, original.Scoring, original.Timing,
            [original.Sections[0] with
            {
                Parts = [original.Sections[0].Parts[0] with { Questions = [changed] }],
            }]);

        Assert.NotEqual(original.ContentFingerprint(), changedVersion.ContentFingerprint());
    }

    [Fact]
    public void Legacy_document_without_slots_rehydrates_stable_per_mark_slots()
    {
        var document = V2().ToDocument();
        var question = document.Sections[0].Parts[0].Questions[0];
        question.Slots.Clear();
        question.AnswerKey = new AnswerKeyDocument
        {
            Accepted =
            [
                new AcceptedAnswerDocument { All = ["A", "B"] },
            ],
        };

        var first = document.ToDomain().Sections[0].Questions.Single().Slots!;
        var second = document.ToDomain().Sections[0].Questions.Single().Slots!;

        Assert.Equal(new[] { "q-1-slot-1", "q-1-slot-2" }, first.Select(s => s.Id));
        Assert.Equal(new[] { 1, 2 }, first.Select(s => s.Number));
        Assert.Equal("A", first[0].AnswerKey!.Accepted[0].Single);
        Assert.Equal("B", first[1].AnswerKey!.Accepted[0].Single);
        Assert.Equal(first.Select(s => s.Id), second.Select(s => s.Id));
    }

    [Fact]
    public void Legacy_document_without_playback_policy_fails_closed()
    {
        var document = V2().ToDocument();
        document.ListeningPlayback = null;

        var roundTrip = document.ToDomain();

        Assert.Equal(ListeningPlaybackProfile.Conservative, roundTrip.ListeningPlayback);
    }
}
