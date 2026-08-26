using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content;

/// <summary>
/// Turns exam JSON into a domain <see cref="ExamVersion"/>, and refuses
/// anything the schema rejects.
///
/// <para>
/// <b>This is the single gate, and it exists for a specific reason.</b>
/// [ADR-0012] moved the CMS behind the learner surface, which removed the ZIP
/// importer as the thing that exercises the package format early. The
/// mitigation is that content still arrives through <c>exam.schema.json</c> —
/// so the seeder, the future importer, and future in-place authoring are three
/// producers of one write model through one validator.
/// </para>
///
/// <para>
/// Loading ad-hoc JSON shaped for whatever renders conveniently would
/// reintroduce exactly the drift that ordering was meant to prevent, and the
/// drift would surface at the first real import — after the engine had
/// hardened around the wrong shape.
/// </para>
/// </summary>
public sealed class ExamPackageReader(JsonSchema schema)
{
    /// <summary>
    /// One built schema per file, per process.
    ///
    /// <b>Not an optimisation — a correctness fix.</b> <c>JsonSchema.FromFile</c>
    /// registers the document by its <c>$id</c> in a process-global registry,
    /// and a second registration of the same id throws
    /// <i>"Overwriting registered schemas is not permitted"</i>. That is
    /// invisible while one host builds the schema once, and it took out five
    /// integration tests the moment a second <c>WebApplicationFactory</c>
    /// started in the same process.
    ///
    /// Caching also stops a multi-megabyte schema being parsed on every boot,
    /// but that is the smaller half.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Lazy<JsonSchema>> SchemaCache = new();

    public static ExamPackageReader FromSchemaFile(string schemaPath) =>
        new(SchemaCache.GetOrAdd(
            Path.GetFullPath(schemaPath),
            // Lazy, not a bare factory. `GetOrAdd` does not promise to invoke
            // its factory only once — two callers racing on a cold key both
            // run it, and the loser's `JsonSchema.FromFile` is the second
            // registration that throws. Which is exactly what happened: xUnit
            // starts test classes in parallel, so two hosts booted together.
            path => new Lazy<JsonSchema>(
                () => JsonSchema.FromFile(path), LazyThreadSafetyMode.ExecutionAndPublication))
            .Value);

