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
}
