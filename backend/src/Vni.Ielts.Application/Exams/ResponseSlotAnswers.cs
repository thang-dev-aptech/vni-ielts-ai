using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Exams;

/// <summary>
/// The wire still presents one value per question because that is what the
/// renderer edits, but persistence is addressed by immutable response-slot id.
/// This adapter is the single compatibility seam while old sessions migrate.
/// A multi-slot value uses the learner's existing pipe encoding and is split
/// deterministically onto the ordered slots.
/// </summary>
internal static class ResponseSlotAnswers
{
    internal sealed record SlotIndexEntry(Question Question, ResponseSlot Slot);

    public static IReadOnlyDictionary<string, SlotIndexEntry> BuildSlotIndex(
        IEnumerable<Question> questions)
    {
        var index = new Dictionary<string, SlotIndexEntry>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            if (question.Slots is not { Count: > 0 } slots) continue;
            foreach (var slot in slots) index[slot.Id] = new SlotIndexEntry(question, slot);
        }

        return index;
    }

    public static IReadOnlyList<string> ResolveChangeKeys(
        IEnumerable<string> keys,
        IReadOnlySet<string> knownQuestionIds,
        IReadOnlyDictionary<string, SlotIndexEntry> slotIndex)
    {
        var unknown = new List<string>();
        foreach (var key in keys)
        {
            if (knownQuestionIds.Contains(key) || slotIndex.ContainsKey(key)) continue;
            unknown.Add(key);
        }

        return unknown;
    }

    public static string MapSlotToQuestion(
        string key, IReadOnlyDictionary<string, SlotIndexEntry> slotIndex) =>
        slotIndex.TryGetValue(key, out var entry) ? entry.Question.Id : key;

    public static IReadOnlyDictionary<string, string?> ToSlotChanges(
        IReadOnlyDictionary<string, string?> changes, Section section)
    {
        var byQuestion = section.Questions.ToDictionary(q => q.Id, StringComparer.Ordinal);
        var slotIndex = BuildSlotIndex(section.Questions);
        var expanded = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (key, value) in changes)
        {
            if (slotIndex.ContainsKey(key))
            {
                expanded[key] = value;
                continue;
            }

            if (!byQuestion.TryGetValue(key, out var question)
                || question.Slots is not { Count: > 0 } slots)
            {
                expanded[key] = value;
                continue;
            }

            var values = value?.Split('|') ?? [];
            for (var index = 0; index < slots.Count; index++)
                expanded[slots[index].Id] = index < values.Length ? values[index] : null;
        }

        return expanded;
    }

    public static IReadOnlyDictionary<string, long> ToSlotSequences(
        IReadOnlyDictionary<string, long>? sequences, Section section)
    {
        if (sequences is null || sequences.Count == 0) return new Dictionary<string, long>();
        var byQuestion = section.Questions.ToDictionary(q => q.Id, StringComparer.Ordinal);
        var slotIndex = BuildSlotIndex(section.Questions);
        var expanded = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var (key, sequence) in sequences)
        {
            if (slotIndex.ContainsKey(key))
            {
                expanded[key] = sequence;
                continue;
            }

            if (!byQuestion.TryGetValue(key, out var question)
                || question.Slots is not { Count: > 0 } slots)
            {
                expanded[key] = sequence;
                continue;
            }

            foreach (var slot in slots) expanded[slot.Id] = sequence;
        }

        return expanded;
    }

    public static IReadOnlyDictionary<string, string?> ToQuestionAnswers(
        IReadOnlyDictionary<string, string?> answers, Section section)
    {
        var projected = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var question in section.Questions)
        {
            if (question.Slots is not { Count: > 0 } slots)
            {
                if (answers.TryGetValue(question.Id, out var value)) projected[question.Id] = value;
                continue;
            }

            var values = slots
                .Select(slot => answers.TryGetValue(slot.Id, out var value) ? value : null)
                .ToArray();
            if (values.Any(value => value is not null))
                projected[question.Id] = slots.Count == 1
                    ? values[0]
                    : string.Join('|', values.Select(value => value ?? string.Empty));
            else if (answers.TryGetValue(question.Id, out var legacy))
                projected[question.Id] = legacy;
        }

        return projected;
    }

    public static IReadOnlyDictionary<string, long> ToQuestionSequences(
        IReadOnlyDictionary<string, long>? sequences, Section section)
    {
        if (sequences is null || sequences.Count == 0) return new Dictionary<string, long>();
        var projected = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var question in section.Questions)
        {
            if (sequences.TryGetValue(question.Id, out var legacy)) projected[question.Id] = legacy;
            if (question.Slots is { Count: > 0 } slots)
            {
                var values = slots
                    .Where(slot => sequences.ContainsKey(slot.Id))
                    .Select(slot => sequences[slot.Id]);
                if (values.Any()) projected[question.Id] = values.Max();
            }
        }

        return projected;
    }

    /// <summary>
    /// Ordering tokens for the wire: slot-keyed for slotted questions, question-keyed
    /// for slotless ones such as Writing essays.
    /// </summary>
    public static IReadOnlyDictionary<string, long> ToWireSequences(
        IReadOnlyDictionary<string, long>? sequences, IEnumerable<Question> visibleQuestions)
    {
        if (sequences is null || sequences.Count == 0) return new Dictionary<string, long>();
        var projected = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var question in visibleQuestions)
        {
            if (question.Slots is { Count: > 0 } slots)
            {
                foreach (var slot in slots)
                {
                    if (sequences.TryGetValue(slot.Id, out var seq)) projected[slot.Id] = seq;
                }
            }
            else if (sequences.TryGetValue(question.Id, out var seq))
            {
                projected[question.Id] = seq;
            }
        }

        return projected;
    }
}
