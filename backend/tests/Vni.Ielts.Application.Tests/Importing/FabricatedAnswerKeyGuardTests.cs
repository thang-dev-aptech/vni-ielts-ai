using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

/// <summary>
/// <b>Written from an observed failure, not from a worry.</b> On 2026-09-02 the
/// first live run of AI-assisted import was handed <c>TEST 2-R.docx</c> — an
/// IELTS Reading paper that ends at question 40's options and contains no
/// answer key — with a prompt whose third numbered rule forbade inventing one.
///
/// Forty answers came back. Five were wrong against the paper's real key, and
/// <b>all forty passed <c>ExamPackageValidator</c></b>, because a fabricated
/// answer key is perfectly well-formed JSON. Schema validity is a statement
/// about shape.
///
/// These tests pin the check that does not ask the model anything: if no key
/// went in, no key may come out.
/// </summary>
public sealed class FabricatedAnswerKeyGuardTests
{
    private const string PackageWithKeys = """
        {
          "sections": [{
            "module": "reading",
            "parts": [{
              "questions": [
                { "id": "r-1", "order": 1, "answerKey": { "accepted": ["father"] } },
                { "id": "r-2", "order": 2, "answerKey": { "accepted": ["music"] } }
              ]
            }]
          }]
        }
        """;

    private const string PackageWithoutKeys = """
        {
          "sections": [{
            "module": "reading",
            "parts": [{
              "questions": [
                { "id": "r-1", "order": 1 },
                { "id": "r-2", "order": 2 }
              ]
            }]
          }]
        }
        """;

    [Fact]
    public void An_answer_key_with_no_key_document_is_refused()
    {
        var findings = FabricatedAnswerKeyGuard.Inspect(PackageWithKeys, sourceIncludesAnswerKey: false);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("error", f.Severity));
        Assert.All(findings, f => Assert.Equal(FabricatedAnswerKeyGuard.FindingCode, f.Code));

        // One summary plus one per offending question, so a reviewer can see
        // both the scale of it and where it is.
        Assert.Equal(3, findings.Count);
        Assert.Contains(findings, f => f.Path == "/sections/0/parts/0/questions/0/answerKey");
        Assert.Contains(findings, f => f.Path == "/sections/0/parts/0/questions/1/answerKey");
    }

    /// <summary>
    /// <b>The half that stops this being a check that always fires.</b> A guard
    /// that refused every package would be removed within a week by whoever was
    /// trying to import one.
    /// </summary>
    [Fact]
    public void The_same_package_is_accepted_when_a_key_document_was_supplied()
    {
        Assert.Empty(FabricatedAnswerKeyGuard.Inspect(PackageWithKeys, sourceIncludesAnswerKey: true));
    }

    [Fact]
    public void A_package_with_no_answer_keys_passes_either_way()
    {
        Assert.Empty(FabricatedAnswerKeyGuard.Inspect(PackageWithoutKeys, sourceIncludesAnswerKey: false));
        Assert.Empty(FabricatedAnswerKeyGuard.Inspect(PackageWithoutKeys, sourceIncludesAnswerKey: true));
    }

    /// <summary>
    /// Malformed JSON belongs to the validator, which reports it with a path and
    /// a reason. Throwing here would replace that message with a stack trace
    /// about a guard the reader has never heard of.
    /// </summary>
    [Fact]
    public void Malformed_output_is_left_to_the_validator()
    {
        Assert.Empty(FabricatedAnswerKeyGuard.Inspect("{ not json", sourceIncludesAnswerKey: false));
    }
}