    /// <summary>
    /// Validates and converts. Findings carry a JSON Pointer path so an author
    /// gets an addressable list of what to fix rather than a stack trace.
    /// </summary>
    public ExamPackageResult Read(
        string json, ExamDefinitionId definitionId, int versionNumber, UserId createdBy)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException e)
        {
            return ExamPackageResult.Rejected(
                [new ValidationFinding("error", "MALFORMED_JSON", "/", e.Message)]);
        }

        if (node is null)
            return ExamPackageResult.Rejected(
                [new ValidationFinding("error", "MALFORMED_JSON", "/", "The document is empty.")]);

        using var document = JsonDocument.Parse(node.ToJsonString());
        var evaluation = schema.Evaluate(document.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        });

        if (!evaluation.IsValid)
            return ExamPackageResult.Rejected(Collect(evaluation));

        var version = Convert(node.AsObject(), definitionId, versionNumber, createdBy);

        // Coverage is checked here rather than by the schema, which can
        // validate shape but not completeness. An incomplete rawToBand table
        // does not fail loudly — it silently produces a wrong band for every
        // score inside the gap.
        var coverage = CheckTableCoverage(version);
        if (coverage.Count > 0)
            return ExamPackageResult.Rejected(coverage);

        return ExamPackageResult.Accepted(version);
    }

    private static List<ValidationFinding> Collect(EvaluationResults results)
    {
        var findings = new List<ValidationFinding>();

        void Walk(EvaluationResults node)
        {
            if (!node.IsValid && node.Errors is { Count: > 0 } errors)
            {
                foreach (var (keyword, message) in errors)
                {
                    findings.Add(new ValidationFinding(
                        "error", "SCHEMA_INVALID", node.InstanceLocation.ToString(),
                        $"{keyword}: {message}"));
                }
            }

            foreach (var child in node.Details ?? [])
                Walk(child);
        }

        Walk(results);

        return findings.Count > 0
            ? findings
            : [new ValidationFinding("error", "SCHEMA_INVALID", "/", "Document does not match the schema.")];
    }

    private static List<ValidationFinding> CheckTableCoverage(ExamVersion version)
    {
        var findings = new List<ValidationFinding>();

        foreach (var section in version.Sections)
        {
            if (section.Module is not (ExamModule.Reading or ExamModule.Listening))
                continue;

            var max = section.AutoScoredCount;
            if (!version.Scoring.CoversRange(section.Module, max, out var firstUncovered))
            {
                findings.Add(new ValidationFinding(
                    "error", "SCORING_TABLE_INCOMPLETE",
                    $"/scoringProfile/rawToBand/{section.Module.ToString().ToLowerInvariant()}",
                    $"The table does not cover a raw score of {firstUncovered} out of {max}. "
                    + "An uncovered score silently produces a wrong band."));
            }
        }

        return findings;
    }

    private static ExamVersion Convert(
        JsonObject root, ExamDefinitionId definitionId, int versionNumber, UserId createdBy)
    {
        var variant = root["variant"]!.GetValue<string>() == "general"
            ? ExamVariant.General : ExamVariant.Academic;

        var sections = root["sections"]!.AsArray()
            .Select(s => ConvertSection(s!.AsObject()))
            .OrderBy(s => s.Order)
            .ToList();

        return ExamVersion.CreateDraft(
            definitionId, versionNumber, root["title"]!.GetValue<string>(), variant, createdBy,
            ConvertScoring(root["scoringProfile"]!.AsObject()),
            ConvertTiming(root["timingProfile"]!.AsObject()),
            sections);
    }

    private static Section ConvertSection(JsonObject s) => new(
        ParseModule(s["module"]!.GetValue<string>()),
        s["order"]!.GetValue<int>(),
        [.. s["parts"]!.AsArray().Select(p => ConvertPart(p!.AsObject())).OrderBy(p => p.Order)]);

    private static SectionPart ConvertPart(JsonObject p) => new(
        p["order"]!.GetValue<int>(),
        p["kind"]!.GetValue<string>(),
        p["title"]?.GetValue<string>(),
        p["body"]?.GetValue<string>(),
        p["audio"]?.GetValue<string>(),
        p["image"]?.GetValue<string>(),
        p["transcript"]?.GetValue<string>(),
        p["taskNumber"]?.GetValue<int>(),
        p["partNumber"]?.GetValue<int>(),
        p["cueCard"] is JsonObject cue
            ? new CueCard(
                cue["topic"]!.GetValue<string>(),
                [.. cue["bullets"]?.AsArray().Select(b => b!.GetValue<string>()) ?? []])
            : null,
        p["constraints"]?["minWords"]?.GetValue<int>(),
        [.. (p["questions"]?.AsArray() ?? []).Select(q => ConvertQuestion(q!.AsObject())).OrderBy(q => q.Order)]);

    private static Question ConvertQuestion(JsonObject q) => new(
        q["id"]!.GetValue<string>(),
        q["order"]!.GetValue<int>(),
        ParseQuestionType(q["type"]!.GetValue<string>()),
        q["prompt"]?.GetValue<string>(),
        [.. (q["options"]?.AsArray() ?? []).Select(o =>
            new QuestionOption(o!["key"]!.GetValue<string>(), o["text"]!.GetValue<string>()))],
        q["constraints"]?["maxWords"]?.GetValue<int>(),
        q["answerKey"] is JsonObject key ? ConvertAnswerKey(key) : null);

    private static AnswerKey ConvertAnswerKey(JsonObject key) => new(
        [.. key["accepted"]!.AsArray().Select(a => a switch
        {
            JsonValue v => new AcceptedAnswer(v.GetValue<string>(), null, null),
            JsonArray arr => new AcceptedAnswer(null, [.. arr.Select(x => x!.GetValue<string>())], null),
            JsonObject pair => new AcceptedAnswer(null, null,
                (pair["left"]!.GetValue<string>(), pair["right"]!.GetValue<string>())),
            _ => throw new InvalidOperationException("Unreachable — the schema constrains this shape."),
        })],
        key["matchingOverrides"] is JsonObject o ? ConvertMatching(o) : null);

    private static AnswerMatchingRules ConvertMatching(JsonObject m) => new(
        m["caseSensitive"]?.GetValue<bool>() ?? false,
        m["trimWhitespace"]?.GetValue<bool>() ?? true,
        m["collapseInnerWhitespace"]?.GetValue<bool>() ?? true,
        m["allowSpellingVariants"]?.GetValue<bool>() ?? false,
        m["numericEquivalence"]?.GetValue<bool>() ?? true);

    private static ScoringProfile ConvertScoring(JsonObject s)
    {
        var tables = new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>();

        foreach (var (name, table) in s["rawToBand"]!.AsObject())
        {
            tables[ParseModule(name)] =
            [
                .. table!.AsArray().Select(b => new BandBoundary(
                    b!["minRaw"]!.GetValue<int>(),
                    BandScore.Create(b["band"]!.GetValue<decimal>()))),
            ];
        }

        return new ScoringProfile(
            tables,
            s["answerMatching"] is JsonObject m ? ConvertMatching(m) : AnswerMatchingRules.Default,
            // No `?? 1m` / `?? 2m` here. A package that omits the weighting
            // carries no weighting; substituting the documented assumption at
            // the boundary is how an open question becomes an answer nobody
            // decided. ScoringProfile refuses at the point of use instead.
            s["criterionWeights"]?["writing"]?["task1"]?.GetValue<decimal>(),
            s["criterionWeights"]?["writing"]?["task2"]?.GetValue<decimal>());
    }

    private static TimingProfile ConvertTiming(JsonObject t)
    {
        var durations = new Dictionary<ExamModule, int>();
        var speaking = new List<SpeakingPartTiming>();
        int? transfer = null;

        foreach (var (name, cfg) in t["sections"]!.AsObject())
        {
            var module = ParseModule(name);

            if (module == ExamModule.Speaking)
            {
                speaking.AddRange(cfg!["parts"]!.AsArray().Select(p => new SpeakingPartTiming(
                    p!["part"]!.GetValue<int>(),
                    p["prepSeconds"]?.GetValue<int>() ?? 0,
                    p["responseSeconds"]!.GetValue<int>())));
                durations[module] = speaking.Sum(p => p.PrepSeconds + p.ResponseSeconds);
                continue;
            }

            durations[module] = cfg!["durationSeconds"]!.GetValue<int>();
            if (module == ExamModule.Listening)
                transfer = cfg["transferTimeSeconds"]?.GetValue<int>();
        }

        return new TimingProfile(durations, transfer, speaking);
    }

    private static ExamModule ParseModule(string value) => value switch
    {
        "reading" => ExamModule.Reading,
        "listening" => ExamModule.Listening,
        "writing" => ExamModule.Writing,
        "speaking" => ExamModule.Speaking,
        _ => throw new InvalidOperationException($"Unknown module '{value}'."),
    };

    private static QuestionType ParseQuestionType(string value) => value switch
    {
        "multiple-choice" => QuestionType.MultipleChoice,
        "multiple-select" => QuestionType.MultipleSelect,
        "true-false-notgiven" => QuestionType.TrueFalseNotGiven,
        "yes-no-notgiven" => QuestionType.YesNoNotGiven,
        "matching" => QuestionType.Matching,
        "completion" => QuestionType.Completion,
        "short-answer" => QuestionType.ShortAnswer,
        "labelling" => QuestionType.Labelling,
        "essay-task" => QuestionType.EssayTask,
        "speaking-response" => QuestionType.SpeakingResponse,
        _ => throw new InvalidOperationException($"Unknown question type '{value}'."),
    };
}

/// <summary>
/// A first-class entity rather than a log line: an administrator whose
/// 200-question package failed needs an addressable list of what to fix.
/// </summary>
public sealed record ValidationFinding(string Severity, string Code, string Path, string Message);

public sealed record ExamPackageResult(
    bool IsValid, ExamVersion? Version, IReadOnlyList<ValidationFinding> Findings)
{
    public static ExamPackageResult Accepted(ExamVersion version) => new(true, version, []);
    public static ExamPackageResult Rejected(IReadOnlyList<ValidationFinding> findings) =>
        new(false, null, findings);
}
