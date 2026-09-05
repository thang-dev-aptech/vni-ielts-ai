using System.Text.Json;
using Vni.Ielts.Application.Explanations;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Explanations;

public sealed class EvidenceSafetyValidatorTests
{
  [Fact]
  public void Reading_evidence_must_exist_in_passage()
  {
    var source = new EvidenceSourceContext("Alpha beta gamma delta.", null);
    var item = JsonSerializer.SerializeToElement("beta gamma");

    var result = EvidenceSafetyValidator.ValidateItem(item, ExamModule.Reading, source);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void Reading_evidence_not_in_passage_is_refused()
  {
    var source = new EvidenceSourceContext("Alpha beta gamma delta.", null);
    var item = JsonSerializer.SerializeToElement("epsilon zeta");

    var result = EvidenceSafetyValidator.ValidateItem(item, ExamModule.Reading, source);

    Assert.False(result.IsValid);
    Assert.Equal("EXPLANATION_EVIDENCE_NOT_FOUND", result.RefusalCode);
  }

  [Fact]
  public void Listening_transcript_timestamp_requires_transcript_source()
  {
    var source = new EvidenceSourceContext(null, "Speaker: sample transcript evidence here.");
    var item = JsonSerializer.SerializeToElement(new
    {
      source = "transcript",
      quote = "sample transcript evidence",
      start = 0,
      end = 10,
    });

    var result = EvidenceSafetyValidator.ValidateItem(item, ExamModule.Listening, source);

    Assert.True(result.IsValid);
  }

  [Fact]
  public void Listening_timestamp_evidence_without_transcript_is_refused()
  {
    var source = new EvidenceSourceContext(null, null);
    var item = JsonSerializer.SerializeToElement(new
    {
      source = "transcript",
      quote = "anything",
      start = 0,
      end = 5,
    });

    var result = EvidenceSafetyValidator.ValidateItem(item, ExamModule.Listening, source);

    Assert.False(result.IsValid);
    Assert.Equal("EXPLANATION_TRANSCRIPT_UNAVAILABLE", result.RefusalCode);
  }
}
