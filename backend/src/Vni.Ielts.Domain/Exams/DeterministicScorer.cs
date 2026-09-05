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
  /// <param name="context">
  /// Optional scope and band policy. Practice parts pass a question filter and
  /// <c>IncludeBand = false</c> because an uncalibrated slice has no IELTS band.
  /// </param>
  public static SectionScore Score(
      Section section,
      ScoringProfile profile,
      IReadOnlyDictionary<string, string?> answers,
      DeterministicScoringContext? context = null)
  {
    if (section.Module is not (ExamModule.Reading or ExamModule.Listening))
      throw new ArgumentException(
          $"{section.Module} is not deterministically scored. Its band comes from a validated "
          + "evaluation, not from an answer key.",
          nameof(section));

    context ??= DeterministicScoringContext.FullSection;

    var results = new List<QuestionResult>();
    var correct = 0;
    var available = 0;

    foreach (var question in section.Questions.Where(q => q.Type.IsAutoScored()))
    {
      if (context.QuestionIds is { } filter && !filter.Contains(question.Id))
        continue;

      var outcome = ScoreQuestion(question, answers, profile);
      available += outcome.Marks;
      correct += outcome.MarksAwarded;
      results.Add(outcome.Result);
    }

    BandScore? band = context.IncludeBand
        ? profile.BandFor(section.Module, correct)
        : null;

    return new SectionScore(section.Module, correct, available, band, results);
  }

  private static QuestionScoringOutcome ScoreQuestion(
      Question question,
      IReadOnlyDictionary<string, string?> answers,
      ScoringProfile profile)
  {
    if (question.Slots is { Count: > 0 } slots)
      return ScoreSlottedQuestion(question, slots, answers, profile.Matching);

  return ScoreSlotlessQuestion(question, answers, profile);
  }

  private static QuestionScoringOutcome ScoreSlottedQuestion(
      Question question,
      IReadOnlyList<ResponseSlot> slots,
      IReadOnlyDictionary<string, string?> answers,
      AnswerMatchingRules matching)
  {
    var slotResults = new List<SlotResult>(slots.Count);
    var marksAwarded = 0;
    var submittedParts = new List<string?>(slots.Count);

    foreach (var slot in slots.OrderBy(s => s.Number))
    {
      var submitted = ResolveSubmitted(question, slot, answers);
      submittedParts.Add(submitted);

      var isCorrect = AnswerMatcher.IsCorrectSlot(slot, question, submitted, matching);
      if (isCorrect) marksAwarded++;

      slotResults.Add(new SlotResult(
          slot.Id,
          slot.Number,
          submitted,
          AnswerMatcher.Outcome(submitted, isCorrect),
          AnswerMatcher.FormatAcceptedAnswer(slot.AnswerKey, matching)));
    }

    var marks = slots.Count;
    var joined = slots.Count == 1
        ? submittedParts[0]
        : string.Join('|', submittedParts.Select(v => v ?? string.Empty));
    var revealed = slots.Count == 1
        ? slotResults[0].CorrectAnswer
        : string.Join(" | ", slotResults.Select(s => s.CorrectAnswer).Where(v => v is not null));

    return new QuestionScoringOutcome(
        marks,
        marksAwarded,
        new QuestionResult(
            question.Id,
            joined,
            marksAwarded == marks,
            slotResults,
            revealed));
  }

  private static QuestionScoringOutcome ScoreSlotlessQuestion(
      Question question,
      IReadOnlyDictionary<string, string?> answers,
      ScoringProfile profile)
  {
    answers.TryGetValue(question.Id, out var submitted);
    var isCorrect = AnswerMatcher.IsCorrect(question, submitted, profile.Matching);
    var marks = question.Marks;
    var marksAwarded = isCorrect ? marks : 0;

    if (!isCorrect
        && marks > 1
        && profile.PartialCredit is { } policy
        && policy.MultiMark != MultiMarkPartialCredit.AllOrNothing)
    {
        throw new InvalidOperationException(
            $"This exam version declares partial-credit mode '{policy.MultiMark}', "
            + "which is not implemented. Refusing to invent a part-mark rule.");
    }

    return new QuestionScoringOutcome(
        marks,
        marksAwarded,
        new QuestionResult(
            question.Id,
            submitted,
            isCorrect,
            CorrectAnswer: AnswerMatcher.FormatAcceptedAnswer(question.AnswerKey, profile.Matching)));
  }

  private static string? ResolveSubmitted(
      Question question,
      ResponseSlot slot,
      IReadOnlyDictionary<string, string?> answers)
  {
    if (answers.TryGetValue(slot.Id, out var direct))
      return direct;

    if (!answers.TryGetValue(question.Id, out var legacy) || legacy is null)
      return null;

    if (question.Slots is not { Count: > 1 } slots)
      return legacy;

    var index = slots.OrderBy(s => s.Number).ToList().FindIndex(s => s.Id == slot.Id);
    if (index < 0) return null;

    var parts = legacy.Split('|');
    return index < parts.Length ? parts[index] : null;
  }

  private sealed record QuestionScoringOutcome(
      int Marks,
      int MarksAwarded,
      QuestionResult Result);
}

/// <summary>
/// Scope and band policy for a deterministic score.
/// </summary>
public sealed record DeterministicScoringContext(
    IReadOnlySet<string>? QuestionIds = null,
    bool IncludeBand = true)
{
  public static readonly DeterministicScoringContext FullSection = new();
}

public sealed record SectionScore(
    ExamModule Module,
    int RawScore,
    int MaxScore,
    BandScore? Band,
    IReadOnlyList<QuestionResult> Questions);

/// <summary>
/// Per-question outcome.
///
/// Note there is no "explanation" field. An AI explanation is attached
/// separately and can never travel on the same object as the mark — the
/// separation is what makes `A-11` enforceable by shape rather than by
/// discipline.
/// </summary>
public sealed record QuestionResult(
    string QuestionId,
    string? Submitted,
    bool IsCorrect,
    IReadOnlyList<SlotResult>? Slots = null,
    string? CorrectAnswer = null);

/// <summary>Per answer-sheet position outcome.</summary>
public sealed record SlotResult(
    string SlotId,
    int Number,
    string? Submitted,
    SlotOutcome Status,
    string? CorrectAnswer);

public enum SlotOutcome { Correct, Incorrect, Unanswered }
