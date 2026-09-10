# Answer-key cross-check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An uploaded exam package keeps its paper and its answer key apart, the key is read by code and never by the model, and a key that disagrees with its paper is reported per question and cannot be approved.

**Architecture:** The ZIP layout gains a second folder level that says whether a file is the paper or the key. The import pipeline sends only the paper to the model, parses the key with existing deterministic code, and writes it on. Two new checkers then compare the finished package against itself: one reads the rules the paper states about its own questions, the other anchors each answer in the passage it must have come from. Their errors become blocking findings that the approval gate refuses.

**Tech Stack:** .NET 10, xUnit, `System.Text.Json.Nodes` for package manipulation. No new dependency.

**Spec:** [`../specs/2026-09-10-import-time-exam-preparation-design.md`](../specs/2026-09-10-import-time-exam-preparation-design.md) — Part A, sections A1, A2, A3 layers 1 to 4, and the blocking-findings gate.

## Global Constraints

- **Domain entities carry no persistence attributes**, and **no AI provider type may appear in the Domain layer**. New checking code lives in `Vni.Ielts.Application.Importing`, which already holds `AnswerKeyInjection` and `FabricatedAnswerKeyGuard`. → CLAUDE.md rules 5 and 7
- **Reading and Listening band scores come from the answer key, never from a model.** Nothing in this plan lets a model write an answer. → CLAUDE.md rule 9
- **Uploaded ZIP packages are untrusted input.** `ExamPackageArchiveInspector` is a security boundary. Task 1 adds a read *after* the existing per-entry checks, on the already-canonicalised `verdict.Path`, and modifies no existing check. → CLAUDE.md rule 3
- **Every item closes with a test verified to go red when the fix is removed.** A green suite is not evidence. Each task's final step records that verification.
- **Finding severity strings are the existing lowercase literals** `"error"` and `"warning"`, as used throughout `PackageFinding`.
- **Never log an answer key, a passage, or learner text.** Findings name a question number and a path, never the answer that failed.
- Run the whole backend suite with `dotnet test backend/Vni.Ielts.sln --nologo`. Three integration tests fail on a machine without MinIO and one (`SecretContractTests.The_speaking_retention_seams_are_unset`) is stale against committed configuration; **these three pre-date this work and are not yours to fix.**

---

### Task 1: The package layout says paper or key

**Files:**
- Modify: `backend/src/Vni.Ielts.Application/Importing/ImportArchivePorts.cs:70-90` (`PackageLayout`)
- Modify: `backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageArchiveInspector.cs:73-78` (folder table) and `:189-198` (classification)
- Modify: `backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs:113-115` (call site, compile fix only)
- Test: `backend/tests/Vni.Ielts.Infrastructure.Tests/Content/Import/ExamPackageArchiveInspectorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `PackageEntryRole`, `SkillEntries(IReadOnlyList<string> Paper, IReadOnlyList<string> Key, IReadOnlyList<string> Audio)` with `IEnumerable<string> All`, and `PackageLayout.EntriesBySkill` retyped to `IReadOnlyDictionary<ExamModule, SkillEntries>`. Task 3 reads `.Paper` and `.Key`.

- [ ] **Step 1: Write the failing test**

Add to `ExamPackageArchiveInspectorTests.cs`:

```csharp
[Fact]
public async Task A_role_folder_separates_the_paper_from_its_answer_key()
{
    var archive = Build(
        File("reading/de/passage.txt"),
        File("reading/dap-an/key.txt"));

    var result = await inspector.InspectAsync(archive, Tight, default);

    Assert.True(result.IsAcceptable, Describe(result));
    var reading = result.Layout.EntriesBySkill[ExamModule.Reading];
    Assert.Equal("reading/de/passage.txt", Assert.Single(reading.Paper));
    Assert.Equal("reading/dap-an/key.txt", Assert.Single(reading.Key));
}

[Theory]
[InlineData("paper", "key")]
[InlineData("QUESTIONS", "ANSWERS")]
[InlineData("de", "dapan")]
public async Task Role_folder_names_are_accepted_in_every_spelling_and_case(string paper, string key)
{
    var archive = Build(File($"listening/{paper}/q.txt"), File($"listening/{key}/k.txt"));

    var result = await inspector.InspectAsync(archive, Tight, default);

    var listening = result.Layout.EntriesBySkill[ExamModule.Listening];
    Assert.Single(listening.Paper);
    Assert.Single(listening.Key);
}

/// <summary>
/// Packages imported before role folders existed put their files straight
/// under the skill folder. Those are papers, and they must keep working —
/// otherwise this change silently empties every historical package.
/// </summary>
[Fact]
public async Task A_file_with_no_role_folder_is_still_a_paper()
{
    var result = await inspector.InspectAsync(Build(File("reading/passage.txt")), Tight, default);

    var reading = result.Layout.EntriesBySkill[ExamModule.Reading];
    Assert.Equal("reading/passage.txt", Assert.Single(reading.Paper));
    Assert.Empty(reading.Key);
}

