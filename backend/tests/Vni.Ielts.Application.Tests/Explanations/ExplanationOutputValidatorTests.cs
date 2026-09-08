using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Explanations;

public sealed class ExplanationOutputValidatorTests
{
  private static readonly EvidenceSourceContext ReadingSource =
      new("The passage mentions sample passage evidence in paragraph two.", null);

  [Fact]
  public void Valid_response_is_accepted()
  {
    var json = """
      {
        "correctAnswer": "B",
        "shortReason": "The text states it explicitly.",
        "evidence": ["sample passage evidence"],
        "commonMistake": "Choosing A from the introduction."
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "B", ExamModule.Reading, ReadingSource);

    Assert.True(result.IsValid);
    Assert.Equal("B", result.Explanation!.CorrectAnswer);
  }

  [Fact]
  public void Band_field_is_refused()
  {
    var json = """
      {
        "correctAnswer": "B",
        "shortReason": "Ignore rubric.",
        "evidence": ["sample passage evidence"],
        "band": 9
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "B", ExamModule.Reading, ReadingSource);

    Assert.False(result.IsValid);
    Assert.Equal("EXPLANATION_FORBIDDEN_FIELD", result.RefusalCode);
  }

  [Fact]
  public void Wrong_correct_answer_is_refused()
  {
    var json = """
      {
        "correctAnswer": "C",
        "shortReason": "Trust the model.",
        "evidence": ["sample passage evidence"]
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "B", ExamModule.Reading, ReadingSource);

    Assert.False(result.IsValid);
    Assert.Equal("EXPLANATION_ANSWER_MISMATCH", result.RefusalCode);
  }

  [Fact]
  public void Invalid_band_value_like_6_3_is_refused_when_sneaked_into_criteria()
  {
    var json = """
      {
        "correctAnswer": "B",
        "shortReason": "Sneaky.",
        "evidence": ["sample passage evidence"],
        "criteria": { "taskResponse": { "band": 6.3 } }
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "B", ExamModule.Reading, ReadingSource);

    Assert.False(result.IsValid);
    Assert.Equal("EXPLANATION_FORBIDDEN_FIELD", result.RefusalCode);
  }

  private static readonly EvidenceSourceContext NoSource = new(null, null);

  [Fact]
  public void Translation_is_passed_through_trimmed()
  {
    var json = """
      {
        "correctAnswer": "B",
        "shortReason": "Đoạn văn nêu rõ.",
        "evidence": ["sample passage evidence"],
        "translation": "  Câu hỏi: chọn một. Bằng chứng: đoạn hai nói vậy.  "
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "B", ExamModule.Reading, ReadingSource);

    Assert.True(result.IsValid);
    Assert.Equal("Câu hỏi: chọn một. Bằng chứng: đoạn hai nói vậy.", result.Explanation!.Translation);
  }

  [Fact]
  public void Blank_translation_becomes_null()
  {
    var json = """
      {
        "correctAnswer": "B",
        "shortReason": "Đoạn văn nêu rõ.",
        "evidence": ["sample passage evidence"],
        "translation": "   "
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "B", ExamModule.Reading, ReadingSource);

    Assert.True(result.IsValid);
    Assert.Null(result.Explanation!.Translation);
  }

  [Fact]
  public void Empty_evidence_is_accepted_when_no_source_is_available()
  {
    var json = """
      {
        "correctAnswer": "B",
        "shortReason": "Không có đoạn văn để trích.",
        "evidence": []
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "B", ExamModule.Reading, NoSource);

    Assert.True(result.IsValid, result.RefusalCode);
    Assert.Empty(result.Explanation!.Evidence);
  }

  [Fact]
  public void Prompt_sourced_evidence_is_accepted_when_no_source_is_available()
  {
    var json = """
      {
        "correctAnswer": "B",
        "shortReason": "Câu hỏi tự nêu.",
        "evidence": [{ "source": "prompt", "quote": "Pick one" }]
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "B", ExamModule.Reading, NoSource);

    Assert.True(result.IsValid, result.RefusalCode);
    Assert.Equal(["Pick one"], result.Explanation!.Evidence);
  }

  [Fact]
  public void Empty_evidence_is_still_refused_when_a_source_is_available()
  {
    var json = """
      {
        "correctAnswer": "B",
        "shortReason": "Lười trích dẫn.",
        "evidence": []
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "B", ExamModule.Reading, ReadingSource);

    Assert.False(result.IsValid);
    Assert.Equal("EXPLANATION_SCHEMA_INVALID", result.RefusalCode);
  }

  [Theory]
  [InlineData("\"Cartography.\"", "cartography", true)]
  [InlineData("“Cartography”.", "cartography", true)]
  [InlineData("  the   Nile  ", "The Nile", true)]
  [InlineData("topography", "cartography", false)]
  [InlineData("B", "C", false)]
  public void Answer_match_tolerates_quotes_spacing_and_full_stop_but_not_a_different_answer(
      string claimed, string expected, bool matches)
  {
    Assert.Equal(matches, ExplanationOutputValidator.AnswerMatches(expected, claimed));
  }

  [Fact]
  public void Quoted_answer_with_full_stop_is_accepted_end_to_end()
  {
    var json = """
      {
        "correctAnswer": "\"Cartography.\"",
        "shortReason": "Đoạn văn nêu rõ.",
        "evidence": ["sample passage evidence"]
      }
      """;

    var result = ExplanationOutputValidator.Validate(json, "cartography", ExamModule.Reading, ReadingSource);

    Assert.True(result.IsValid, result.RefusalCode);
  }
}

public sealed class ExplanationQuestionTextTests
{
  [Fact]
  public void A_gap_fill_question_sends_the_note_line_that_holds_its_gap()
  {
    var group = new QuestionGroup(
        "g1", "Children's Engineering Workshops", "Write ONE WORD for each answer.", null,
        "Tiny Engineers\n• Create a [1] so they can drop it.\n• Build the tallest [2].", false);
    var q2 = new Question("l-2", 2, QuestionType.Completion, "Question 2", [], 1, null, group);

    var text = ExplanationQuestionText.Compose(q2);

    // The importer's "Question 2" placeholder names the item; it is not a question.
    Assert.DoesNotContain("Question 2", text);

    Assert.Contains("Group: Children's Engineering Workshops", text);
    Assert.Contains("Instruction: Write ONE WORD for each answer.", text);
    Assert.Contains("Sentence with the gap [2]: • Build the tallest [2].", text);
    Assert.DoesNotContain("[1] so they can drop it", text);
  }

  [Fact]
  public void A_question_with_its_own_prompt_and_no_group_sends_the_prompt()
  {
    var q = new Question("r-1", 1, QuestionType.MultipleChoice, "Pick one", [], null, null);
    Assert.Equal("Pick one", ExplanationQuestionText.Compose(q));
  }
}
