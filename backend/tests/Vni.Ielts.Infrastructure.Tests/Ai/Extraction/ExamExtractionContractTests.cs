using System.Text.Json.Nodes;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Ai.Extraction;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Extraction;

/// <summary>
/// The contract itself, and the server-side validation that stands behind it.
/// </summary>
public sealed class ExamExtractionContractTests
{
    /// <summary>
    /// Fields the contract must not have, whatever a provider proposes.
    ///
    /// <para>
    /// <b>Timing is a rule of the test and a raw-to-band table is equated per
    /// exam version</b> (CLAUDE.md rule 4). A model asked for either will
    /// produce a plausible number, and a rounded band boundary is a wrong band
    /// for every learner near it. A contract with no field for them cannot
    /// carry one.
    /// </para>
    /// </summary>
    private static readonly string[] ForbiddenProperties =
    [
        "variant",
        "timing",
        "duration",
        "durationminutes",
        "timelimit",
        "scoring",
        "scoringprofile",
        "bandtable",
        "band",
        "rawscore",
        "marks",
        "packageid",
    ];

    [Fact]
    public void Every_object_in_the_contract_is_closed()
    {
        var open = new List<string>();
        Walk(ExamExtractionSchema.LoadNode(), "#", open);

        Assert.True(
            open.Count == 0,
            "These objects do not set additionalProperties: false, so a model — or an injected "
            + $"instruction — could add a field to them: {string.Join(", ", open)}");
    }

    [Fact]
    public void The_contract_has_no_variant_timing_or_scoring_field()
    {
        var found = new List<string>();
        Properties(ExamExtractionSchema.LoadNode(), found);

        var forbidden = found
            .Where(name => ForbiddenProperties.Contains(name.ToLowerInvariant()))
            .ToArray();

        Assert.True(
            forbidden.Length == 0,
            $"The extraction contract must not ask a model for {string.Join(", ", forbidden)}.");
    }

    [Fact]
    public void The_contract_pins_the_version_the_server_validates()
    {
        var contractVersion = ExamExtractionSchema.LoadNode()["properties"]?["contractVersion"]?["enum"]
            ?.AsArray()
            .Select(value => value!.GetValue<string>())
            .ToArray();

        Assert.NotNull(contractVersion);
        Assert.Equal([ExamExtractionContract.Version], contractVersion!);
    }

    [Fact]
    public void The_contract_declares_the_closed_classification_set()
    {
        var classifications = ExamExtractionSchema.LoadNode()["$defs"]?["classification"]?["enum"]
            ?.AsArray()
            .Select(value => value!.GetValue<string>())
            .ToArray();

        Assert.NotNull(classifications);
        Assert.Equal(Enum.GetNames<ParsedExamClassification>(), classifications!);
    }

    /// <summary>
    /// <b>Confidence is bounded, not enumerated, and that is not an exception
    /// to the enum-not-range rule.</b> That rule is about a band: a decided
    /// value where <c>6.3</c> must be impossible to express. Confidence decides
    /// nothing — no code reads it — so enumerating tenths would only have
    /// rejected a legitimate <c>0.85</c> and invented a granularity no provider
    /// agreed to.
    /// </summary>
    [Fact]
    public void Confidence_is_a_nullable_bounded_number_and_not_an_enumeration()
    {
        var confidence = ExamExtractionSchema.LoadNode()["$defs"]?["confidence"]?.AsObject();

        Assert.NotNull(confidence);
        Assert.False(confidence!.ContainsKey("enum"));
        Assert.Equal(0, confidence["minimum"]!.GetValue<decimal>());
        Assert.Equal(1, confidence["maximum"]!.GetValue<decimal>());

        var types = confidence["type"]!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
        Assert.Equal(["number", "null"], types);
    }

    /// <summary>
    /// The value the enumerated version rejected.
    /// </summary>
    [Fact]
    public void A_confidence_between_two_tenths_is_accepted()
    {
        var json = Fixture("reading-classified-valid.json")
            .Replace("\"confidence\": 0.8,", "\"confidence\": 0.85,", StringComparison.Ordinal);

        var candidate = ExamExtractionValidator.ToCandidate(
            json, "candidate-1", "package-1", Index(), Cap);

        Assert.Equal(0.85m, candidate.Confidence);
        Assert.Equal(0.85m, candidate.Modules.Single().Confidence);
    }