/// <summary>
/// The role is read from the canonicalised path, after every traversal check
/// has already run. A package that tries to reach a role folder by climbing
/// must still be refused as traversal, not quietly filed as a key.
/// </summary>
[Fact]
public async Task A_traversal_attempt_through_a_role_folder_is_still_refused()
{
    var archive = Build(File("reading/de/../../../dap-an/evil.txt"));

    var result = await inspector.InspectAsync(archive, Tight, default);

    AssertRefused(result, ArchiveFindingCodes.PathEscape);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Vni.Ielts.Infrastructure.Tests --nologo --filter "FullyQualifiedName~ExamPackageArchiveInspectorTests"`
Expected: FAIL to compile — `SkillEntries` does not exist and `EntriesBySkill` returns `IReadOnlyList<string>`, which has no `.Paper`.

- [ ] **Step 3: Add the role types**

In `ImportArchivePorts.cs`, above `PackageLayout`:

```csharp
/// <summary>
/// What an accepted file is <i>for</i>, decided by the folder directly under
/// the skill folder (<c>IP-02</c>).
///
/// <b>This is what keeps the answer key away from the model.</b> Before it
/// existed, every file under a skill folder was concatenated into one blob
/// and sent to the parser, so a key dropped into <c>reading/</c> was read by
/// it — the configuration measured wrong on 2026-09-02, where a model shown
/// a paper produced forty answers for a paper that contained none.
/// </summary>
public enum PackageEntryRole
{
    /// <summary>Passages, questions, task prompts. The only role the model sees.</summary>
    Paper,

    /// <summary>The supplier's answer key. Read by code, never by a model.</summary>
    Key,

    /// <summary>Listening recordings.</summary>
    Audio,
}

/// <summary>
/// One skill's accepted files, split by role. Order within each list is the
/// archive's own order, which is what an administrator reading a report sees.
/// </summary>
public sealed record SkillEntries(
    IReadOnlyList<string> Paper,
    IReadOnlyList<string> Key,
    IReadOnlyList<string> Audio)
{
    public static SkillEntries Empty { get; } = new([], [], []);

    /// <summary>Every accepted file for this skill, whatever its role.</summary>
    public IEnumerable<string> All => Paper.Concat(Key).Concat(Audio);

    public int Count => Paper.Count + Key.Count + Audio.Count;
}
```

Retype `PackageLayout`:

```csharp
public sealed record PackageLayout(
    IReadOnlyDictionary<ExamModule, SkillEntries> EntriesBySkill,
    IReadOnlyList<string> UnknownEntries)
{
    public static PackageLayout Empty { get; } =
        new(new Dictionary<ExamModule, SkillEntries>(), []);

    /// <summary>Skills with at least one file. Order follows <see cref="ExamModule"/>.</summary>
    public IReadOnlyList<ExamModule> PresentSkills =>
        Enum.GetValues<ExamModule>()
            .Where(m => EntriesBySkill.TryGetValue(m, out var e) && e.Count > 0)
            .ToArray();

    /// <summary>Every relative path that would be extracted, across all skills and roles.</summary>
    public IEnumerable<string> AcceptedEntries => EntriesBySkill.Values.SelectMany(e => e.All);

    public SkillEntries For(ExamModule module) =>
        EntriesBySkill.TryGetValue(module, out var e) ? e : SkillEntries.Empty;
}
```

- [ ] **Step 4: Classify the role in the inspector**

In `ExamPackageArchiveInspector.cs`, beside the existing `SkillFolders` table:

```csharp
/// <summary>
/// The folder directly under a skill folder. Unaccented spellings only: a ZIP
/// stores entry names as CP437 or UTF-8 depending on a per-entry flag that
/// many Windows tools set wrongly, so an accented name arrives mangled often
/// enough that matching on it would fail unpredictably. The downloadable
/// skeleton ships the correct names so nobody has to type one.
/// </summary>
private static readonly Dictionary<string, PackageEntryRole> RoleFolders =
    new(StringComparer.OrdinalIgnoreCase)
    {
        ["de"] = PackageEntryRole.Paper,
        ["paper"] = PackageEntryRole.Paper,
        ["questions"] = PackageEntryRole.Paper,
        ["dap-an"] = PackageEntryRole.Key,
        ["dapan"] = PackageEntryRole.Key,
        ["key"] = PackageEntryRole.Key,
        ["answers"] = PackageEntryRole.Key,
        ["audio"] = PackageEntryRole.Audio,
    };
```

Change the accumulator declaration from `Dictionary<ExamModule, List<string>> bySkill` to:

```csharp
var bySkill = new Dictionary<ExamModule, (List<string> Paper, List<string> Key, List<string> Audio)>();
```

Replace the classification arm (currently `if (!bySkill.TryGetValue(module, out var list)) …; list.Add(verdict.Path);`) with:

```csharp
if (segments.Length >= 2 && SkillFolders.TryGetValue(segments[0], out var module))
{
    if (!bySkill.TryGetValue(module, out var lists))
        bySkill[module] = lists = ([], [], []);

    /*
     * `verdict.Path` has already been through Examine: traversal, absolute
     * prefixes, reserved names, null bytes and non-regular entries are all
     * gone. Reading segments[1] here is a read of a cleared value. A role
     * must never be taken from a raw entry name — "../" is a legal substring
     * of a folder label, and the check that catches it runs above, not here.
     *
     * A file with no role folder (segments.Length == 2) is a paper, which is
     * what every package imported before IP-02 looks like.
     */
    var role = segments.Length >= 3 && RoleFolders.TryGetValue(segments[1], out var found)
        ? found
        : PackageEntryRole.Paper;

    switch (role)
    {
        case PackageEntryRole.Key: lists.Key.Add(verdict.Path); break;
        case PackageEntryRole.Audio: lists.Audio.Add(verdict.Path); break;
        default: lists.Paper.Add(verdict.Path); break;
    }
}
```

And the layout construction:

```csharp
var layout = new PackageLayout(
    bySkill.ToDictionary(
        p => p.Key,
        p => new SkillEntries(
            p.Value.Paper.AsReadOnly(),
            p.Value.Key.AsReadOnly(),
            p.Value.Audio.AsReadOnly())),
    unknown.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
```

- [ ] **Step 5: Fix the two existing call sites**

In `ExamPackageImportPipeline.cs`, the loop `foreach (var relativePath in layout.EntriesBySkill[skill])` becomes `foreach (var relativePath in layout.EntriesBySkill[skill].All)`. This is a compile fix that preserves today's behaviour exactly; Task 3 is what changes it.

In `ExamPackageArchiveInspectorTests.cs`, the existing assertion `single.Layout.EntriesBySkill[ExamModule.Reading].Single()` becomes `single.Layout.EntriesBySkill[ExamModule.Reading].All.Single()`. **This is a type change, not a weakened assertion** — it still asserts exactly one accepted entry with exactly that name.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/tests/Vni.Ielts.Infrastructure.Tests --nologo --filter "FullyQualifiedName~ExamPackageArchiveInspectorTests"`
Expected: PASS, including every pre-existing hostile-archive test.

- [ ] **Step 7: Verify the new tests go red when the fix is removed**

Temporarily replace the role expression with `var role = PackageEntryRole.Paper;`, re-run, and confirm `A_role_folder_separates_the_paper_from_its_answer_key` fails on an empty `Key`. Restore it. Record the observation in the task report.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Vni.Ielts.Application/Importing/ImportArchivePorts.cs \
        backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageArchiveInspector.cs \
        backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs \
        backend/tests/Vni.Ielts.Infrastructure.Tests/Content/Import/ExamPackageArchiveInspectorTests.cs
git commit -m "feat(import): the package layout says whether a file is the paper or its key"
```

---

### Task 2: An answer key applies to one skill

**Files:**
- Modify: `backend/src/Vni.Ielts.Application/Importing/AnswerKeyInjection.cs` (`Apply` signature and the question enumeration)
- Test: `backend/tests/Vni.Ielts.Application.Tests/Importing/AnswerKeyTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `AnswerKeyInjection.Apply(string packageJson, IReadOnlyList<AnswerKeyEntry> entries, ExamModule? module = null)`. Task 3 passes a module.

**Why this task exists.** Reading questions are numbered 1 to 40 and Listening questions are numbered 1 to 40. `Apply` matches an `AnswerKeyEntry` to a question by number across the whole package, so a package holding both skills would have every Reading answer collide with a Listening question. The command-line importer never hit this because `--keep-module` runs one skill at a time; the HTTP door does not have that luxury.

- [ ] **Step 1: Write the failing test**

Add to `AnswerKeyTests.cs`:

```csharp
/// <summary>
/// Reading 1-40 and Listening 1-40 are different questions with the same
/// numbers. Without a module filter the first key written wins both.
/// </summary>
[Fact]
public void A_key_applies_only_to_the_skill_it_belongs_to()
{
    var package = TwoSkillPackage();
    var readingKey = new[] { new AnswerKeyEntry(1, 1, "TRUE") };

    var result = AnswerKeyInjection.Apply(package, readingKey, ExamModule.Reading);

    Assert.True(result.IsSuccess, Describe(result.Findings));
    var doc = JsonNode.Parse(result.PackageJson)!.AsObject();
    Assert.Equal("TRUE", AnswerAt(doc, ExamModule.Reading, order: 1));
    Assert.Null(AnswerAt(doc, ExamModule.Listening, order: 1));
}

/// <summary>
/// Coverage is counted inside the filtered skill. A Reading key that covers
/// Reading completely must not be reported short because Listening exists.
/// </summary>
[Fact]
public void Coverage_is_counted_within_the_filtered_skill_only()
{
    var package = TwoSkillPackage();
    var readingKey = new[] { new AnswerKeyEntry(1, 1, "TRUE") };

    var result = AnswerKeyInjection.Apply(package, readingKey, ExamModule.Reading);

    Assert.DoesNotContain(
        result.Findings, f => f.Code == AnswerKeyInjection.CoverageCode);
}
```

Add these helpers to the same file:

```csharp
/// <summary>One Reading and one Listening question, both numbered 1.</summary>
private static string TwoSkillPackage() =>
    """
    {
      "formatVersion": "2.0",
      "sections": [
        { "module": "reading", "order": 1, "parts": [ { "order": 1, "kind": "passage",
          "questions": [ { "id": "r1", "order": 1, "type": "true-false-notgiven" } ] } ] },
        { "module": "listening", "order": 2, "parts": [ { "order": 1, "kind": "recording",
          "questions": [ { "id": "l1", "order": 1, "type": "true-false-notgiven" } ] } ] }
      ]
    }
    """;

private static string? AnswerAt(JsonObject package, ExamModule module, int order)
{
    var wanted = module.ToString().ToLowerInvariant();
    foreach (var section in package["sections"]!.AsArray())
    {
        if (section!["module"]!.GetValue<string>() != wanted) continue;
        foreach (var part in section["parts"]!.AsArray())
        foreach (var question in part!["questions"]!.AsArray())
        {
            if (question!["order"]!.GetValue<int>() != order) continue;
            return question["answerKey"]?["accepted"]?.AsArray()[0]?.GetValue<string>();
        }
    }

    return null;
}

private static string Describe(IReadOnlyList<PackageFinding> findings) =>
    string.Join("\n", findings.Select(f => $"{f.Severity} {f.Code} {f.Path}"));
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~AnswerKeyTests"`
Expected: FAIL to compile — `Apply` takes two arguments.

- [ ] **Step 3: Add the filter**

Change the signature and thread the filter through the enumeration:

```csharp
/// <param name="module">
/// When given, only questions in sections of this module are keyed, and
/// coverage is counted within it. <b>Required whenever the package holds more
/// than one skill:</b> Reading and Listening both number their questions 1 to
/// 40, so an unfiltered apply writes Reading's answers onto Listening's
/// questions and reports nothing wrong. The operator CLI avoids this by
/// running one skill at a time (<c>--keep-module</c>); the HTTP door cannot.
/// </param>
public static Result Apply(
    string packageJson,
    IReadOnlyList<AnswerKeyEntry> entries,
    ExamModule? module = null)
```

Inside, the enumeration `Questions(package)` gains the filter. `Questions` currently walks sections, parts and questions; give it the module and skip a section whose `module` string does not match:

```csharp
private static IEnumerable<(JsonObject Node, string Path)> Questions(
    JsonObject package, ExamModule? module)
{
    var wanted = module?.ToString().ToLowerInvariant();
    var sectionIndex = -1;
    foreach (var section in package["sections"]?.AsArray() ?? [])
    {
        sectionIndex++;
        if (section is not JsonObject s) continue;
        if (wanted is not null && s["module"]?.GetValue<string>() != wanted) continue;
        // …existing part and question walk, unchanged…
    }
}
```

Pass `module` at both call sites inside `Apply`, including the one inside `FoldMultiMarkChoices`, so folding and keying see the same question set.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~AnswerKey"`
Expected: PASS, including every pre-existing `AnswerKeyTests` and `FabricatedAnswerKeyGuardTests` case. The default `module: null` keeps every existing caller behaving exactly as before.

- [ ] **Step 5: Verify the new test goes red when the fix is removed**

Temporarily drop the `if (wanted is not null && …) continue;` line, re-run, and confirm `A_key_applies_only_to_the_skill_it_belongs_to` fails because Listening question 1 also carries `TRUE`. Restore it.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Vni.Ielts.Application/Importing/AnswerKeyInjection.cs \
        backend/tests/Vni.Ielts.Application.Tests/Importing/AnswerKeyTests.cs
git commit -m "feat(import): apply an answer key to one skill, so Reading and Listening stop colliding"
```

---

### Task 3: The pipeline sends the paper to the model and the key to code

**Files:**
- Modify: `backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs:100-180`
- Test: `backend/tests/Vni.Ielts.Infrastructure.Tests/Content/Import/ExamPackageImportPipelineTests.cs` (create if absent)

**Interfaces:**
- Consumes: `PackageLayout.For(module).Paper` and `.Key` from Task 1; `AnswerKeyInjection.Apply(json, entries, module)` from Task 2.
- Produces: no new public type. Task 4 and Task 5 run on the package this task produces.

- [ ] **Step 1: Write the failing test**

The point of the test is that the key text never reaches the parser, so the fake parser must record what it was given.

```csharp
/// <summary>
/// A parser that remembers its input, so a test can assert what the model
/// would have seen. The real parser is an AI call; what it is handed is the
/// whole security property of this task.
/// </summary>
private sealed class RecordingParser : IExamSourceParser
{
    public string? LastSourceText { get; private set; }

    public Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct)
    {
        LastSourceText = source.Text;
        // Metadata is non-nullable on ParsedExamPackage; a fake still has to
        // name a provider, because the draft records what produced it.
        return Task.FromResult(new ParsedExamPackage(
            OneReadingQuestion(),
            new ParserRunMetadata("fake", "recording-parser", "test", "req-1")));
    }

    private static string OneReadingQuestion() =>
        """
        {
          "formatVersion": "2.0",
          "title": "T",
          "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
            "kind": "passage", "body": "The roof is made of slate.",
            "questions": [ { "id": "r1", "order": 1, "type": "true-false-notgiven" } ] } ] } ]
        }
        """;
}

[Fact]
public async Task The_answer_key_never_reaches_the_parser()
{
    var parser = new RecordingParser();
    var pipeline = PipelineWith(parser);
    var archive = Build(
        File("reading/de/passage.txt", "The roof is made of slate."),
        File("reading/dap-an/key.txt", "Câu số 1: TRUE"));

    var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

    Assert.True(attempt.IsAccepted, Describe(attempt.Findings));
    Assert.NotNull(parser.LastSourceText);
    Assert.DoesNotContain("TRUE", parser.LastSourceText);
    Assert.DoesNotContain("Câu số", parser.LastSourceText);
    Assert.Contains("slate", parser.LastSourceText);
}

[Fact]
public async Task The_supplied_key_is_written_onto_the_parsed_package()
{
    var pipeline = PipelineWith(new RecordingParser());
    var archive = Build(
        File("reading/de/passage.txt", "The roof is made of slate."),
        File("reading/dap-an/key.txt", "Câu số 1: TRUE"));

    var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

    var doc = JsonNode.Parse(attempt.Draft!.PackageJson)!.AsObject();
    var question = doc["sections"]![0]!["parts"]![0]!["questions"]![0]!;
    Assert.Equal("TRUE", question["answerKey"]!["accepted"]!.AsArray()[0]!.GetValue<string>());
}

/// <summary>
/// With a key document in hand, an answer the model invented is worthless and
/// is about to be overwritten. Stripping first is what stops a model that
/// solved the paper anyway from leaving a guess on a question the key does
/// not cover. → FabricatedAnswerKeyGuard.Strip
/// </summary>
[Fact]
public async Task Model_written_answers_are_stripped_before_the_real_key_is_applied()
{
    var parser = new RecordingParser { }; // returns a package with no answerKey
    var pipeline = PipelineWith(parser);
    var archive = Build(
        File("reading/de/passage.txt", "The roof is made of slate."),
        File("reading/dap-an/key.txt", "Câu số 1: TRUE"));

    var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

    Assert.DoesNotContain(
        attempt.Draft!.Warnings,
        w => w.Id.StartsWith("FABRICATED_ANSWER_KEY", StringComparison.Ordinal));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Vni.Ielts.Infrastructure.Tests --nologo --filter "FullyQualifiedName~ExamPackageImportPipelineTests"`
Expected: FAIL — `LastSourceText` contains `Câu số 1: TRUE`, because today every file under `reading/` is concatenated into the parser's input.

- [ ] **Step 3: Send only the paper**

In `ImportFromSandboxAsync`, the concatenation loop reads `.Paper` instead of `.All`:

```csharp
var combined = new StringBuilder();
foreach (var skill in layout.PresentSkills)
{
    foreach (var relativePath in layout.For(skill).Paper)
    {
        var extracted = await extractor.ExtractAsync(
            sandboxDirectory, relativePath, SourceExtractionLimits.Default, ct);

        if (!extracted.IsSuccess || extracted.Source is null)
            return ExamImportAttempt.Rejected(extracted.Findings);

        combined.AppendLine($"## {skill} / {relativePath}");
        combined.AppendLine(extracted.Source.Text);
        combined.AppendLine();
    }
}
```

- [ ] **Step 4: Read the key with code and write it on**

After the parse attempt succeeds and before the fabrication guard, add:

```csharp
/// <summary>
/// Reads every key document, one skill at a time, and writes it onto the
/// parsed package.
///
/// <b>The model is never shown any of this.</b> Measured on VOL 9: with the
/// key in the prompt a model scored 36 of 38 and both misses were alignment
/// failures — the right answer on the wrong question. With the key read by
/// code the same paper scored 34 of 38 exactly, and refused on the other
/// four rather than guessing. Counting is what code does better.
/// </summary>
private async Task<ExamImportAttempt> ApplySuppliedKeysAsync(
    ExamImportDraft draft, PackageLayout layout, string sandboxDirectory, CancellationToken ct)
{
    var json = draft.PackageJson;
    var findings = new List<PackageFinding>();
    var applied = false;

    foreach (var skill in layout.PresentSkills)
    {
        var keyFiles = layout.For(skill).Key;
        if (keyFiles.Count == 0) continue;

        var entries = new List<AnswerKeyEntry>();
        foreach (var relativePath in keyFiles)
        {
            var extracted = await extractor.ExtractAsync(
                sandboxDirectory, relativePath, SourceExtractionLimits.Default, ct);

            if (!extracted.IsSuccess || extracted.Source is null)
                return ExamImportAttempt.Rejected(extracted.Findings);

            entries.AddRange(AnswerKeyDocument.Parse(extracted.Source.Text));
        }

        if (entries.Count == 0)
        {
            findings.Add(new PackageFinding(
                "error", "ANSWER_KEY_UNREADABLE", $"/sections/{skill}",
                $"The {skill} key folder holds no answers this reader recognises. "
                + "Two formats are read: numbered lines, and a bare ordered list."));
            continue;
        }

        // A key is about to be written over every question, so a guess the
        // model left behind is worthless. Strip first, key second.
        (json, _) = FabricatedAnswerKeyGuard.Strip(json);

        var result = AnswerKeyInjection.Apply(json, entries, skill);
        json = result.PackageJson;
        findings.AddRange(result.Findings);
        applied = true;
    }

    if (!applied) return ExamImportAttempt.Accepted(draft);

    var updated = draft with
    {
        PackageJson = json,
        PackageHash = ExamImportWorkflow.Hash(json),
        Findings = [.. draft.Findings, .. findings],
        Revision = draft.Revision + 1,
    };

    var replaced = await drafts.ReplaceAsync(updated, draft.Revision, ct);
    return ExamImportAttempt.Accepted(replaced ? updated : draft);
}
```

- [ ] **Step 5: Choose the guard by what the layout carried, not by the package**

Replace the hard-coded `sourceIncludesAnswerKey: false` call site so the two paths are explicit:

```csharp
var attempt = await workflow.ImportExtractedAsync(source, definitionId, versionNumber, ct);
if (!attempt.IsAccepted || attempt.Draft is null) return attempt;

var suppliedAKey = layout.PresentSkills.Any(s => layout.For(s).Key.Count > 0);

/*
 * Derived from the layout, never from the package. Inferring "a key was
 * supplied" from the presence of answer keys would make the check vacuous —
 * the guard's own remarks say so, and the caller is the only party that
 * actually knows.
 */
return suppliedAKey
    ? await ApplySuppliedKeysAsync(attempt.Draft, layout, sandboxDirectory, ct)
    : await GuardAgainstFabricatedAnswersAsync(attempt.Draft, ct);
```

> **Task 4 changes this return** so both branches feed the cross-checkers. Leave it as written here; Task 4's step shows the final shape.

`ApplySuppliedKeysAsync` therefore ends without calling any checker — it applies keys and reports what `AnswerKeyInjection` found, nothing more.

`ImportFromSandboxAsync` needs `sandboxDirectory` in scope at this point; it already is.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/tests/Vni.Ielts.Infrastructure.Tests --nologo --filter "FullyQualifiedName~ExamPackageImportPipelineTests"`
Expected: PASS.

- [ ] **Step 7: Verify the security test goes red when the fix is removed**

Temporarily change `.Paper` back to `.All` in the concatenation loop, re-run, and confirm `The_answer_key_never_reaches_the_parser` fails on `Assert.DoesNotContain("TRUE", …)`. Restore it. **This is the most important negative proof in the plan; record the exact failure message in the task report.**

- [ ] **Step 8: Commit**

```bash
git add backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs \
        backend/tests/Vni.Ielts.Infrastructure.Tests/Content/Import/ExamPackageImportPipelineTests.cs
git commit -m "fix(import): stop sending the answer key to the model, and apply the real key with code"
```

---

### Task 4: The paper's own stated rules are checked against the key

**Files:**
- Create: `backend/src/Vni.Ielts.Application/Importing/PaperKeyConsistency.cs`
- Modify: `backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs` (call it after keys are applied)
- Test: `backend/tests/Vni.Ielts.Application.Tests/Importing/PaperKeyConsistencyTests.cs`

**Interfaces:**
- Consumes: a package JSON with answer keys written on, from Task 3.
- Produces: `PaperKeyConsistency.Inspect(string packageJson) → IReadOnlyList<PackageFinding>` with codes `KEY_MARK_COUNT_MISMATCH`, `KEY_EXCEEDS_WORD_LIMIT`, `KEY_LABEL_REUSED`, all severity `"error"`. Task 6 refuses approval on them.

**What this checks.** Three things the paper states about itself, which nothing currently compares the key against:

| Check | Field the paper declares it in |
|---|---|
| A question worth two marks needs a two-part answer | `question.marks` |
| An answer must fit the stated word limit | `question.constraints.maxWords` |
| A no-reuse matching group must not reuse a label | `question.group.eachLetterOnce` |

`AnswerMatcher.ExceedsWordLimit` already applies the word limit to fail a *learner's* answer. It has never been applied to the answer key, so the system is stricter with learners than with the answers it marks them against.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json.Nodes;
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class PaperKeyConsistencyTests
{
    [Fact]
    public void A_two_mark_question_answered_with_one_letter_is_refused()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "multiple-select", "marks": 2,
              "answerKey": { "accepted": [ "A" ] } }
            """);

        var findings = PaperKeyConsistency.Inspect(package);

        var finding = Assert.Single(findings);
        Assert.Equal(PaperKeyConsistency.MarkCountCode, finding.Code);
        Assert.Equal("error", finding.Severity);
        Assert.Contains("1", finding.Message);
    }

    [Fact]
    public void A_two_mark_question_answered_with_two_letters_passes()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "multiple-select", "marks": 2,
              "answerKey": { "accepted": [ [ "A", "C" ] ] } }
            """);

        Assert.Empty(PaperKeyConsistency.Inspect(package));
    }

    [Fact]
    public void A_key_answer_over_the_papers_own_word_limit_is_refused()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "completion",
              "constraints": { "maxWords": 2 },
              "answerKey": { "accepted": [ "a very long answer" ] } }
            """);

        var finding = Assert.Single(PaperKeyConsistency.Inspect(package));
        Assert.Equal(PaperKeyConsistency.WordLimitCode, finding.Code);
    }

    /// <summary>
    /// Every alternative is one a learner may type, so every alternative has
    /// to fit. A second alternative over the limit is a trap: it is marked
    /// correct by the key and wrong by the word-limit rule.
    /// </summary>
    [Fact]
    public void Every_accepted_alternative_must_fit_the_word_limit()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "completion",
              "constraints": { "maxWords": 2 },
              "answerKey": { "accepted": [ "slate roof", "a roof made of slate" ] } }
            """);

        Assert.Single(PaperKeyConsistency.Inspect(package));
    }

    [Fact]
    public void A_no_reuse_group_that_reuses_a_label_is_refused()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "matching",
              "group": { "id": "g1", "eachLetterOnce": true },
              "answerKey": { "accepted": [ "B" ] } }
            """, """
            { "id": "q2", "order": 2, "type": "matching",
              "group": { "id": "g1", "eachLetterOnce": true },
              "answerKey": { "accepted": [ "B" ] } }
            """);

        var finding = Assert.Single(PaperKeyConsistency.Inspect(package));
        Assert.Equal(PaperKeyConsistency.LabelReuseCode, finding.Code);
        Assert.Contains("2", finding.Message);
    }

    /// <summary>
    /// Reuse is legal unless the paper says otherwise — "NB You may use any
    /// letter more than once". Refusing it by default would reject correct
    /// papers, which is the expensive kind of false alarm here.
    /// </summary>
    [Fact]
    public void A_group_without_the_no_reuse_flag_may_reuse_a_label()
    {
        var package = Package("""
            { "id": "q1", "order": 1, "type": "matching",
              "group": { "id": "g1" }, "answerKey": { "accepted": [ "B" ] } }
            """, """
            { "id": "q2", "order": 2, "type": "matching",
              "group": { "id": "g1" }, "answerKey": { "accepted": [ "B" ] } }
            """);

        Assert.Empty(PaperKeyConsistency.Inspect(package));
    }

    private static string Package(params string[] questions) =>
        $$"""
          { "formatVersion": "2.0",
            "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
              "kind": "passage", "questions": [ {{string.Join(",", questions)}} ] } ] } ] }
          """;
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~PaperKeyConsistencyTests"`
Expected: FAIL to compile — `PaperKeyConsistency` does not exist.

- [ ] **Step 3: Write the checker**

```csharp
using System.Text.Json.Nodes;

namespace Vni.Ielts.Application.Importing;

/// <summary>
/// Compares a keyed package against the rules the paper states about its own
/// questions. Every check here is a contradiction between two declarations in
/// the same document, so every finding is an error and none is a judgement
/// call — there is nothing for a reviewer to weigh, and therefore no override.
///
/// <b>Layer 3 of the cross-check.</b> Layer 1 (counting) and layer 2 (shape)
/// already run inside <see cref="AnswerKeyInjection"/>. This layer exists
/// because those two ask whether an answer *could* belong to a question, and
/// three fields on the paper say more than that.
/// </summary>
public static class PaperKeyConsistency
{
    public const string MarkCountCode = "KEY_MARK_COUNT_MISMATCH";
    public const string WordLimitCode = "KEY_EXCEEDS_WORD_LIMIT";
    public const string LabelReuseCode = "KEY_LABEL_REUSED";

    public static IReadOnlyList<PackageFinding> Inspect(string packageJson)
    {
        var package = JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException(
                "The package did not parse as an object.", nameof(packageJson));

        var findings = new List<PackageFinding>();
        var usedLabels = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var (question, path) in Questions(package))
        {
            var accepted = question["answerKey"]?["accepted"]?.AsArray();
            if (accepted is null || accepted.Count == 0) continue;

            var order = question["order"]?.GetValue<int>() ?? 0;

            CheckMarkCount(question, accepted, order, path, findings);
            CheckWordLimit(question, accepted, order, path, findings);
            CheckLabelReuse(question, accepted, order, path, usedLabels, findings);
        }

        return findings;
    }

    /// <summary>
    /// "Choose TWO letters" occupies two numbered lines on the answer sheet
    /// and is worth two marks. An answer with one letter under it is a key
    /// that lost a value, and the scorer would mark every candidate wrong.
    /// </summary>
    private static void CheckMarkCount(
        JsonObject question, JsonArray accepted, int order, string path,
        List<PackageFinding> findings)
    {
        var marks = question["marks"]?.GetValue<int>() ?? 1;
        if (marks <= 1) return;

        foreach (var alternative in accepted)
        {
            var count = alternative is JsonArray array ? array.Count : 1;
            if (count == marks) continue;

            findings.Add(new PackageFinding(
                "error", MarkCountCode, path,
                $"Question {order} is worth {marks} marks, but the key gives {count} "
                + "answer(s) for it. The key document may have folded them onto one line."));
            return;
        }
    }

    /// <summary>
    /// The paper prints "NO MORE THAN TWO WORDS"; an over-length answer is
    /// marked wrong. <see cref="Vni.Ielts.Domain.Exams.AnswerMatcher.ExceedsWordLimit"/>
    /// already applies that rule to a learner. Nothing applied it to the key.
    /// </summary>
    private static void CheckWordLimit(
        JsonObject question, JsonArray accepted, int order, string path,
        List<PackageFinding> findings)
    {
        var limit = question["constraints"]?["maxWords"]?.GetValue<int>();
        if (limit is not { } maxWords) return;

        foreach (var alternative in accepted)
        {
            if (alternative is not JsonValue value) continue;
            var words = WordCount(value.GetValue<string>());
            if (words <= maxWords) continue;

            findings.Add(new PackageFinding(
                "error", WordLimitCode, path,
                $"Question {order} allows at most {maxWords} word(s); the key holds an "
                + $"answer of {words}. A learner typing it would be marked wrong."));
            return;
        }
    }

    /// <summary>
    /// Only when the paper declares <c>eachLetterOnce</c>. A group that
    /// permits reuse ("NB You may use any letter more than once") is normal
    /// IELTS and must not be reported.
    /// </summary>
    private static void CheckLabelReuse(
        JsonObject question, JsonArray accepted, int order, string path,
        Dictionary<string, Dictionary<string, int>> usedLabels,
        List<PackageFinding> findings)
    {
        var group = question["group"];
        if (group?["eachLetterOnce"]?.GetValue<bool>() is not true) return;
        if (group["id"]?.GetValue<string>() is not { Length: > 0 } groupId) return;
        if (accepted[0] is not JsonValue first) return;

        var label = first.GetValue<string>();
        if (!usedLabels.TryGetValue(groupId, out var seen))
            usedLabels[groupId] = seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (seen.TryGetValue(label, out var earlier))
        {
            findings.Add(new PackageFinding(
                "error", LabelReuseCode, path,
                $"Question {order} reuses a label already used by question {earlier} in a "
                + "group the paper marks as use-each-letter-once. One of the two is wrong."));
            return;
        }

        seen[label] = order;
    }

    private static int WordCount(string value) =>
        value.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static IEnumerable<(JsonObject Question, string Path)> Questions(JsonObject package)
    {
        var sectionIndex = -1;
        foreach (var section in package["sections"]?.AsArray() ?? [])
        {
            sectionIndex++;
            var module = section?["module"]?.GetValue<string>() ?? "unknown";
            var partIndex = -1;
            foreach (var part in section?["parts"]?.AsArray() ?? [])
            {
                partIndex++;
                var questionIndex = -1;
                foreach (var question in part?["questions"]?.AsArray() ?? [])
                {
                    questionIndex++;
                    if (question is JsonObject q)
                        yield return (q, $"/sections/{module}/parts/{partIndex}/questions/{questionIndex}");
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the checker from the pipeline, on both routes**

Add a helper to `ExamPackageImportPipeline` and call it on whatever draft Task 3's branch produced. **It must run whether or not a key folder was supplied** — that is the point of putting it here rather than inside `ApplySuppliedKeysAsync`:

```csharp
/// <summary>
/// Runs the deterministic cross-checks over a draft's package, whatever
/// produced its answers.
///
/// <b>A package with no key folder gets checked too, deliberately.</b> Its
/// answers were written by the model, and a fabricated answer that
/// contradicts its own question type or exceeds the paper's stated word limit
/// is caught by exactly these checks. The 2026-09-02 measurement is the
/// argument: forty invented answers, five of them wrong, and every one passed
/// schema validation. The fabrication warning alone says a key was invented;
/// these findings say which of the invented answers are impossible.
/// </summary>
private async Task<ExamImportAttempt> CrossCheckAsync(
    ExamImportDraft draft, CancellationToken ct)
{
    var findings = PaperKeyConsistency.Inspect(draft.PackageJson);
    if (findings.Count == 0) return ExamImportAttempt.Accepted(draft);

    var updated = draft with
    {
        Findings = [.. draft.Findings, .. findings],
        Revision = draft.Revision + 1,
    };

    var replaced = await drafts.ReplaceAsync(updated, draft.Revision, ct);
    return ExamImportAttempt.Accepted(replaced ? updated : draft);
}
```

Change Task 3's branch so both routes feed it:

```csharp
var keyed = suppliedAKey
    ? await ApplySuppliedKeysAsync(attempt.Draft, layout, sandboxDirectory, ct)
    : await GuardAgainstFabricatedAnswersAsync(attempt.Draft, ct);

if (!keyed.IsAccepted || keyed.Draft is null) return keyed;
return await CrossCheckAsync(keyed.Draft, ct);
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~PaperKeyConsistencyTests"`
Expected: PASS, 6 tests.

- [ ] **Step 6: Verify each check goes red when removed**

Comment out the body of `CheckMarkCount`, run, confirm the two-mark test fails. Restore. Repeat for `CheckWordLimit` and `CheckLabelReuse`. Three separate observations; record all three.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Vni.Ielts.Application/Importing/PaperKeyConsistency.cs \
        backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs \
        backend/tests/Vni.Ielts.Application.Tests/Importing/PaperKeyConsistencyTests.cs
git commit -m "feat(import): check the answer key against the rules the paper states about itself"
```

---

### Task 5: Every completion answer is anchored in its passage

**Files:**
- Modify: `backend/src/Vni.Ielts.Domain/Exams/AnswerMatcher.cs:133` (make `Normalise` public)
- Create: `backend/src/Vni.Ielts.Application/Importing/PassageAnchorCheck.cs`
- Modify: `backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs` (call it)
- Test: `backend/tests/Vni.Ielts.Application.Tests/Importing/PassageAnchorCheckTests.cs`

**Interfaces:**
- Consumes: a keyed package from Task 3; `AnswerMatcher.Normalise(string, AnswerMatchingRules)` made public in Step 1.
- Produces: `PassageAnchorCheck.Inspect(string packageJson) → AnchorReport(IReadOnlyList<PackageFinding> Findings, IReadOnlyList<QuestionAnchor> Anchors)`. `QuestionAnchor(string QuestionId, int Order, bool Anchored)` is what the CMS panel renders in the next plan.

**Why this is the highest-value check.** For `completion`, `short-answer` and `labelling` — about half a Reading paper and most of a Listening paper — IELTS requires the answer to be words taken from the passage. So the answer must be findable in the passage (a string search, no AI), and within a group the answers must appear in question order. A key shifted by one line makes that order run backwards at the shift point, which names the first bad question. This catches, deterministically and for free, the exact failure that layers 1 and 2 cannot see.

- [ ] **Step 1: Expose the normaliser**

Change `private static string Normalise(string value, AnswerMatchingRules rules)` to `public static string Normalise(…)` and give it a remark:

```csharp
/// <summary>
/// …existing behaviour…
///
/// <b>Public because the import-time anchor check needs the same normalisation
/// for both sides of its comparison.</b> Searching a raw passage for a
/// normalised answer finds nothing whenever the passage differs in case or
/// spacing, which is most of the time; duplicating the rules in a second
/// place is how two normalisers drift apart. Note that normalising a whole
/// passage moves character offsets, so a caller wanting a highlight position
/// in the original text must re-find it there.
/// </summary>
```

- [ ] **Step 2: Write the failing test**

```csharp
using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class PassageAnchorCheckTests
{
    private const string Passage =
        "The hall has a slate roof. Beneath it the timber frame was rebuilt in 1974 "
        + "after a fire, and the west window was replaced with stained glass.";

    [Fact]
    public void A_completion_answer_present_in_the_passage_is_anchored()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "slate roof")));

        Assert.Empty(report.Findings);
        Assert.True(Assert.Single(report.Anchors).Anchored);
    }

    [Fact]
    public void A_completion_answer_absent_from_the_passage_is_refused()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "copper roof")));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(PassageAnchorCheck.NotInPassageCode, finding.Code);
        Assert.Equal("error", finding.Severity);
        Assert.False(Assert.Single(report.Anchors).Anchored);
    }

    /// <summary>
    /// Case and spacing differ constantly between a key document and a
    /// passage. Reporting those as missing answers would bury the real ones.
    /// </summary>
    [Fact]
    public void Case_and_spacing_differences_do_not_count_as_absent()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "  SLATE   ROOF ")));

        Assert.Empty(report.Findings);
    }

    /// <summary>
    /// The shift this whole layer exists for. Answers 1 and 2 are each in the
    /// passage, and each is legal for its question type, so layers 1 to 3 see
    /// nothing. Only their order gives it away.
    /// </summary>
    [Fact]
    public void Answers_running_backwards_within_a_group_are_reported_at_the_shift()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage,
            Completion(1, "stained glass"),
            Completion(2, "slate roof")));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(PassageAnchorCheck.OutOfOrderCode, finding.Code);
        Assert.Equal("warning", finding.Severity);
        Assert.Contains("2", finding.Message);
    }

    /// <summary>
    /// A word occurring several times must not fire the order check: some
    /// choice of occurrences may well be in order, and reporting on the first
    /// one found would make common words permanently suspicious.
    /// </summary>
    [Fact]
    public void A_repeated_word_does_not_produce_an_order_warning()
    {
        const string repeated = "The roof was replaced. Later the roof leaked again.";

        var report = PassageAnchorCheck.Inspect(Package(repeated,
            Completion(1, "roof"),
            Completion(2, "roof")));

        Assert.Empty(report.Findings);
    }

    /// <summary>
    /// Nothing in the group anchored means the passage is wrong, not the key.
    /// The administrator's next action is completely different — re-parse the
    /// paper, do not touch the key — so the finding has to say which.
    /// </summary>
    [Fact]
    public void A_group_with_nothing_anchored_blames_the_paper_not_the_key()
    {
        var report = PassageAnchorCheck.Inspect(Package("An unrelated passage entirely.",
            Completion(1, "slate roof"),
            Completion(2, "stained glass")));

        var finding = Assert.Single(report.Findings);
        Assert.Equal(PassageAnchorCheck.PassageMismatchCode, finding.Code);
        Assert.Contains("passage", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True/False/Not Given answers are not words from the passage. Searching
    /// for "TRUE" in a passage would report almost every one as absent.
    /// </summary>
    [Fact]
    public void Question_types_whose_answers_are_not_passage_words_are_skipped()
    {
        var report = PassageAnchorCheck.Inspect(Package(Passage, """
            { "id": "q1", "order": 1, "type": "true-false-notgiven",
              "group": { "id": "g1" }, "answerKey": { "accepted": [ "TRUE" ] } }
            """));

        Assert.Empty(report.Findings);
        Assert.Empty(report.Anchors);
    }

    private static string Completion(int order, string answer) =>
        $$"""
          { "id": "q{{order}}", "order": {{order}}, "type": "completion",
            "group": { "id": "g1" },
            "answerKey": { "accepted": [ "{{answer}}" ] } }
          """;

    private static string Package(string body, params string[] questions) =>
        $$"""
          { "formatVersion": "2.0",
            "sections": [ { "module": "reading", "order": 1, "parts": [ { "order": 1,
              "kind": "passage", "body": "{{body}}",
              "questions": [ {{string.Join(",", questions)}} ] } ] } ] }
          """;
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~PassageAnchorCheckTests"`
Expected: FAIL to compile — `PassageAnchorCheck` does not exist.

- [ ] **Step 4: Write the checker**

```csharp
using System.Text.Json.Nodes;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Importing;

/// <summary>Whether one question's key answer was found in its own passage.</summary>
public sealed record QuestionAnchor(string QuestionId, int Order, bool Anchored);

public sealed record AnchorReport(
    IReadOnlyList<PackageFinding> Findings, IReadOnlyList<QuestionAnchor> Anchors);

/// <summary>
/// Layer 4 of the cross-check: anchors each answer in the text it must have
/// come from.
///
/// <b>Why this layer is worth more than the three above it.</b> IELTS requires
/// the answer to a completion, short-answer or labelling question to be words
/// taken from the passage, and those types are about half a Reading paper.
/// So the answer must be findable — a string search, no model, no cost — and
/// within a group the answers must appear in the passage in question order.
///
/// A key shifted by one line therefore makes the sequence of found positions
/// run <i>backwards</i> at the shift point, and names the first bad question.
/// That is exactly the failure layers 1 to 3 are blind to: on VOL 9 Test 2,
/// question 13 held FALSE where the answer was TRUE and question 36 held A
/// where the answer was C. Both are legal answers of the right shape.
///
/// <b>What it cannot do.</b> True/False/Not Given, Yes/No/Not Given,
/// multiple-choice and matching answers are not words from the passage, so
/// they are skipped entirely and left to layer 5 and to a human.
/// </summary>
public static class PassageAnchorCheck
{
    public const string NotInPassageCode = "KEY_ANSWER_NOT_IN_PASSAGE";
    public const string OutOfOrderCode = "KEY_ANSWERS_OUT_OF_PASSAGE_ORDER";
    public const string PassageMismatchCode = "PASSAGE_DOES_NOT_MATCH_QUESTIONS";

    /// <summary>The types whose answers IELTS takes from the passage.</summary>
    private static readonly HashSet<string> Anchorable =
        new(StringComparer.OrdinalIgnoreCase) { "completion", "short-answer", "labelling" };

    public static AnchorReport Inspect(string packageJson)
    {
        var package = JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException(
                "The package did not parse as an object.", nameof(packageJson));

        var findings = new List<PackageFinding>();
        var anchors = new List<QuestionAnchor>();

        foreach (var (part, path, source) in Parts(package))
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            var haystack = AnswerMatcher.Normalise(source, AnswerMatchingRules.Default);

            foreach (var group in AnchorableQuestionsByGroup(part))
            {
                var positions = new List<(int Order, string Id, IReadOnlyList<int> At)>();

                foreach (var (question, order, id, answer) in group)
                {
                    var needle = AnswerMatcher.Normalise(answer, AnswerMatchingRules.Default);
                    var at = Occurrences(haystack, needle);
                    positions.Add((order, id, at));
                    anchors.Add(new QuestionAnchor(id, order, at.Count > 0));
                }

                var missing = positions.Where(p => p.At.Count == 0).ToArray();

                /*
                 * Nothing anchored means the passage is wrong, not the key —
                 * a truncated body, the wrong passage, a bad OCR crop. Saying
                 * "12 answers are missing" would send an administrator to fix
                 * twelve answers that are all correct.
                 */
                if (missing.Length == positions.Count && positions.Count > 1)
                {
                    findings.Add(new PackageFinding(
                        "error", PassageMismatchCode, path,
                        $"None of the {positions.Count} anchorable answers in this group appears "
                        + "in the passage. The passage is probably the wrong one or was cut short; "
                        + "re-check the parsed paper before touching the key."));
                    continue;
                }

                foreach (var (order, _, _) in missing)
                {
                    findings.Add(new PackageFinding(
                        "error", NotInPassageCode, path,
                        $"The key answer for question {order} does not appear in the passage. "
                        + "This question type takes its answer from the text, so no learner "
                        + "could produce it."));
                }

                if (missing.Length > 0) continue;

                if (FirstBackwardsAt(positions) is { } culprit)
                {
                    findings.Add(new PackageFinding(
                        "warning", OutOfOrderCode, path,
                        $"Answers in this group stop following the passage's order at question "
                        + $"{culprit}. That is the shape of a key shifted by a line. Rare "
                        + "exceptions exist, so check against the original key before clearing."));
                }
            }
        }

        return new AnchorReport(findings, anchors);
    }

    /// <summary>
    /// The order check, done as a greedy earliest-legal walk rather than on
    /// the first occurrence of each answer.
    ///
    /// A word can occur many times. Taking its first occurrence would report
    /// a perfectly ordered key as backwards whenever an earlier paragraph
    /// happens to repeat a later answer — so common words would become
    /// permanently suspicious and the signal would be worthless. Instead each
    /// answer takes the earliest occurrence after the one before it; only
    /// when no such occurrence exists is the order genuinely broken.
    /// </summary>
    private static int? FirstBackwardsAt(
        IReadOnlyList<(int Order, string Id, IReadOnlyList<int> At)> positions)
    {
        var cursor = -1;
        foreach (var (order, _, at) in positions.OrderBy(p => p.Order))
        {
            var next = at.FirstOrDefault(p => p > cursor, -1);
            if (next < 0) return order;
            cursor = next;
        }

        return null;
    }

    private static IReadOnlyList<int> Occurrences(string haystack, string needle)
    {
        if (needle.Length == 0) return [];

        var found = new List<int>();
        var from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            var at = haystack.IndexOf(needle, from, StringComparison.Ordinal);
            if (at < 0) break;
            found.Add(at);
            from = at + 1;
        }

        return found;
    }

    private static IEnumerable<(JsonObject Part, string Path, string? Source)> Parts(
        JsonObject package)
    {
        foreach (var section in package["sections"]?.AsArray() ?? [])
        {
            var module = section?["module"]?.GetValue<string>() ?? "unknown";
            var partIndex = -1;
            foreach (var part in section?["parts"]?.AsArray() ?? [])
            {
                partIndex++;
                if (part is not JsonObject p) continue;

                // Reading anchors in the passage body; Listening in the
                // transcript, when one was supplied (a missing transcript is
                // a warning under P-19, not a reason to refuse the package).
                var source = p["body"]?.GetValue<string>() ?? p["transcript"]?.GetValue<string>();
                yield return (p, $"/sections/{module}/parts/{partIndex}", source);
            }
        }
    }

    private static IEnumerable<List<(JsonObject Question, int Order, string Id, string Answer)>>
        AnchorableQuestionsByGroup(JsonObject part)
    {
        var groups = new Dictionary<string, List<(JsonObject, int, string, string)>>(
            StringComparer.Ordinal);

        foreach (var node in part["questions"]?.AsArray() ?? [])
        {
            if (node is not JsonObject question) continue;
            if (question["type"]?.GetValue<string>() is not { } type) continue;
            if (!Anchorable.Contains(type)) continue;
            if (question["answerKey"]?["accepted"]?.AsArray() is not { Count: > 0 } accepted) continue;
            if (accepted[0] is not JsonValue first) continue;

            var id = question["id"]?.GetValue<string>() ?? string.Empty;
            var order = question["order"]?.GetValue<int>() ?? 0;

            // A question with no group is its own group of one: containment
            // still applies, order has nothing to compare against.
            var groupId = question["group"]?["id"]?.GetValue<string>() ?? $"__solo-{id}";

            if (!groups.TryGetValue(groupId, out var members))
                groups[groupId] = members = [];

            members.Add((question, order, id, first.GetValue<string>()));
        }

        return groups.Values;
    }
}
```

- [ ] **Step 5: Run the checker from the pipeline**

In `CrossCheckAsync`, which Task 4 added, the two checkers run together so both routes get both layers:

```csharp
var findings = new List<PackageFinding>(PaperKeyConsistency.Inspect(draft.PackageJson));
findings.AddRange(PassageAnchorCheck.Inspect(draft.PackageJson).Findings);
if (findings.Count == 0) return ExamImportAttempt.Accepted(draft);
```

Add a pipeline-level test that the no-key route is checked too, since that is the route a reader most easily assumes is skipped:

```csharp
/// <summary>
/// No key folder means the answers came from the model. Those are exactly
/// the answers most worth checking.
/// </summary>
[Fact]
public async Task A_package_with_no_key_folder_is_still_cross_checked()
{
    var pipeline = PipelineWith(new ParserInventingAnImpossibleAnswer());
    var archive = Build(File("reading/de/passage.txt", "The hall has a slate roof."));

    var attempt = await pipeline.ImportAsync(archive, ExamDefinitionId.New(), 1, default);

    Assert.Contains(
        attempt.Draft!.Findings,
        f => f.Code == PassageAnchorCheck.NotInPassageCode);
}
```

`ParserInventingAnImpossibleAnswer` is a copy of `RecordingParser` whose returned package carries one `completion` question with `"accepted": [ "copper roof" ]` against that passage.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~PassageAnchorCheckTests"`
Expected: PASS, 7 tests.

- [ ] **Step 7: Verify the order check goes red when removed**

Replace `FirstBackwardsAt` with `return null;`, re-run, and confirm `Answers_running_backwards_within_a_group_are_reported_at_the_shift` fails. Restore. Then replace the greedy walk with first-occurrence-only (`var next = at[0];`) and confirm `A_repeated_word_does_not_produce_an_order_warning` fails — this proves the greedy walk is doing work, not decoration. Record both.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Vni.Ielts.Domain/Exams/AnswerMatcher.cs \
        backend/src/Vni.Ielts.Application/Importing/PassageAnchorCheck.cs \
        backend/src/Vni.Ielts.Infrastructure/Content/Import/ExamPackageImportPipeline.cs \
        backend/tests/Vni.Ielts.Application.Tests/Importing/PassageAnchorCheckTests.cs
git commit -m "feat(import): anchor every completion answer in its passage, and catch a shifted key by order"
```

---

### Task 6: A draft carrying a blocking finding cannot be approved

**Files:**
- Modify: `backend/src/Vni.Ielts.Application/Importing/ImportReviewWorkflow.cs:110-131` (`ApproveAsync`)
- Test: `backend/tests/Vni.Ielts.Application.Tests/Importing/ImportReviewWorkflowTests.cs`

**Interfaces:**
- Consumes: findings of severity `"error"` produced by Tasks 3, 4 and 5.
- Produces: refusal code `"IMPORT_FINDINGS_BLOCKING"` on `ImportReviewResult.ErrorCode` (the property is named `ErrorCode`, not `RefusalCode` — `ImportReviewResult.Refused` passes it positionally, which is easy to misread).

**Why a new gate.** `ApproveAsync` today checks unresolved warnings and the checklist. It never looks at `draft.Findings`, so a draft whose key contradicts its paper is approvable. This is `IP-03`, and the distinction from `P-19` matters: a warning is cleared by a person who records a reason, and an error has no override at all, because there is no judgement to exercise when two documents contradict each other.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task A_draft_with_a_blocking_finding_cannot_be_approved()
{
    var draft = DraftWith(findings: [
        new PackageFinding("error", PaperKeyConsistency.WordLimitCode, "/q/1", "over the limit")]);
    await drafts.SaveAsync(draft, default);

    var result = await workflow.ApproveAsync(draft.Id, draft.Revision, Reviewer, default);

    Assert.False(result.IsSuccess);
    Assert.Equal("IMPORT_FINDINGS_BLOCKING", result.ErrorCode);
}

/// <summary>
/// Warnings stay clearable — that is `P-19`, and the transcript case depends
/// on it. Only errors are absolute.
/// </summary>
[Fact]
public async Task A_warning_severity_finding_does_not_block_approval()
{
    var draft = DraftWith(findings: [
        new PackageFinding("warning", PassageAnchorCheck.OutOfOrderCode, "/q/2", "out of order")]);
    await drafts.SaveAsync(draft, default);

    var result = await workflow.ApproveAsync(draft.Id, draft.Revision, Reviewer, default);

    Assert.True(result.IsSuccess);
}

/// <summary>
/// There is no override. A reviewer with every permission still cannot clear
/// a contradiction between a paper and its key — the fix is a corrected file,
/// not a recorded reason.
/// </summary>
[Fact]
public async Task Resolving_every_warning_does_not_clear_a_blocking_finding()
{
    var draft = DraftWith(findings: [
        new PackageFinding("error", PassageAnchorCheck.NotInPassageCode, "/q/1", "absent")]);
    await drafts.SaveAsync(draft, default);

    var result = await workflow.ApproveAsync(draft.Id, draft.Revision, Reviewer, default);

    Assert.Equal("IMPORT_FINDINGS_BLOCKING", result.ErrorCode);
}
```

Match the fixture helpers already in this test file. If `DraftWith` does not exist, add one that builds an `ExamImportDraft` with the given findings, `ApprovalState.ReviewRequired`, no unresolved warnings and a complete checklist, so the new refusal is the only thing under test.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~ImportReviewWorkflowTests"`
Expected: FAIL — the first test approves successfully, because nothing reads `draft.Findings`.

- [ ] **Step 3: Add the gate**

In `ApproveAsync`, after the revision check and before the warning check:

```csharp
/*
 * A blocking finding has no override, and that is the difference from a
 * warning. `P-19`'s warnings are judgements a reviewer may make with a
 * recorded reason. An error here is two documents in the same package
 * contradicting each other — a key answer the passage does not contain, an
 * answer over the paper's own word limit — and no amount of authority makes
 * those consistent. The fix is a corrected file. → IP-03
 */
if (draft.Findings.Any(f => f.Severity == "error"))
    return ImportReviewResult.Refused("IMPORT_FINDINGS_BLOCKING");
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/tests/Vni.Ielts.Application.Tests --nologo --filter "FullyQualifiedName~ImportReviewWorkflowTests"`
Expected: PASS, including every pre-existing case in the file.

- [ ] **Step 5: Verify the gate goes red when removed**

Delete the three added lines, re-run, confirm `A_draft_with_a_blocking_finding_cannot_be_approved` fails with a successful approval. Restore.

- [ ] **Step 6: Run the whole backend suite**

Run: `dotnet test backend/Vni.Ielts.sln --nologo`
Expected: every project passes except the three pre-existing integration failures named in Global Constraints. If any other test fails, it is yours.

- [ ] **Step 7: Regenerate the contract and check drift**

Run: `pnpm run generate:api-client && node scripts/check-generated-drift.mjs --mode=all`
Expected: `OK — no generated-artifact drift`. No endpoint changed in this plan, so the contract should be untouched; a diff here means something leaked into a response type and needs explaining.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Vni.Ielts.Application/Importing/ImportReviewWorkflow.cs \
        backend/tests/Vni.Ielts.Application.Tests/Importing/ImportReviewWorkflowTests.cs
git commit -m "feat(import): refuse to approve a draft whose key contradicts its paper"
```

---

### Task 7: Record what was built

**Files:**
- Modify: `docs/ai/writing-marking.md` — no change; listed only to say explicitly that it is not touched
- Modify: `CLAUDE.md` — the inventory paragraph under *Built and running*
- Modify: `docs/requirements/assumptions-and-open-questions.md` — nothing closes; listed for the same reason
- Create: `docs/development/answer-key-cross-check.md`

**Interfaces:**
- Consumes: everything the previous six tasks built.
- Produces: nothing code depends on.

**Why this is a task and not a footnote.** CLAUDE.md is the first thing every session reads, and its own rule says a canonical document that is wrong about the code is worse than no document. After this plan the inventory sentence about import is wrong in two ways: the HTTP door no longer sends keys to the model, and approval can now be refused for a reason that did not exist.

- [ ] **Step 1: Write the implementation note**

Create `docs/development/answer-key-cross-check.md` describing what runs, in the style of `docs/ai/writing-marking.md`: a table of the layers with their finding codes and severities, which question types each layer reaches, and the three residual cases from the spec's section A5 that no layer covers. State the date it was verified against the tree.

- [ ] **Step 2: Correct the CLAUDE.md inventory**

In the *Built and running* paragraph, the phrase describing the import door gains: the paper-and-key folder split, the key read by code and never sent to the model, layers 3 and 4, and the blocking-approval gate. Remove nothing that is still true.

- [ ] **Step 3: Run the docs gate**

Run: `node scripts/check-docs.mjs`
Expected: `All documentation checks passed.` One warning about a local secrets file is pre-existing and not blocking.

- [ ] **Step 4: Commit**

```bash
git add docs/development/answer-key-cross-check.md CLAUDE.md
git commit -m "docs: record the answer-key cross-check as built"
```

---

## What this plan deliberately leaves for the next one

Named here so nobody reads their absence as an oversight. All are in the spec, Part A.

| Left out | Why it is not here |
|---|---|
| The downloadable ZIP skeleton (`GET …/import/template`) | An endpoint, independent of the checking core, and useless until the layout it documents exists — which is what this plan builds |
| Explanations generated at import (`IP-05`) | Depends on this plan's pipeline shape and on the import becoming a background job. It is the owner's primary complaint and is the next plan's first task |
| Layer 5, the model's dispute promoted to its own finding code | Belongs with the explanation wiring, since the signal comes out of the same call |
| Import as a background job returning `202` | A parse plus forty explanations does not fit an HTTP request. Nothing in this plan calls a provider, so nothing here needs it yet |
| The CMS review panel: per-question anchors and the coverage statement | Consumes `AnchorReport.Anchors`, which Task 5 produces and nothing yet renders |
| Part B, Writing marking notes and model answers | `IP-04`: Reading and Listening first |
