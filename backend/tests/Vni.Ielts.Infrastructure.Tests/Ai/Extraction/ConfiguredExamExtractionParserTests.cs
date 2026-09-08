using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Extraction;
using Vni.Ielts.Infrastructure.Observability;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Extraction;

/// <summary>
/// What the configured parser decides, and what it refuses to decide.
/// </summary>
public sealed class ConfiguredExamExtractionParserTests
{
    [Fact]
    public async Task Parse_duration_is_recorded_on_success()
    {
        using var readings = Listen<double>("vni.exam_parsing.duration");

        await Parser(new Runs(), new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json")))
            .ParseAsync([Document()], default);

        var reading = Assert.Single(readings.Values);
        Assert.True(reading.Value >= 0);
        Assert.Equal("OpenAi", reading.Provider);
    }

    [Fact]
    public async Task Parse_failure_increments_failure_counter()
    {
        using var readings = Listen<long>("vni.exam_parsing.failures");
        var parser = Parser(
            new Runs(),
            new FakeClient("OpenAi", _ => throw new TransientExamExtractionException("provider unavailable")));

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => parser.ParseAsync([Document()], default));

        var reading = Assert.Single(readings.Values);
        Assert.Equal(1, reading.Value);
        Assert.Equal("OpenAi", reading.Provider);
        Assert.Equal(ExamExtractionRejection.TransientFailure, reading.FailureCode);
    }

    [Fact]
    public async Task Token_counts_are_recorded_from_run_metadata()
    {
        using var input = Listen<long>("vni.exam_parsing.tokens.input");
        using var output = Listen<long>("vni.exam_parsing.tokens.output");

        await Parser(new Runs(), new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json")))
            .ParseAsync([Document()], default);

        var inputReading = Assert.Single(input.Values);
        var outputReading = Assert.Single(output.Values);
        Assert.Equal(11, inputReading.Value);
        Assert.Equal("OpenAi", inputReading.Provider);
        Assert.Equal(22, outputReading.Value);
        Assert.Equal("OpenAi", outputReading.Provider);
    }

    [Fact]
    public async Task An_accepted_answer_becomes_a_pending_review_candidate_and_a_run_record()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"));
        var runs = new Runs();
        var parser = Parser(runs, client);

        var candidate = await parser.ParseAsync([Document()], default);

        Assert.Equal(ParsedCandidateStatus.PendingReview, candidate.Status);
        Assert.Equal(ParsedExamClassification.Reading, candidate.Classification);
        Assert.Equal("package-1", candidate.PackageId);

        var run = Assert.Single(runs.Recorded);
        Assert.Equal("OpenAi", run.Provider);
        Assert.Equal("model-under-test", run.Model);
        Assert.Equal(ExamExtractionPrompt.Version, run.PromptVersion);
        Assert.Equal(ExamExtractionContract.SchemaId, run.SchemaId);
        Assert.Equal(ExamExtractionContract.Version, run.ContractVersion);
        Assert.Equal(candidate.Id, run.CandidateId);
        Assert.Null(run.FailureCode);
        Assert.Equal(64, run.InputHash.Length);
    }