    /// <summary>
    /// <b>The range is enforced twice, and the second time is the one that
    /// counts.</b> Provider-side schema enforcement is a convenience this
    /// server assumes failed, so the mapper re-checks the bound on a payload
    /// that never went through a provider's validator at all.
    /// </summary>
    [Theory]
    [InlineData("1.4")]
    [InlineData("-0.2")]
    public void A_confidence_outside_the_range_is_refused_by_the_server(string value)
    {
        var json = Fixture("reading-classified-valid.json")
            .Replace("\"confidence\": 0.8,", $"\"confidence\": {value},", StringComparison.Ordinal);

        var rejected = Assert.Throws<ExamExtractionRejectedException>(() =>
            ExamExtractionValidator.ToCandidate(json, "candidate-1", "package-1", Index(), Cap));

        // The schema catches it first; either way the code is stable and the
        // message carries no payload.
        Assert.Contains(
            rejected.Code,
            new[] { ExamExtractionRejection.SchemaInvalid, ExamExtractionRejection.ConfidenceRange });
        Assert.DoesNotContain(value, rejected.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mapper's own bound, reached with the schema out of the way — the
    /// path that runs when a provider's structured-output enforcement is what
    /// failed.
    /// </summary>
    [Fact]
    public void The_mapper_refuses_an_out_of_range_confidence_on_its_own()
    {
        var rejected = Assert.Throws<ExamExtractionRejectedException>(() =>
            ExamExtractionMapper.ToCandidate(
                new ExamExtractionDto(
                    ExamExtractionContract.Version, null, "NeedsReview", 1.4m, []),
                "candidate-1",
                "package-1",
                Index()));

        Assert.Equal(ExamExtractionRejection.ConfidenceRange, rejected.Code);
    }

    [Fact]
    public void A_classified_response_validates_and_maps_to_a_review_candidate()
    {
        var candidate = ExamExtractionValidator.ToCandidate(
            Fixture("reading-classified-valid.json"), "candidate-1", "package-1", Index(), Cap);

        Assert.Equal(ParsedExamClassification.Reading, candidate.Classification);
        Assert.Equal(ParsedCandidateStatus.PendingReview, candidate.Status);
        Assert.Equal("Synthetic Reading Practice A", candidate.Title);

        var questions = candidate.Modules.Single().Parts.Single().Questions;
        Assert.Equal([1, 2, 5], questions.Select(question => question.Order));
        Assert.Equal(["B"], questions[0].AnswerKey!.Accepted);
        Assert.Null(questions[2].AnswerKey);
        Assert.Equal("reading/paper.docx", questions[0].Provenance.FileName);
        Assert.Equal(2, questions[1].Provenance.Page);
    }

    [Fact]
    public void An_unclassified_response_validates_and_maps()
    {
        var candidate = ExamExtractionValidator.ToCandidate(
            Fixture("unclassified-valid.json"), "candidate-1", "package-1", Index(), Cap);

        Assert.Equal(ParsedExamClassification.Unclassified, candidate.Classification);
        Assert.Null(candidate.Modules.Single().Module);
        Assert.Null(candidate.Confidence);
    }

    [Fact]
    public void A_needs_review_response_validates_and_maps()
    {
        var candidate = ExamExtractionValidator.ToCandidate(
            Fixture("needs-review-valid.json"), "candidate-1", "package-1", Index(), Cap);

        Assert.Equal(ParsedExamClassification.NeedsReview, candidate.Classification);
        Assert.Empty(candidate.Modules);
    }

    [Theory]
    [InlineData("malformed-json.json", ExamExtractionRejection.MalformedJson)]
    [InlineData("extra-property.json", ExamExtractionRejection.SchemaInvalid)]
    [InlineData("invalid-enum-classification.json", ExamExtractionRejection.SchemaInvalid)]
    [InlineData("dangling-source-reference.json", ExamExtractionRejection.DanglingSource)]
    [InlineData("dangling-chunk-reference.json", ExamExtractionRejection.DanglingChunk)]
    [InlineData("fabricated-answer-key.json", ExamExtractionRejection.AnswerKeyNotAnOption)]
    [InlineData("duplicate-option-key.json", ExamExtractionRejection.DuplicateOptionKey)]
    [InlineData("answer-key-without-options.json", ExamExtractionRejection.AnswerKeyWithoutOptions)]
    public void A_hostile_or_malformed_response_is_refused_with_a_stable_code(
        string fixture, string expectedCode)
    {
        var rejected = Assert.Throws<ExamExtractionRejectedException>(() =>
            ExamExtractionValidator.ToCandidate(
                Fixture(fixture), "candidate-1", "package-1", Index(), Cap));

        Assert.Equal(expectedCode, rejected.Code);
    }

    [Fact]
    public void An_oversized_response_is_refused_before_it_is_parsed()
    {
        var rejected = Assert.Throws<ExamExtractionRejectedException>(() =>
            ExamExtractionValidator.ToCandidate(
                Fixture("oversized-response.json"), "candidate-1", "package-1", Index(),
                maxResponseBytes: 8192));

        Assert.Equal(ExamExtractionRejection.ResponseTooLarge, rejected.Code);
    }

    /// <summary>
    /// A refusal is the message most likely to be logged verbatim and pasted
    /// into a ticket, so it must not carry the payload that caused it.
    /// </summary>
    [Fact]
    public void A_refusal_message_carries_neither_the_offending_value_nor_the_limit()
    {
        var schema = Assert.Throws<ExamExtractionRejectedException>(() =>
            ExamExtractionValidator.EnsureSchemaValid(Fixture("extra-property.json")));

        Assert.DoesNotContain("Academic", schema.Message, StringComparison.Ordinal);

        var oversized = Assert.Throws<ExamExtractionRejectedException>(() =>
            ExamExtractionValidator.EnsureWithinSizeLimit(Fixture("oversized-response.json"), 8192));

        Assert.DoesNotContain("8192", oversized.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic passage sentence", oversized.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_response_measured_in_bytes_rather_than_characters()
    {
        // Three bytes per character in UTF-8; a character count would let this
        // through a cap it exceeds threefold.
        var json = "{\"contractVersion\":\"" + new string('ế', 40) + "\"}";

        Assert.Throws<ExamExtractionRejectedException>(() =>
            ExamExtractionValidator.EnsureWithinSizeLimit(json, 80));
    }

    private const int Cap = 256 * 1024;

    private static ExamExtractionSourceIndex Index() => new(
    [
        new ExamExtractionSourceBinding("s1", "reading/paper.docx", "sha-1",
        [
            new ExamExtractionChunkBinding("s1c1", null, "Part 1"),
            new ExamExtractionChunkBinding("s1c2", 2, "Part 2"),
        ]),
    ]);

    private static void Walk(JsonNode? node, string path, List<string> open)
    {
        switch (node)
        {
            case JsonObject obj:
                var declaresObject = obj["type"]?.ToString() == "object" || obj.ContainsKey("properties");
                if (declaresObject && obj["additionalProperties"]?.GetValue<bool>() is not false)
                    open.Add(path);

                foreach (var (key, value) in obj) Walk(value, $"{path}/{key}", open);
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++) Walk(array[i], $"{path}/{i}", open);
                break;
        }
    }

    private static void Properties(JsonNode? node, List<string> found)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["properties"] is JsonObject properties)
                {
                    foreach (var (name, _) in properties) found.Add(name);
                }

                foreach (var (_, value) in obj) Properties(value, found);
                break;

            case JsonArray array:
                foreach (var item in array) Properties(item, found);
                break;
        }
    }

    private static string Fixture(string name) =>
        File.ReadAllText(ExtractionFixtures.Find($"fixtures/ai/exam-extraction/{name}"));
}

/// <summary>
/// Where the synthetic and hostile extraction fixtures live.
///
/// <b>Resolved by walking up from the test binary</b>, the same way the
/// recorded writing fixtures are found: the working directory of a test host is
/// its output folder, not the repository.
/// </summary>
internal static class ExtractionFixtures
{
    public static string Find(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
