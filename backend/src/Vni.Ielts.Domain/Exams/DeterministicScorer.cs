namespace Vni.Ielts.Domain.Exams;

/// <summary>
/// Reading and Listening scoring. Pure arithmetic over the answer key.
///
/// <b>No AI provider is involved, and none can be.</b> Requirement `A-11`
/// confirms the band comes from the answer key; an AI explanation is a
/// separate artifact generated <i>about</i> a completed result, with no path
/// to a band. This class has no dependency through which a model could reach
/// a score, which is the property that lets Reading and Listening work fully
/// before any provider is chosen.
/// </summary>
public static class DeterministicScorer
{
    public static SectionScore Score(
        Section section, ScoringProfile profile, IReadOnlyDictionary<string, string?> answers)
    {
        if (section.Module is not (ExamModule.Reading or ExamModule.Listening))
            throw new ArgumentException(
                $"{section.Module} is not deterministically scored. Its band comes from a validated "
                + "evaluation, not from an answer key.",
                nameof(section));

        var results = new List<QuestionResult>();
        var correct = 0;
        var available = 0;

        foreach (var question in section.Questions.Where(q => q.Type.IsAutoScored()))
        {
            answers.TryGetValue(question.Id, out var submitted);
            var isCorrect = AnswerMatcher.IsCorrect(question, submitted, profile.Matching);

            // Marks, not questions. A "Choose TWO letters" question is one
            // object and two marks, and the band table is equated against
            // marks — counting objects scored a 40-mark Listening section out
            // of 36 and put the top of its own table out of reach.
            available += question.Marks;
            if (isCorrect) correct += question.Marks;

            results.Add(new QuestionResult(question.Id, submitted, isCorrect));
        }

        var band = profile.BandFor(section.Module, correct);

        return new SectionScore(section.Module, correct, available, band, results);
    }
}

public sealed record SectionScore(
    ExamModule Module,
    int RawScore,
    int MaxScore,
    BandScore Band,
    IReadOnlyList<QuestionResult> Questions);

/// <summary>
/// Per-question outcome.
///
/// Note there is no "explanation" field. An AI explanation is attached
/// separately and can never travel on the same object as the mark — the
/// separation is what makes `A-11` enforceable by shape rather than by
/// discipline.
/// </summary>
public sealed record QuestionResult(string QuestionId, string? Submitted, bool IsCorrect);
