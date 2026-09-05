using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
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
    public ExamPackageResult Read(string json, ExamDefinitionId definitionId, int versionNumber)
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

        var version = Convert(node.AsObject(), definitionId, versionNumber);

        // Coverage is checked here rather than by the schema, which can
        // validate shape but not completeness. An incomplete rawToBand table
        // does not fail loudly — it silently produces a wrong band for every
        // score inside the gap.
        var coverage = CheckTableCoverage(version);
        if (coverage.Count > 0)
            return ExamPackageResult.Rejected(coverage);

        var slots = CheckResponseSlots(version);
        if (slots.Count > 0)
            return ExamPackageResult.Rejected(slots);

        var invariants = CheckPackageInvariants(node.AsObject(), version);
        if (invariants.Count > 0)
            return ExamPackageResult.Rejected(invariants);

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

            var max = section.AutoScoredMarks;
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

    private static List<ValidationFinding> CheckResponseSlots(ExamVersion version)
    {
        var findings = new List<ValidationFinding>();
        foreach (var section in version.Sections)
        {
            var slots = section.Questions.SelectMany(q => q.Slots ?? []).OrderBy(s => s.Number).ToList();
            if (slots.Count == 0) continue;

            var duplicate = slots.GroupBy(s => s.Number).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                findings.Add(new ValidationFinding("error", "RESPONSE_SLOT_DUPLICATE", $"/sections/{section.Order}",
                    $"Response slot number {duplicate.Key} is used more than once."));

            var expected = Enumerable.Range(1, slots.Count).ToHashSet();
            var actual = slots.Select(s => s.Number).ToHashSet();
            if (!expected.SetEquals(actual))
                findings.Add(new ValidationFinding("error", "RESPONSE_SLOT_NON_CONTIGUOUS", $"/sections/{section.Order}",
                    "Response slot numbers must be unique and contiguous starting at 1."));

            var duplicateId = slots.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1);
            if (duplicateId is not null)
                findings.Add(new ValidationFinding("error", "RESPONSE_SLOT_DUPLICATE_ID", $"/sections/{section.Order}",
                    $"Response slot id '{duplicateId.Key}' is used more than once."));

            foreach (var question in section.Questions)
            {
                var qSlots = question.Slots ?? [];
                if (qSlots.Count != question.Marks)
                    findings.Add(new ValidationFinding("error", "RESPONSE_SLOT_COUNT_MISMATCH", $"/sections/{section.Order}/questions/{question.Order}",
                        $"Question declares {question.Marks} marks but occupies {qSlots.Count} response slots."));
                if (question.Type.IsAutoScored() && qSlots.Any(s => s.AnswerKey is null))
                    findings.Add(new ValidationFinding("error", "RESPONSE_SLOT_KEY_MISSING", $"/sections/{section.Order}/questions/{question.Order}",
                        "Every auto-scored response slot must carry an answer key."));
            }
        }
        return findings;
    }

    private static List<ValidationFinding> CheckPackageInvariants(JsonObject root, ExamVersion version)
    {
        var findings = new List<ValidationFinding>();
        CheckSequence(root, version, findings);
        CheckFormatProfile(root, version, findings);
        CheckGroups(version, findings);
        CheckAssets(root, version, findings);
        CheckAuthoredExplanations(root, version, findings);
        return findings;
    }

    private static void CheckSequence(
        JsonObject root, ExamVersion version, ICollection<ValidationFinding> findings)
    {
        if (root["sequenceProfile"]?["modules"] is not JsonArray sequence) return;
        var declared = sequence.Select(x => ParseModule(x!.GetValue<string>())).ToHashSet();
        var present = version.Sections.Select(s => s.Module).ToHashSet();
        if (!declared.SetEquals(present))
            findings.Add(new ValidationFinding(
                "error", "SEQUENCE_MODULE_MISMATCH", "/sequenceProfile/modules",
                "The sequence must name every section module exactly once and no absent module."));

        var duplicateOrder = version.Sections.GroupBy(s => s.Order).FirstOrDefault(g => g.Count() > 1);
        if (duplicateOrder is not null)
            findings.Add(new ValidationFinding(
                "error", "SECTION_ORDER_DUPLICATE", "/sections",
                $"Section order {duplicateOrder.Key} is used more than once."));
    }

    private static void CheckFormatProfile(
        JsonObject root, ExamVersion version, ICollection<ValidationFinding> findings)
    {
        var profile = root["formatProfile"]?.GetValue<string>();
        if (profile is null or "vni-practice") return;

        var expectedVariant = profile == "ielts-general-full" ? ExamVariant.General : ExamVariant.Academic;
        if (version.Variant != expectedVariant)
            findings.Add(new ValidationFinding(
                "error", "FORMAT_PROFILE_VARIANT_MISMATCH", "/variant",
                $"{profile} requires the {expectedVariant} variant."));

        var partCounts = new Dictionary<ExamModule, int>
        {
            [ExamModule.Listening] = 4,
            [ExamModule.Reading] = 3,
            [ExamModule.Writing] = 2,
            [ExamModule.Speaking] = 3,
        };
        foreach (var (module, count) in partCounts)
        {
            var section = version.Section(module);
            if (section is null || section.Parts.Count != count)
                findings.Add(new ValidationFinding(
                    "error", "FORMAT_PROFILE_PART_COUNT", "/sections",
                    $"{profile} requires {count} {module} parts."));
        }

        foreach (var module in new[] { ExamModule.Reading, ExamModule.Listening })
        {
            var count = version.Section(module)?.Questions.SelectMany(q => q.Slots ?? []).Count() ?? 0;
            if (count != 40)
                findings.Add(new ValidationFinding(
                    "error", "FORMAT_PROFILE_SLOT_COUNT", "/sections",
                    $"{profile} requires 40 {module} response slots; found {count}."));
        }
    }

    private static void CheckGroups(ExamVersion version, ICollection<ValidationFinding> findings)
    {
        foreach (var part in version.Sections.SelectMany(s => s.Parts))
        {
            foreach (var question in part.Questions)
            {
                var duplicateOption = question.Options.GroupBy(o => o.Key).FirstOrDefault(g => g.Count() > 1);
                if (duplicateOption is not null)
                    findings.Add(new ValidationFinding(
                        "error", "QUESTION_OPTION_DUPLICATE", $"/parts/{part.Order}/questions/{question.Order}/options",
                        $"Option key '{duplicateOption.Key}' is duplicated."));
            }

            foreach (var group in part.Questions.Where(q => q.Group is not null).GroupBy(q => q.Group!.Id))
            {
                var definition = group.First().Group;
                if (group.Any(q => q.Group != definition))
                    findings.Add(new ValidationFinding(
                        "error", "QUESTION_GROUP_CONFLICT", $"/parts/{part.Order}",
                        $"Question group '{group.Key}' has conflicting shared definitions."));

                var bank = group.First().Options.Select(o => (o.Key, o.Text)).ToArray();
                if (group.Any(q => !q.Options.Select(o => (o.Key, o.Text)).SequenceEqual(bank)))
                    findings.Add(new ValidationFinding(
                        "error", "QUESTION_GROUP_OPTION_MISMATCH", $"/parts/{part.Order}",
                        $"Question group '{group.Key}' must use one identical option bank."));
            }
        }
    }

    private static void CheckAssets(
        JsonObject root, ExamVersion version, ICollection<ValidationFinding> findings)
    {
        var refs = version.Sections.SelectMany(s => s.Parts).SelectMany(p =>
                new[] { p.AudioKey, p.ImageKey }.Concat(p.Questions.Select(q => q.Group?.Image)))
            .Where(path => path is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);
        var manifest = root["assetManifest"]?.AsArray()
            .Select(x => x!["path"]!.GetValue<string>()).ToList() ?? [];

        var duplicate = manifest.GroupBy(x => x, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            findings.Add(new ValidationFinding(
                "error", "ASSET_MANIFEST_DUPLICATE", "/assetManifest",
                $"Asset '{duplicate.Key}' appears more than once."));

        if (root["formatVersion"]!.GetValue<string>().StartsWith("2.", StringComparison.Ordinal))
        {
            foreach (var missing in refs.Except(manifest, StringComparer.Ordinal))
                findings.Add(new ValidationFinding(
                    "error", "ASSET_CHECKSUM_MISSING", "/assetManifest",
                    $"Referenced asset '{missing}' has no SHA-256 manifest entry."));
        }
    }

    private static void CheckAuthoredExplanations(
        JsonObject root, ExamVersion version, ICollection<ValidationFinding> findings)
    {
        var mode = root["policyProfile"]?["explanation"]?["mode"]?.GetValue<string>();
        if (mode != "authored") return;
        foreach (var question in version.Sections.SelectMany(s => s.Questions).Where(q => q.Type.IsAutoScored()))
            if (question.Explanation is null)
                findings.Add(new ValidationFinding(
                    "error", "AUTHORED_EXPLANATION_MISSING", "/policyProfile/explanation",
                    $"Question '{question.Id}' has no authored explanation evidence."));
    }

    private static ExamVersion Convert(JsonObject root, ExamDefinitionId definitionId, int versionNumber)
    {
        var variant = root["variant"]!.GetValue<string>() == "general"
            ? ExamVariant.General : ExamVariant.Academic;

        var sections = root["sections"]!.AsArray()
            .Select(s => ConvertSection(s!.AsObject()))
            .OrderBy(s => s.Order)
            .ToList();

        IReadOnlyList<ExamModule>? declaredSequence = null;
        if (root["sequenceProfile"]?["modules"] is JsonArray sequenceModules)
        {
            declaredSequence = [.. sequenceModules.Select(x => ParseModule(x!.GetValue<string>()))];
        }

        return ExamVersion.CreateDraft(
            definitionId, versionNumber, root["title"]!.GetValue<string>(), variant,
            ConvertScoring(root["scoringProfile"]!.AsObject()),
            ConvertTiming(root["timingProfile"]!.AsObject()),
            sections,
            ConvertListeningPlayback(root["policyProfile"]?["listeningPlayback"] as JsonObject),
            declaredSequence,
            /*
             * `description` has been in the schema and in every fixture since
             * the format was written, and until 2026-09-03 the reader dropped
             * it on the floor — so the sentence an author wrote to distinguish
             * one paper from another never left the JSON file.
             *
             * Whitespace-only counts as absent. A package carrying `" "` should
             * render as no description rather than as a blank line under the
             * title, and normalising it here means no consumer has to remember.
             */
            Blank(root["description"]?.GetValue<string>()));
    }

    /// <summary>Null for absent, empty or whitespace-only text.</summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ListeningPlaybackProfile ConvertListeningPlayback(JsonObject? policy)
    {
        if (policy is null) return ListeningPlaybackProfile.Conservative;

        static AudioPlaybackRule Rule(JsonNode? node)
        {
            var value = node!.AsObject();
            return new AudioPlaybackRule(
                value["playOnce"]!.GetValue<bool>(),
                value["allowSeek"]!.GetValue<bool>());
        }

        return new ListeningPlaybackProfile(Rule(policy["practice"]), Rule(policy["mock"]));
    }

    private static Section ConvertSection(JsonObject s)
    {
        var nextSlot = 1;
        var parts = new List<SectionPart>();
        foreach (var p in s["parts"]!.AsArray().OrderBy(p => p!["order"]!.GetValue<int>()))
            parts.Add(ConvertPart(p!.AsObject(), ref nextSlot));
        return new Section(ParseModule(s["module"]!.GetValue<string>()), s["order"]!.GetValue<int>(), parts);
    }

    private static SectionPart ConvertPart(JsonObject p, ref int nextSlot)
    {
        var questions = new List<Question>();
        foreach (var q in (p["questions"]?.AsArray() ?? []).OrderBy(q => q!["order"]!.GetValue<int>()))
            questions.Add(ConvertQuestion(q!.AsObject(), ref nextSlot));
        PartTiming? timing = p["timing"] is JsonObject t
            ? new PartTiming(t["durationSeconds"]!.GetValue<int>(), t["prepSeconds"]?.GetValue<int>(), t["responseSeconds"]?.GetValue<int>())
            : null;
        return new SectionPart(
            p["order"]!.GetValue<int>(), p["kind"]!.GetValue<string>(), p["title"]?.GetValue<string>(),
            p["body"]?.GetValue<string>(), p["audio"]?.GetValue<string>(), p["image"]?.GetValue<string>(),
            p["transcript"]?.GetValue<string>(), p["taskNumber"]?.GetValue<int>(), p["partNumber"]?.GetValue<int>(),
            p["cueCard"] is JsonObject cue
                ? new CueCard(cue["topic"]!.GetValue<string>(), [.. cue["bullets"]?.AsArray().Select(b => b!.GetValue<string>()) ?? []])
                : null,
            p["constraints"]?["minWords"]?.GetValue<int>(), questions, timing);
    }

    private static Question ConvertQuestion(JsonObject q, ref int nextSlot)
    {
        var type = ParseQuestionType(q["type"]!.GetValue<string>());
        var marks = q["marks"]?.GetValue<int>() ?? 1;
        var questionKey = q["answerKey"] is JsonObject key ? ConvertAnswerKey(key) : null;
        IReadOnlyList<ResponseSlot> slots;
        if (q["slots"] is JsonArray declared)
        {
            slots = [.. declared.Select(s =>
            {
                var o = s!.AsObject();
                return new ResponseSlot(o["id"]!.GetValue<string>(), o["number"]!.GetValue<int>(),
                    o["answerKey"] is JsonObject k ? ConvertAnswerKey(k) : null);
            })];
            if (slots.Count > 0) nextSlot = Math.Max(nextSlot, slots.Max(s => s.Number) + 1);
        }
        else
        {
            var generated = new List<ResponseSlot>();
            for (var i = 0; i < marks; i++)
                generated.Add(new ResponseSlot(
                    $"{q["id"]!.GetValue<string>()}-slot-{i + 1}", nextSlot++,
                    SlotAnswerKey(questionKey, i)));
            slots = generated;
        }
        QuestionExplanation? explanation = q["explanation"] is JsonObject e
            ? new QuestionExplanation(e["correctAnswer"]?.GetValue<string>(), e["shortReason"]!.GetValue<string>(),
                [.. e["evidence"]!.AsArray().Select(x => x is JsonValue v
                    ? v.GetValue<string>()
                    : x!["quote"]!.GetValue<string>())], e["commonMistake"]?.GetValue<string>())
            : null;
        return new Question(q["id"]!.GetValue<string>(), q["order"]!.GetValue<int>(), type,
            q["prompt"]?.GetValue<string>(), [.. (q["options"]?.AsArray() ?? []).Select(o => new QuestionOption(o!["key"]!.GetValue<string>(), o["text"]!.GetValue<string>()))],
            q["constraints"]?["maxWords"]?.GetValue<int>(), questionKey,
            q["group"] is JsonObject group ? ConvertGroup(group) : null, marks, slots, explanation);
    }

    private static AnswerKey? SlotAnswerKey(AnswerKey? key, int index)
    {
        if (key is null) return null;
        var accepted = key.Accepted.Select(a => a switch
        {
            { All: { } all } when index < all.Count => new AcceptedAnswer(all[index], null, null),
            { Single: { } single } => new AcceptedAnswer(single, null, null),
            { Pair: { } pair } => new AcceptedAnswer(null, null, pair),
            _ => a,
        }).ToList();
        return new AnswerKey(accepted, key.Overrides);
    }

    private static QuestionGroup ConvertGroup(JsonObject g) => new(
        g["id"]!.GetValue<string>(),
        g["title"]?.GetValue<string>(),
        g["instruction"]?.GetValue<string>(),
        g["image"]?.GetValue<string>(),
        g["text"]?.GetValue<string>(),
        g["eachLetterOnce"]?.GetValue<bool>() ?? false);

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
            s["criterionWeights"]?["writing"]?["task2"]?.GetValue<decimal>(),
            ConvertPartialCredit(s["partialCredit"] as JsonObject));
    }

    private static PartialCreditPolicy? ConvertPartialCredit(JsonObject? partialCredit)
    {
        if (partialCredit is null) return null;

        var mode = partialCredit["multiMark"]?.GetValue<string>()
            ?? throw new InvalidOperationException("partialCredit.multiMark is required.");

        return mode switch
        {
            "all-or-nothing" => new PartialCreditPolicy(MultiMarkPartialCredit.AllOrNothing),
            _ => throw new InvalidOperationException(
                $"partialCredit.multiMark '{mode}' is not implemented."),
        };
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
