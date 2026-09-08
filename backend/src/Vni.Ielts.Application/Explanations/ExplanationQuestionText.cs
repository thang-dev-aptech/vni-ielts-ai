using System.Text;
using System.Text.RegularExpressions;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Explanations;

/// <summary>
/// The question as a reader would see it, for the explanation prompt.
///
/// <b>A gap-fill question's own <c>Prompt</c> is often empty.</b> On a
/// Cambridge note-completion set the sentence with the gap lives on the
/// group (<c>QuestionGroup.Text</c>, with <c>[n]</c> where question n's gap
/// falls) and the question carries only its number and answer key. Sending
/// the bare prompt produced explanations that began "no specific question
/// was provided" — the model was right, it had not been given one. This
/// composes what the candidate actually read: the group's caption and
/// rubric, then the line of the note holding this question's gap, then the
/// question's own prompt when it has one.
/// </summary>
public static class ExplanationQuestionText
{
    public static string Compose(Question question)
    {
        var text = new StringBuilder();

        if (question.Group is { } group)
        {
            if (!string.IsNullOrWhiteSpace(group.Title))
                text.Append("Group: ").AppendLine(group.Title.Trim());

            if (!string.IsNullOrWhiteSpace(group.Instruction))
                text.Append("Instruction: ").AppendLine(group.Instruction.Trim());

            var gapLine = GapLine(group.Text, question.Order);
            if (gapLine is not null)
                text.Append("Sentence with the gap [").Append(question.Order).Append("]: ")
                    .AppendLine(gapLine);
        }

        if (!string.IsNullOrWhiteSpace(question.Prompt) && !IsPlaceholder(question.Prompt))
            text.AppendLine(question.Prompt.Trim());

        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// "Question 7" is what the importer writes when a gap-fill item has no
    /// sentence of its own. It names the item; it does not ask anything, and
    /// sending it as the question had the model explaining that no question
    /// was supplied.
    /// </summary>
    internal static bool IsPlaceholder(string prompt) =>
        Regex.IsMatch(prompt.Trim(), @"^(Question|Câu)\s+\d+$", RegexOptions.IgnoreCase);

    /// <summary>
    /// The line of the group text that carries <c>[order]</c>; the whole text
    /// when it is one block; null when there is no text at all.
    /// </summary>
    internal static string? GapLine(string? groupText, int order)
    {
        if (string.IsNullOrWhiteSpace(groupText)) return null;

        var marker = $"[{order}]";
        var lines = groupText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.FirstOrDefault(l => l.Contains(marker, StringComparison.Ordinal));

        return line ?? (Regex.IsMatch(groupText, @"\[\d+\]") ? groupText.Trim() : null);
    }
}
