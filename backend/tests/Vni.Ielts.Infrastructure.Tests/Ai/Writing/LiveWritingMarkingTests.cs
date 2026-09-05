using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Assessment;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Writing;
using Vni.Ielts.Infrastructure.Assessment;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Writing;

/// <summary>
/// Marks a real essay with the real provider and checks what a learner would be
/// shown.
///
/// ── Why this exists, given everything else that is already tested ─────────
///
/// <b>Nothing proved a band ever reached a results screen.</b> Every other test
/// on this path uses a fake <c>HttpMessageHandler</c> or a recorded fixture, so
/// between them they prove the adapter builds the right request and the
/// validator refuses the wrong response — and say nothing about whether the two
/// halves meet. The one integration test that touches the results payload
/// asserts <c>markings</c> is <b>empty</b>, which it was, correctly, for as long
/// as no evaluator was wired.
///
/// So on 2026-09-02, the day learner essays started flowing, the honest answer
/// to "does a band reach the learner" was: the code path is complete and nobody
/// has ever watched it run. This is the test that watches it.
///
/// ── What it asserts, and why each one ─────────────────────────────────────
///
/// It does not assert a band <i>value</i>. Two runs of the same essay may
/// differ by half a step and that is a property of the thing being measured,
/// not a defect. What must hold every time is the <b>contract</b>:
///
/// <list type="bullet">
/// <item>the criterion set is exactly the rubric's four — not three, not five,
/// not renamed;</item>
/// <item>every band sits on the half-step grid, because a 6.3 is not an IELTS
/// band and cannot be reported as one;</item>
/// <item>every criterion carries evidence, and the evidence is quoted from the
/// learner's own words — <c>A-13c</c>, and the difference between a mark
/// somebody can learn from and a number they can only accept;</item>
/// <item>the section band is the one <c>CriterionMarking.Mark</c> recomputed,
/// never the one the model reported.</item>
/// </list>
///
/// ── Opt-in, because it spends money and crosses a border ──────────────────
///
/// It calls a paid endpoint and sends text abroad, so it runs only when someone
/// asks for it:
///
/// <code>VNI_LIVE_AI=1 dotnet test --filter LiveWritingMarkingTests</code>
///
/// The essay is a fixture nobody wrote for real, so nothing here is a learner's
/// personal data — but the call is authorised as
/// <see cref="AiDataClassification.LearnerPersonal"/> anyway, on purpose. The
/// point is to exercise the gates the production path passes through; running
/// it as synthetic would test a route no learner takes.
/// </summary>
public sealed class LiveWritingMarkingTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    /*
     * Held for the lifetime of the test, not the lifetime of the builder.
     * `IHttpClientFactory` creates a DI scope per handler, lazily, on the
     * first `CreateClient` — so a provider disposed when the setup method
     * returned took the factory down with it and the failure surfaced
     * inside the adapter, three layers away from the mistake.
     */
    private ServiceProvider? _provider;

    private const string OptIn = "VNI_LIVE_AI";

    private const string SkipReason =
        "Live provider test. Set VNI_LIVE_AI=1 and configure Ai:OpenAi in "
        + "backend/src/Vni.Ielts.Api/secrets.develop.json to run it. It spends money.";

    [SkippableFact]
    public async Task A_real_essay_comes_back_as_a_band_a_learner_could_be_shown()
    {
        Skip.If(Environment.GetEnvironmentVariable(OptIn) != "1", SkipReason);

        var (ai, assessment) = LoadConfiguration();

        Skip.IfNot(
            WritingSectionEvaluator.IsConfiguredFor(assessment, ai),
            "Writing marking is not configured, or a gate refuses it. " + SkipReason);

        var evaluator = BuildEvaluator(ai, assessment);
        var rubric = new ConfiguredRubricSource(Options.Create(assessment)).For(ExamModule.Writing);

        Assert.NotNull(rubric);

        var essay = await File.ReadAllTextAsync(FixturePath(
            "fixtures", "ai", "writing", "essays", "task2-opinion-band6.txt"));

        const string prompt =
            "Some people believe that governments should spend more money on public transport "
            + "than on building new roads. Others think the opposite. Discuss both views and "
            + "give your own opinion.";

        var claim = await evaluator.EvaluateAsync(
            new EvaluationRequest(rubric, essay, prompt), CancellationToken.None);

        // ── The claim, before anything trusts it ──────────────────────────
        Assert.Equal(
            rubric.Criteria.OrderBy(c => c, StringComparer.Ordinal),
            claim.Criteria.Select(c => c.Criterion).OrderBy(c => c, StringComparer.Ordinal));

        // ── And what the claim becomes ────────────────────────────────────
        var marking = CriterionMarking.Mark(
            rubric, claim.Criteria, claim.ReportedBand, essay, taskNumber: 2);

        Assert.Equal(rubric.Version, marking.RubricVersion);
        Assert.Equal(4, marking.Criteria.Count);

        Assert.True(
            marking.Band.Value % 0.5m == 0,
            $"Section band {marking.Band.Value} is not on the half-step grid, so it is not an "
            + "IELTS band and must not be reported as one.");

        var normalisedEssay = Normalise(essay);

        foreach (var criterion in marking.Criteria)
        {
            Assert.True(
                criterion.Band.Value % 0.5m == 0,
                $"{criterion.Criterion} band {criterion.Band.Value} is off the half-step grid.");

            Assert.False(
                string.IsNullOrWhiteSpace(criterion.Feedback),
                $"{criterion.Criterion} came back with no feedback, so the results screen would "
                + "show a number with nothing under it.");

            Assert.NotEmpty(criterion.Evidence);

            foreach (var quote in criterion.Evidence)
            {
                Assert.Contains(
                    Normalise(quote),
                    normalisedEssay,
                    StringComparison.Ordinal);
            }
        }

        /*
         * <b>Printed, because this is the artefact the test exists to produce.</b>
         * A green tick says the contract held; it does not let anybody look at
         * the band and judge whether the marking is any good. That judgement is
         * calibration (`M-28`) and it needs eyes on real output.
         */
        output.WriteLine(
            $"section band {marking.Band.Value} under rubric {marking.RubricVersion}");

        foreach (var criterion in marking.Criteria)
        {
            output.WriteLine(
                $"  {criterion.Criterion,-30} {criterion.Band.Value}  "
                + $"evidence: \"{criterion.Evidence[0]}\"");
        }
    }

    public void Dispose() => _provider?.Dispose();

    private WritingSectionEvaluator BuildEvaluator(AiOptions ai, AssessmentOptions assessment)
    {
        var services = new ServiceCollection();

        /*
         * Three minutes, not the default hundred seconds. A whole essay against
         * four criteria with quoted evidence is a long generation, and the
         * exam importer already learned this the expensive way: headers arrive
         * fast, the body streams, and `SendAsync` does not return until it is
         * finished.
         */
        services.AddHttpClient(nameof(OpenAiWritingEvaluationClient))
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(3));

        services.AddHttpClient(nameof(GeminiWritingEvaluationClient));

        _provider = services.BuildServiceProvider();
        var factory = _provider.GetRequiredService<IHttpClientFactory>();

        var aiOptions = Options.Create(ai);
        var assessmentOptions = Options.Create(assessment);

        var clients = new IWritingEvaluationClient[]
        {
            new OpenAiWritingEvaluationClient(
                factory, NullLogger<OpenAiWritingEvaluationClient>.Instance),
            new GeminiWritingEvaluationClient(
                factory, NullLogger<GeminiWritingEvaluationClient>.Instance),
        };

        var router = new WritingEvaluationRouter(
            clients,
            assessmentOptions,
            aiOptions,
            new NullWritingEvaluationCostMetric(),
            NullLogger<WritingEvaluationRouter>.Instance);

        return new WritingSectionEvaluator(
            aiOptions, assessmentOptions, router, NullLogger<WritingSectionEvaluator>.Instance);
    }

    private static (AiOptions Ai, AssessmentOptions Assessment) LoadConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(
                FixturePath("backend", "src", "Vni.Ielts.Api", "secrets.develop.json"),
                optional: true,
                reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        return (
            configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions(),
            configuration.GetSection(AssessmentOptions.SectionName).Get<AssessmentOptions>()
                ?? new AssessmentOptions());
    }

    /// <summary>Resolves a repository-relative path by walking up from the test binary.</summary>
    private static string FixturePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "contracts", "schemas")))
                return Path.Combine([directory.FullName, .. segments]);

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test binary.");
    }

    /// <summary>
    /// Whitespace-insensitive containment, matching how
    /// <c>CriterionMarking</c> itself compares a quotation to the submission.
    /// A model that re-wraps a line has not invented a quotation.
    /// </summary>
    private static string Normalise(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