    /// <summary>
    /// <b>A candidate is the point at which a package becomes reviewable</b>, so
    /// a refused answer must not produce one — a reviewer opening an empty
    /// proposal has no way to tell it apart from a paper the model could not
    /// read.
    /// </summary>
    [Fact]
    public async Task A_refused_answer_produces_no_candidate_and_is_recorded_with_its_code()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("dangling-source-reference.json"));
        var runs = new Runs();
        var parser = Parser(runs, client);

        var rejected = await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => parser.ParseAsync([Document()], default));

        Assert.Equal(ExamExtractionRejection.DanglingSource, rejected.Code);

        var run = Assert.Single(runs.Recorded);
        Assert.Null(run.CandidateId);
        Assert.Equal(ExamExtractionRejection.DanglingSource, run.FailureCode);
    }

    /// <summary>
    /// A shape the provider got wrong is the same shape on the next call, and
    /// the package is waiting for a reviewer while the budget is spent finding
    /// that out.
    /// </summary>
    [Fact]
    public async Task A_refused_answer_is_not_retried_even_when_a_retry_budget_exists()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("extra-property.json"));
        var parser = Parser(new Runs(), client, options => options.MaxAttempts = 3);

        await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => parser.ParseAsync([Document()], default));

        Assert.Equal(1, client.Calls);
    }

    /// <summary>
    /// <b>One attempt unless somebody configured more.</b> A retry budget
    /// invented here would multiply an unpriced call by a number nobody chose.
    /// → <c>G-11</c>
    /// </summary>
    [Fact]
    public async Task A_transient_failure_is_tried_once_when_no_retry_budget_is_configured()
    {
        var client = new FakeClient("OpenAi", _ => throw new TransientExamExtractionException("stalled"));
        var parser = Parser(new Runs(), client);

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => parser.ParseAsync([Document()], default));

        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task A_configured_retry_budget_is_honoured_and_not_exceeded()
    {
        var client = new FakeClient("OpenAi", _ => throw new TransientExamExtractionException("stalled"));
        var parser = Parser(new Runs(), client, options => options.MaxAttempts = 3);

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => parser.ParseAsync([Document()], default));

        Assert.Equal(3, client.Calls);
    }

    [Fact]
    public async Task A_configured_fallback_provider_is_tried_after_a_transient_failure()
    {
        var primary = new FakeClient("OpenAi", _ => throw new TransientExamExtractionException("stalled"));
        var fallback = new FakeClient("Gemini", _ => Fixture("reading-classified-valid.json"));
        var runs = new Runs();

        var parser = Parser(
            runs,
            [primary, fallback],
            options => options.FallbackProvider = "Gemini");

        var candidate = await parser.ParseAsync([Document()], default);

        Assert.Equal(ParsedExamClassification.Reading, candidate.Classification);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, fallback.Calls);

        /*
         * Two runs, not one: the attempt that failed is part of what happened.
         * A single record showing Gemini answering would read as a package that
         * parsed first time, hiding the provider that did not.
         */
        Assert.Equal(
            [("OpenAi", ExamExtractionRejection.TransientFailure), ("Gemini", null)],
            runs.Recorded.Select(run => (run.Provider, run.FailureCode)));

        Assert.Equal(candidate.Id, runs.Recorded[1].CandidateId);
    }

    /// <summary>
    /// <b>No fallback configured means no fallback</b>, not "try the other one
    /// that happens to be registered". Which provider parses a paper is a
    /// decision with a cost and a calibration consequence.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_second_provider_is_never_reached()
    {
        var primary = new FakeClient("OpenAi", _ => throw new TransientExamExtractionException("stalled"));
        var other = new FakeClient("Gemini", _ => Fixture("reading-classified-valid.json"));

        var parser = Parser(new Runs(), [primary, other]);

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => parser.ParseAsync([Document()], default));

        Assert.Equal(0, other.Calls);
    }

    /// <summary>
    /// <b>The rights gate is a different question from the egress guard.</b> An
    /// exam paper is somebody's copyright and nobody's personal data, so the
    /// egress guard clears it trivially — and an upload nobody has cleared
    /// still must not reach a third organisation.
    /// </summary>
    [Fact]
    public async Task A_reseller_is_refused_restricted_material_before_a_call_is_made()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"), reseller: true);
        var parser = Parser(new Runs(), client, options => options.SourceRights = null);

        var rejected = await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => parser.ParseAsync([Document()], default));

        Assert.Equal(ExamExtractionRejection.ResellerRestrictedSource, rejected.Code);
        Assert.Equal(0, client.Calls);
    }

    [Theory]
    [InlineData("Synthetic")]
    [InlineData("RightsCleared")]
    public async Task A_reseller_may_be_shown_synthetic_or_rights_cleared_material(string rights)
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"), reseller: true);
        var parser = Parser(new Runs(), client, options => options.SourceRights = rights);

        await parser.ParseAsync([Document()], default);

        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task Restricted_material_may_still_go_to_the_vendors_own_endpoint()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"));
        var parser = Parser(new Runs(), client, options => options.SourceRights = "Restricted");

        await parser.ParseAsync([Document()], default);

        Assert.Equal(1, client.Calls);
    }

    /// <summary>
    /// An encrypted or image-only upload is already an actionable extraction
    /// finding on the package; calling a provider with an empty prompt would
    /// spend money to be told nothing.
    /// </summary>
    [Fact]
    public async Task A_package_with_no_extractable_text_calls_nobody_and_still_becomes_reviewable()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"));
        var runs = new Runs();
        var parser = Parser(runs, client);

        var candidate = await parser.ParseAsync(
            [Document() with { Outcome = DocumentExtractionOutcome.Encrypted, Chunks = [] }], default);

        Assert.Equal(0, client.Calls);
        Assert.Empty(runs.Recorded);
        Assert.Equal(ParsedExamClassification.NeedsReview, candidate.Classification);
        Assert.Empty(candidate.Modules);
        Assert.Equal("reading/paper.docx", Assert.Single(candidate.Sources).FileName);
    }

    /// <summary>
    /// <b>Safe provider metadata is mandatory, so it fails closed.</b> The
    /// caller persists whatever this parser returns; a candidate that reached a
    /// reviewer with no record of the provider, model or prompt behind it would
    /// be indistinguishable from one whose provenance was never required. The
    /// audit fact and the artefact land together or neither lands.
    /// </summary>
    [Fact]
    public async Task An_unrecordable_run_fails_the_parse_rather_than_returning_a_candidate()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"));
        var parser = Parser(new Runs(fail: true), client);

        var failed = await Assert.ThrowsAsync<ExamExtractionRunNotRecordedException>(
            () => parser.ParseAsync([Document()], default));

        Assert.Equal("package-1", failed.PackageId);

        // The message is read on support and must carry nothing from the paper.
        Assert.DoesNotContain("Synthetic", failed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("1998", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The asymmetry is deliberate.</b> On a failure path the parse is
    /// already failing and no candidate will be persisted, so replacing the
    /// refusal with a storage error would only send whoever reads it to the
    /// wrong system.
    /// </summary>
    [Fact]
    public async Task An_unrecordable_failed_attempt_still_reports_the_original_failure()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("dangling-source-reference.json"));
        var parser = Parser(new Runs(fail: true), client);

        var rejected = await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => parser.ParseAsync([Document()], default));

        Assert.Equal(ExamExtractionRejection.DanglingSource, rejected.Code);
    }

    /// <summary>
    /// <b>A timed-out attempt is a run too.</b> Without a record, a package
    /// that eventually parsed on the second try looks like one that parsed
    /// first time, and a route failing half its calls stays invisible until
    /// somebody happens to read a log.
    /// </summary>
    [Fact]
    public async Task A_transient_failure_is_recorded_with_a_stable_code_and_the_configured_model()
    {
        var client = new FakeClient("OpenAi", _ => throw new TransientExamExtractionException("stalled"));
        var runs = new Runs();
        var parser = Parser(runs, client, options => options.MaxAttempts = 2);

        await Assert.ThrowsAsync<TransientExamExtractionException>(
            () => parser.ParseAsync([Document()], default));

        Assert.Equal(2, runs.Recorded.Count);

        foreach (var run in runs.Recorded)
        {
            Assert.Equal(ExamExtractionRejection.TransientFailure, run.FailureCode);
            Assert.Null(run.CandidateId);
            Assert.Equal("OpenAi", run.Provider);
            Assert.Equal("model-under-test", run.Model);
            Assert.Equal(ExamExtractionPrompt.Version, run.PromptVersion);
            Assert.Equal(64, run.InputHash.Length);
            Assert.Equal(1, run.SourceCount);
            Assert.Equal(0, run.InputTokens);
            Assert.NotEqual(default, run.RequestedAt);
            Assert.NotEqual(default, run.CompletedAt);
            Assert.NotEmpty(run.RequestId);
        }

        // One attempt, one record: a redelivery must not be readable as a
        // single call that failed twice.
        Assert.Equal(2, runs.Recorded.Select(run => run.RequestId).Distinct().Count());
    }

    [Fact]
    public async Task An_egress_refusal_is_recorded_with_its_own_code_and_not_retried()
    {
        var client = new FakeClient(
            "OpenAi",
            _ => throw new AiEgressRefusedException("no key", AiEgressRefusal.NotConfigured));

        var runs = new Runs();
        var parser = Parser(runs, client, options => options.MaxAttempts = 3);

        await Assert.ThrowsAsync<AiEgressRefusedException>(
            () => parser.ParseAsync([Document()], default));

        var run = Assert.Single(runs.Recorded);
        Assert.Equal(ExamExtractionRejection.EgressRefused, run.FailureCode);
        Assert.Equal("model-under-test", run.Model);
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task A_rights_refusal_is_recorded_even_though_no_call_was_made()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"), reseller: true);
        var runs = new Runs();
        var parser = Parser(runs, client, options => options.SourceRights = "Restricted");

        await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => parser.ParseAsync([Document()], default));

        var run = Assert.Single(runs.Recorded);
        Assert.Equal(ExamExtractionRejection.ResellerRestrictedSource, run.FailureCode);
        Assert.Equal(0, client.Calls);
    }

    /// <summary>
    /// <b>A validation refusal keeps what the provider actually answered.</b>
    /// Recording <c>model=unknown</c> for a response that named a model would
    /// make the one question a run record exists to answer — which model
    /// produced this shape — unanswerable for exactly the runs where it
    /// matters.
    /// </summary>
    [Fact]
    public async Task A_refused_answer_keeps_the_response_model_request_id_and_token_counts()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("fabricated-answer-key.json"));
        var runs = new Runs();

        await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => Parser(runs, client).ParseAsync([Document()], default));

        var run = Assert.Single(runs.Recorded);
        Assert.Equal(ExamExtractionRejection.AnswerKeyNotAnOption, run.FailureCode);
        Assert.Equal("provider-request-1", run.RequestId);
        Assert.Equal("model-under-test", run.Model);
        Assert.Equal(11, run.InputTokens);
        Assert.Equal(22, run.OutputTokens);
    }

    /// <summary>
    /// <b>A cancelled caller writes no run, and that is a choice.</b> A
    /// cancelled call has no completion to describe — the worker is shutting
    /// down or the job was abandoned — and inventing a completion timestamp for
    /// it would put a run in the audit trail that never finished. The package is
    /// untouched and the next poll starts over.
    /// </summary>
    [Fact]
    public async Task A_cancelled_caller_writes_no_run_record()
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        var client = new FakeClient("OpenAi", _ => throw new OperationCanceledException());
        var runs = new Runs();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Parser(runs, client).ParseAsync([Document()], caller.Token));

        Assert.Empty(runs.Recorded);
    }

    /// <summary>
    /// Re-parsing the same bytes has to address the same candidate rather than
    /// accumulating one per attempt, or a reviewer sees several proposals for
    /// one package with no way to tell which is current.
    /// </summary>
    [Fact]
    public async Task The_candidate_identifier_is_derived_from_the_document_bytes()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"));

        var first = await Parser(new Runs(), client).ParseAsync([Document()], default);
        var second = await Parser(new Runs(), client).ParseAsync([Document()], default);
        var other = await Parser(new Runs(), client)
            .ParseAsync([Document() with { Sha256 = "sha-other" }], default);

        Assert.Equal(first.Id, second.Id);
        Assert.NotEqual(first.Id, other.Id);
    }

    [Fact]
    public async Task Documents_from_two_packages_are_refused_rather_than_merged()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"));
        var parser = Parser(new Runs(), client);

        var rejected = await Assert.ThrowsAsync<ExamExtractionRejectedException>(
            () => parser.ParseAsync(
                [Document(), Document() with { PackageId = "package-2" }], default));

        Assert.Equal(ExamExtractionRejection.PackageMismatch, rejected.Code);
        Assert.Equal(0, client.Calls);
    }

    /// <summary>
    /// The provider sees <c>s1</c> and <c>s1c1</c>. The archive entry path is
    /// how a reviewer finds the document again and is also a string an uploader
    /// chose, so it stays on this side.
    /// </summary>
    [Fact]
    public async Task The_provider_is_given_opaque_identifiers_and_no_paths()
    {
        ExamExtractionRequest? seen = null;
        var client = new FakeClient("OpenAi", request =>
        {
            seen = request;
            return Fixture("reading-classified-valid.json");
        });

        await Parser(new Runs(), client).ParseAsync([Document()], default);

        var source = Assert.Single(seen!.Sources);
        Assert.Equal("s1", source.SourceId);
        Assert.Equal("s1c1", source.Chunks[0].ChunkId);
        Assert.DoesNotContain("paper.docx", string.Join(' ', source.Chunks.Select(chunk => chunk.Text)));
        Assert.Equal(ExamExtractionPrompt.Version, seen.PromptVersion);
        Assert.Equal(30, seen.TimeoutSeconds);
    }

    /// <summary>
    /// Provenance is bound from the extractor's own chunk table, so a model
    /// that cites a real chunk with the wrong page cannot move where a reviewer
    /// looks.
    /// </summary>
    [Fact]
    public async Task Provenance_is_rebound_to_the_real_entry_path_and_page()
    {
        var client = new FakeClient("OpenAi", _ => Fixture("reading-classified-valid.json"));

        var candidate = await Parser(new Runs(), client).ParseAsync([Document()], default);
        var question = candidate.Modules.Single().Parts.Single().Questions[1];

        Assert.Equal("reading/paper.docx", question.Provenance.FileName);
        Assert.Equal(2, question.Provenance.Page);
        Assert.Equal("sha-1", question.Provenance.Reference);
    }

    private static ConfiguredExamExtractionParser Parser(
        Runs runs, FakeClient client, Action<ExamParsingOptions>? configure = null) =>
        Parser(runs, [client], configure);

    private static ConfiguredExamExtractionParser Parser(
        Runs runs, IReadOnlyList<FakeClient> clients, Action<ExamParsingOptions>? configure = null)
    {
        var options = new ExamParsingOptions
        {
            Enabled = true,
            Provider = clients[0].Provider,
            PromptVersion = ExamExtractionPrompt.Version,
            TimeoutSeconds = 30,
            SourceRights = "Synthetic",
        };

        configure?.Invoke(options);

        return new ConfiguredExamExtractionParser(
            clients,
            Options.Create(options),
            runs,
            new FixedClock(),
            new ExamParsingMetrics(),
            NullLogger<ConfiguredExamExtractionParser>.Instance);
    }

    private static ExtractedSourceDocument Document() => new(
        "package-1",
        "reading/paper.docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "sha-1",
        DocumentExtractionOutcome.Extracted,
        [
            new ExtractedTextChunk("c1", 0, "Synthetic passage one.", null, "Part 1"),
            new ExtractedTextChunk("c2", 1, "Synthetic passage two.", 2, "Part 2"),
        ]);

    private static string Fixture(string name) =>
        File.ReadAllText(ExtractionFixtures.Find($"fixtures/ai/exam-extraction/{name}"));

    private static MetricReadings<T> Listen<T>(string instrumentName) where T : struct
    {
        var readings = new MetricReadings<T>(instrumentName);
        readings.Start();
        return readings;
    }

    private sealed class MetricReadings<T>(string instrumentName) : IDisposable where T : struct
    {
        private readonly MeterListener _listener = new();

        public List<(T Value, string? Provider, string? FailureCode)> Values { get; } = [];

        public void Start()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == Telemetry.MeterName && instrument.Name == instrumentName)
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<T>((_, value, tags, _) =>
            {
                string? provider = null;
                string? failureCode = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "provider") provider = tag.Value?.ToString();
                    if (tag.Key == "failure_code") failureCode = tag.Value?.ToString();
                }
                Values.Add((value, provider, failureCode));
            });
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class FakeClient(
        string provider, Func<ExamExtractionRequest, string> answer, bool reseller = false)
        : IExamExtractionClient
    {
        public string Provider => provider;

        public string Model => "model-under-test";

        public bool IsReseller => reseller;

        public int Calls { get; private set; }

        public Task<ExamExtractionResponse> ExtractAsync(
            ExamExtractionRequest request, CancellationToken ct)
        {
            Calls++;

            return Task.FromResult(new ExamExtractionResponse(
                answer(request), provider, "model-under-test", "provider-request-1", 11, 22));
        }
    }

    private sealed class Runs(bool fail = false) : IExamExtractionRunStore
    {
        public List<ExamExtractionRunMetadata> Recorded { get; } = [];

        public Task RecordAsync(ExamExtractionRunMetadata run, CancellationToken ct)
        {
            if (fail) throw new InvalidOperationException("storage failed");

            Recorded.Add(run);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 7, 3, 0, 0, TimeSpan.Zero);
    }
}

