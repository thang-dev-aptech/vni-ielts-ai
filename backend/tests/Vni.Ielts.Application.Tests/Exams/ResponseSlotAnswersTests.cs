using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Exams;

public sealed class ResponseSlotAnswersTests
{
    private static Section SlottedSection() =>
        new(ExamModule.Reading, 1,
        [
            new SectionPart(1, "passage", "P", "body", null, null, null, null, null, null, null,
            [
                MultiSlotQuestion(),
                SlotlessQuestion(),
            ]),
        ]);

    private static Question MultiSlotQuestion() =>
        new("q-multi", 1, QuestionType.MultipleSelect, "Choose TWO", [], null,
            new AnswerKey([new AcceptedAnswer(null, ["A", "B"], null)], null),
            null,
            2,
            [
                new ResponseSlot("slot-1", 1, new AnswerKey([new AcceptedAnswer("A", null, null)], null)),
                new ResponseSlot("slot-2", 2, new AnswerKey([new AcceptedAnswer("B", null, null)], null)),
            ]);

    private static Question SlotlessQuestion() =>
        new("q-essay", 2, QuestionType.EssayTask, "Write", [], null, null);

    [Fact]
    public void ToSlotChanges_passes_through_known_slot_ids_without_splitting()
    {
        var section = SlottedSection();

        var expanded = ResponseSlotAnswers.ToSlotChanges(
            new Dictionary<string, string?>
            {
                ["slot-1"] = "A",
                ["slot-2"] = "B",
            },
            section);

        Assert.Equal("A", expanded["slot-1"]);
        Assert.Equal("B", expanded["slot-2"]);
        Assert.Equal(2, expanded.Count);
    }

    [Fact]
    public void ToSlotChanges_still_expands_question_ids_for_legacy_clients()
    {
        var section = SlottedSection();

        var expanded = ResponseSlotAnswers.ToSlotChanges(
            new Dictionary<string, string?> { ["q-multi"] = "A|B" },
            section);

        Assert.Equal("A", expanded["slot-1"]);
        Assert.Equal("B", expanded["slot-2"]);
    }

    [Fact]
    public void ToSlotSequences_keeps_per_slot_tokens_independent()
    {
        var section = SlottedSection();

        var expanded = ResponseSlotAnswers.ToSlotSequences(
            new Dictionary<string, long>
            {
                ["slot-1"] = 3,
                ["slot-2"] = 7,
            },
            section);

        Assert.Equal(3, expanded["slot-1"]);
        Assert.Equal(7, expanded["slot-2"]);
    }

    [Fact]
    public void ToSlotSequences_replicates_question_level_tokens_across_slots()
    {
        var section = SlottedSection();

        var expanded = ResponseSlotAnswers.ToSlotSequences(
            new Dictionary<string, long> { ["q-multi"] = 5 },
            section);

        Assert.Equal(5, expanded["slot-1"]);
        Assert.Equal(5, expanded["slot-2"]);
    }

    [Fact]
    public void ToWireSequences_exposes_slot_keys_for_slotted_questions_and_question_keys_for_slotless()
    {
        var section = SlottedSection();
        var visible = section.Questions.ToArray();

        var wire = ResponseSlotAnswers.ToWireSequences(
            new Dictionary<string, long>
            {
                ["slot-1"] = 2,
                ["slot-2"] = 4,
                ["q-essay"] = 9,
                ["q-multi"] = 99,
            },
            visible);

        Assert.Equal(2, wire["slot-1"]);
        Assert.Equal(4, wire["slot-2"]);
        Assert.Equal(9, wire["q-essay"]);
        Assert.False(wire.ContainsKey("q-multi"));
    }

    [Fact]
    public void ResolveChangeKeys_accepts_question_or_slot_ids_and_refuses_unknown()
    {
        var section = SlottedSection();
        var visible = section.Questions.ToArray();
        var knownQuestions = visible.Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        var slotIndex = ResponseSlotAnswers.BuildSlotIndex(visible);

        var unknown = ResponseSlotAnswers.ResolveChangeKeys(
            ["slot-1", "q-essay", "no-such-slot"],
            knownQuestions,
            slotIndex);

        Assert.Equal(["no-such-slot"], unknown);
    }

    [Fact]
    public void MapSlotToQuestion_maps_slot_ids_for_error_reporting()
    {
        var section = SlottedSection();
        var slotIndex = ResponseSlotAnswers.BuildSlotIndex(section.Questions);

        Assert.Equal("q-multi", ResponseSlotAnswers.MapSlotToQuestion("slot-1", slotIndex));
        Assert.Equal("q-essay", ResponseSlotAnswers.MapSlotToQuestion("q-essay", slotIndex));
    }
}