/// <summary>
/// The configuration gate, which decides whether a provider is called at all.
/// </summary>
public sealed class ExamParsingOptionsTests
{
    [Fact]
    public void Disabled_is_the_default_and_reports_no_problems()
    {
        var options = new ExamParsingOptions();

        Assert.False(options.Enabled);
        Assert.Empty(options.Problems(Ai()));
        Assert.False(options.IsConfiguredFor(Ai()));
    }

    /// <summary>
    /// <b>All the problems, not the first one.</b> A gate that stops at the
    /// first fault is answered one deployment at a time.
    /// </summary>
    [Fact]
    public void Enabled_with_nothing_set_names_every_missing_decision_at_once()
    {
        var problems = new ExamParsingOptions { Enabled = true }.Problems(Ai());

        Assert.Contains(problems, problem => problem.Contains("Provider", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("PromptVersion", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("TimeoutSeconds", StringComparison.Ordinal));
    }

    /// <summary>
    /// A stored proposal that cannot name the instruction which produced it
    /// cannot be told from a prompt regression later, so a pin this build does
    /// not carry fails rather than being ignored.
    /// </summary>
    [Fact]
    public void A_prompt_version_this_build_does_not_carry_is_a_problem()
    {
        var problems = Valid(options => options.PromptVersion = "exam-extraction-prompt-v0").Problems(Ai());

        Assert.Contains(problems, problem => problem.Contains("does not carry", StringComparison.Ordinal));
    }

    [Fact]
    public void A_third_provider_is_an_owner_decision_rather_than_a_configuration_value()
    {
        var problems = Valid(options => options.Provider = "Anthropic").Problems(Ai());

        Assert.Contains(problems, problem => problem.Contains("Only OpenAi and Gemini", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unconfigured_provider_cannot_be_selected()
    {
        var problems = Valid().Problems(new AiOptions());

        Assert.Contains(problems, problem => problem.Contains("ApiKey is not set", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>The exclusion is checked here too.</b> The reseller route serves
    /// Claude models beside GPT ones under a section called <c>OpenAi</c>, so a
    /// typo in a config value is otherwise one step from breaking an owner
    /// decision silently. → CLAUDE.md rule 6
    /// </summary>
    [Fact]
    public void An_excluded_model_family_cannot_be_selected()
    {
        var ai = Ai();
        ai.OpenAi.Model = "claude-4-opus";

        var problems = Valid().Problems(ai);

        Assert.Contains(problems, problem => problem.Contains("Claude API is excluded", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>Setting the cap stops parsing rather than enabling a limit.</b> The
    /// configured route publishes no per-token price, so a cap here could not
    /// be enforced — only displayed, which is worse than absent because
    /// whoever set it stops watching.
    /// </summary>
    [Fact]
    public void A_spend_cap_this_build_cannot_enforce_is_a_problem_rather_than_a_silent_no_op()
    {
        var problems = Valid(options => options.MaxCostUsdPerPackage = 5m).Problems(Ai());

        Assert.Contains(problems, problem => problem.Contains("cannot enforce it", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void A_retry_budget_outside_the_permitted_range_is_a_problem(int attempts)
    {
        var problems = Valid(options => options.MaxAttempts = attempts).Problems(Ai());

        Assert.Contains(problems, problem => problem.Contains("MaxAttempts", StringComparison.Ordinal));
    }

    [Fact]
    public void Unset_rights_mean_restricted()
    {
        Assert.Equal(ImportDataClassification.Restricted, new ExamParsingOptions().ResolvedRights());
        Assert.Equal(
            ImportDataClassification.Synthetic,
            new ExamParsingOptions { SourceRights = "synthetic" }.ResolvedRights());
    }

    [Fact]
    public void An_unrecognised_rights_value_is_a_problem_rather_than_a_fallback()
    {
        var problems = Valid(options => options.SourceRights = "Cleared-ish").Problems(Ai());

        Assert.Contains(problems, problem => problem.Contains("SourceRights", StringComparison.Ordinal));
    }

    [Fact]
    public void Unset_policy_values_stay_unset()
    {
        var options = new ExamParsingOptions();

        Assert.Null(options.FallbackProvider);
        Assert.Null(options.MaxAttempts);
        Assert.Null(options.MaxResponseBytes);
        Assert.Null(options.MaxCostUsdPerPackage);
        Assert.Null(options.TimeoutSeconds);
        Assert.Equal(1, options.ResolvedMaxAttempts());
        Assert.Equal(ExamExtractionContract.DefaultMaxResponseBytes, options.ResolvedMaxResponseBytes());
        Assert.Empty(options.ProviderOrder());
    }

    [Fact]
    public void A_valid_configuration_composes_the_provider_parser()
    {
        var options = Valid();

        Assert.True(options.IsConfiguredFor(Ai()));
        Assert.Equal(["OpenAi"], options.ProviderOrder());

        var withFallback = Valid(o => o.FallbackProvider = "Gemini");
        Assert.Equal(["OpenAi", "Gemini"], withFallback.ProviderOrder());

        var duplicated = Valid(o => o.FallbackProvider = "openai");
        Assert.Equal(["OpenAi"], duplicated.ProviderOrder());
    }

    private static ExamParsingOptions Valid(Action<ExamParsingOptions>? configure = null)
    {
        var options = new ExamParsingOptions
        {
            Enabled = true,
            Provider = "OpenAi",
            PromptVersion = ExamExtractionPrompt.Version,
            TimeoutSeconds = 30,
            SourceRights = "Synthetic",
            Environment = "SyntheticTest",
        };

        configure?.Invoke(options);
        return options;
    }

    private static AiOptions Ai() => new()
    {
        OpenAi = new AiProviderOptions { ApiKey = "test-key", Model = "model-under-test" },
        Gemini = new AiProviderOptions { ApiKey = "test-key", Model = "model-under-test" },
    };

    // ── Plan 06 environment classification ──────────────────────────────

    [Fact]
    public void Unset_Environment_with_Enabled_produces_a_problem()
    {
        var problems = Valid(o => o.Environment = null).Problems(Ai());

        Assert.Contains(problems, p => p.Contains("ExamParsing:Environment", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_Environment_value_produces_a_problem()
    {
        var problems = Valid(o => o.Environment = "Staging").Problems(Ai());

        Assert.Contains(problems, p => p.Contains("Staging", StringComparison.Ordinal));
    }

    [Fact]
    public void Disabled_parser_does_not_require_Environment()
    {
        var options = new ExamParsingOptions { Enabled = false };
        var problems = options.Problems(Ai());

        Assert.Empty(problems);
    }

    [Fact]
    public void Production_plus_reseller_BaseUrl_produces_a_composition_problem()
    {
        var ai = new AiOptions
        {
            OpenAi = new AiProviderOptions
            {
                ApiKey = "test-key",
                Model = "model-under-test",
                BaseUrl = "https://api.vietapi.tech/v1",
                SyntheticDataOnly = false,
            },
        };

        var problems = Valid(o =>
        {
            o.Environment = "Production";
            o.ProviderRetentionNoTrainingAsserted = true;
        }).Problems(ai);

        Assert.Contains(problems, p =>
            p.Contains("vendor-direct", StringComparison.Ordinal)
            && p.Contains("api.vietapi.tech", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_plus_vendor_BaseUrl_has_no_environment_problem()
    {
        var ai = new AiOptions
        {
            OpenAi = new AiProviderOptions
            {
                ApiKey = "test-key",
                Model = "model-under-test",
                BaseUrl = "https://api.openai.com/v1",
            },
        };

        var problems = Valid(o =>
        {
            o.Environment = "Production";
            o.ProviderRetentionNoTrainingAsserted = true;
        }).Problems(ai);

        Assert.DoesNotContain(problems, p => p.Contains("vendor-direct", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_with_no_BaseUrl_is_vendor_direct()
    {
        var problems = Valid(o =>
        {
            o.Environment = "Production";
            o.ProviderRetentionNoTrainingAsserted = true;
        }).Problems(Ai());

        Assert.DoesNotContain(problems, p => p.Contains("vendor-direct", StringComparison.Ordinal));
    }

    [Fact]
    public void SyntheticTest_allows_reseller_with_synthetic_rights()
    {
        var ai = new AiOptions
        {
            OpenAi = new AiProviderOptions
            {
                ApiKey = "test-key",
                Model = "model-under-test",
                BaseUrl = "https://api.vietapi.tech/v1",
                SyntheticDataOnly = false,
            },
        };

        var problems = Valid(o =>
        {
            o.Environment = "SyntheticTest";
            o.SourceRights = "Synthetic";
        }).Problems(ai);

        Assert.DoesNotContain(problems, p => p.Contains("vendor-direct", StringComparison.Ordinal));
    }

    // ── Plan 06 production assertions ───────────────────────────────────

    [Fact]
    public void Production_without_no_training_attestation_does_not_compose()
    {
        var options = Valid(o =>
        {
            o.Environment = "Production";
            o.ProviderRetentionNoTrainingAsserted = false;
        });

        Assert.False(options.IsConfiguredFor(Ai()));
        Assert.Contains(options.Problems(Ai()), p =>
            p.Contains("ProviderRetentionNoTrainingAsserted", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_with_attestation_and_vendor_endpoint_composes()
    {
        var options = Valid(o =>
        {
            o.Environment = "Production";
            o.ProviderRetentionNoTrainingAsserted = true;
        });

        Assert.True(options.IsConfiguredFor(Ai()));
    }

    [Fact]
    public void SyntheticTest_does_not_require_no_training_attestation()
    {
        var options = Valid(o =>
        {
            o.Environment = "SyntheticTest";
            o.ProviderRetentionNoTrainingAsserted = false;
        });

        Assert.DoesNotContain(options.Problems(Ai()), p =>
            p.Contains("ProviderRetentionNoTrainingAsserted", StringComparison.Ordinal));
    }

    [Fact]
    public void MaxCostUsdPerPackage_set_still_prevents_composition()
    {
        var options = Valid(o => o.MaxCostUsdPerPackage = 5m);

        Assert.False(options.IsConfiguredFor(Ai()));
    }

    [Fact]
    public void MaxConcurrentPackages_null_is_default_and_not_a_problem()
    {
        var options = Valid();

        Assert.Null(options.MaxConcurrentPackages);
        Assert.True(options.IsConfiguredFor(Ai()));
    }
}
