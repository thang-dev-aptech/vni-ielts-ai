using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Infrastructure.Content.Import;

/// <summary>
/// Glues the exam-import engine into one call an HTTP endpoint can make: ZIP
/// bytes in, a persisted review draft (or a refusal) out.
///
/// <b>Follows the sequence <c>backend/tools/Vni.Ielts.ExamImporter/Program.cs</c>
/// already proves — inspect, extract to a sandbox, structured-or-AI-parsed,
/// validate, save.</b> S6b's job was to give that existing sequence an HTTP
/// door, not to invent a new one. → docs/security/zip-ingestion-security.md
/// </summary>
public sealed class ExamPackageImportPipeline(
    IExamPackageArchiveInspector inspector,
    ISourceDocumentExtractor extractor,
    ExamImportWorkflow workflow,
    IExamPackageValidator validator,
    IImportDraftStore drafts,
    IOptions<ImportArchiveOptions> archiveOptions)
{
    /// <param name="zip">
    /// Must be seekable. Inspection reads the archive's central directory,
    /// which lives at the end of the file, and extraction re-reads the same
    /// bytes — <see cref="IExamPackageArchiveInspector"/>'s own remarks call
    /// this the reason a two-step usage needs the upload persisted once and
    /// opened twice. A buffered upload (ASP.NET Core spools a multipart file
    /// part to disk or memory before handing it back as an <c>IFormFile</c>)
    /// satisfies this without the caller doing anything extra; a raw,
    /// forward-only request body would not, and is rejected here rather than
    /// silently mis-reading a truncated archive.
    /// </param>
    /// <param name="progress">
    /// Where the import has got to, reported as it happens.
    ///
    /// <b>Optional and null by default, so no existing caller changes.</b> The
    /// operator CLI and the synchronous tests do not care; the import worker
    /// does, because a stage is how far the money went. A resumed job that
    /// cannot tell whether a paid parse already happened has to guess, and
    /// guessing wrong buys a second parse of a Cambridge paper. →
    /// <c>ImportJobStage</c>
    ///
    /// <b>Reported, never depended on.</b> This pipeline does not read the
    /// stage back and does not change behaviour when nobody is listening: a
    /// recipient that throws must not be able to fail an import that has
    /// already been paid for, so every call is guarded.
    /// </param>
    public async Task<ExamImportAttempt> ImportAsync(
        Stream zip, ExamDefinitionId definitionId, int versionNumber, CancellationToken ct,
        IProgress<ImportJobStage>? progress = null)
    {
        if (!zip.CanSeek)
        {
            throw new ArgumentException(
                "The archive stream must be seekable — inspection and extraction both read it in full. "
                + "Buffer the upload to a seekable stream before calling this.", nameof(zip));
        }

        Report(progress, ImportJobStage.Extracting);

        var limits = archiveOptions.Value.ToLimits();
        var inspection = await inspector.InspectAsync(zip, limits, ct);
        if (!inspection.IsAcceptable)
            return ExamImportAttempt.Rejected(inspection.Findings);

        zip.Position = 0;
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "vni-import", Guid.NewGuid().ToString("n"));
        var extraction = await inspector.ExtractToSandboxAsync(zip, inspection, sandboxRoot, limits, ct);

        try
        {
            if (!extraction.IsSuccess || extraction.SandboxDirectory is null)
                return ExamImportAttempt.Rejected(extraction.Findings);

            var attempt = await ImportFromSandboxAsync(
                inspection.Layout, extraction.SandboxDirectory, definitionId, versionNumber, ct,
                progress);

            return await AttachRoleFolderWarningsAsync(attempt, inspection.Findings, ct);
        }
        catch (ExamSourceParsingUnavailableException e)
        {
            return ExamImportAttempt.Rejected(
                [new PackageFinding("error", "AI_PARSER_UNAVAILABLE", "/", e.Message)]);
        }
        finally
        {
            // Best-effort. A stray temp directory is a disk-hygiene problem,
            // not a correctness one, and must never turn a completed import
            // into a failed response.
            if (Directory.Exists(sandboxRoot))
            {
                try { Directory.Delete(sandboxRoot, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>
    /// <b>Structured route:</b> the archive holds exactly one accepted file
    /// and it is JSON — a package already assembled by a human, or by the
    /// operator CLI, which already ran <see cref="FabricatedAnswerKeyGuard"/>
    /// itself before anyone would upload its output here. Running the guard
    /// again on a package the CLI has already keyed would flag every
    /// legitimate answer key it wrote, so this route deliberately does not
    /// call it — see <see cref="FabricatedWarnings"/> for
    /// where the guard actually runs.
    ///
    /// <b>AI-parsed route:</b> every other shape. <c>P-18</c>: the folder name
    /// alone decides the skill, and a missing folder is simply not present —
    /// <see cref="PackageLayout.PresentSkills"/> already encodes exactly that.
    /// <b>Only the paper is concatenated and sent.</b> Every file whose role is
    /// <see cref="PackageEntryRole.Paper"/> is extracted under a heading per
    /// file; a key document is never part of that text. Until 2026-09-10 the
    /// loop read <c>.All</c>, so a key dropped into <c>reading/</c> went
    /// straight into the prompt — the configuration measured on 2026-09-02,
    /// where a model shown a paper with no key produced forty answers, five of
    /// them wrong, and every one passed schema validation. The key is read by
    /// <see cref="AnswerKeyDocument"/> in <see cref="ApplyKeysAndGuardAsync"/>
    /// and written on afterwards. → CLAUDE.md rule 9
    /// </summary>
    private async Task<ExamImportAttempt> ImportFromSandboxAsync(
        PackageLayout layout, string sandboxDirectory, ExamDefinitionId definitionId, int versionNumber,
        CancellationToken ct, IProgress<ImportJobStage>? progress)
    {
        var allEntries = layout.AcceptedEntries.ToArray();

        if (allEntries.Length == 1 && allEntries[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var packageJson = await File.ReadAllTextAsync(Path.Combine(sandboxDirectory, allEntries[0]), ct);

            // <b>Reported even though no parser runs.</b> The structured route
            // reads a package somebody already assembled, so `Parsing` here
            // costs nothing — but a job that jumped from `Extracting` to
            // `Done` would leave an operator watching the CMS with no idea
            // which route their upload took.
            Report(progress, ImportJobStage.Parsing);

            return await workflow.ImportStructuredAsync(packageJson, definitionId, versionNumber, ct);
        }

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

        // Past here the parser is called, and a Cambridge parse is the single
        // most expensive thing this pipeline does.
        Report(progress, ImportJobStage.Parsing);

        var text = combined.ToString();
        var hash = ExamImportWorkflow.Hash(text);
        var source = new ExtractedImportSource(
            "package", "text/plain", text, hash, hash, ImportDataClassification.Restricted);

        var attempt = await workflow.ImportExtractedAsync(source, definitionId, versionNumber, ct);
        if (!attempt.IsAccepted || attempt.Draft is null) return attempt;

        return await ApplyKeysAndGuardAsync(attempt.Draft, layout, sandboxDirectory, ct, progress);
    }

    /// <summary>
    /// Reads every key document, one skill at a time, writes it onto the parsed
    /// package, and turns the model's own answers loose on the fabrication
    /// guard wherever no key was supplied.
    ///
    /// <b>The model is never shown any of this.</b> Measured on VOL 9: with the
    /// key in the prompt a model scored 36 of 38 and both misses were alignment
    /// failures — the right answer on the wrong question. With the key read by
    /// code the same paper scored 34 of 38 exactly, and refused on the other
    /// four rather than guessing. Counting is what code does better.
    ///
    /// <b>The choice is per skill, never per package.</b> A package can carry
    /// <c>reading/dap-an/</c> and no Listening key at all, and a package-wide
    /// "a key was supplied" flag then sends the whole import down the keyed
    /// route: Listening gets neither a real key nor the guard, and the model's
    /// invented Listening answers are persisted with nothing said about them —
    /// strictly worse than having no key folder anywhere, which at least always
    /// flagged them. Which skill has a key folder is a fact about the
    /// <b>layout</b>; it is never inferred from the package, because inferring
    /// it from the presence of answer keys is exactly the vacuous check
    /// <see cref="FabricatedAnswerKeyGuard"/>'s own remarks warn against.
    /// </summary>
    private async Task<ExamImportAttempt> ApplyKeysAndGuardAsync(
        ExamImportDraft draft, PackageLayout layout, string sandboxDirectory, CancellationToken ct,
        IProgress<ImportJobStage>? progress = null)
    {
        Report(progress, ImportJobStage.Keying);

        var json = draft.PackageJson;
        var findings = new List<PackageFinding>();
        var injectionWarnings = new List<PackageFinding>();
        var keyed = new HashSet<ExamModule>();
        var changed = false;

        foreach (var skill in layout.PresentSkills)
        {
            var keyFiles = layout.For(skill).Key;
            if (keyFiles.Count == 0) continue;

            keyed.Add(skill);

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
                    "error", "ANSWER_KEY_UNREADABLE", $"/sections/{Name(skill)}",
                    $"The {skill} key folder holds no answers this reader recognises. "
                    + "Two formats are read: numbered lines, and a bare ordered list."));

                /*
                 * <b>The model's answers go with it.</b> A key folder was
                 * supplied, so whatever the model wrote was never meant to
                 * stand — and nothing readable arrived to replace it. Leaving
                 * the guesses persists the 2026-09-02 failure to disk: forty
                 * well-formed, unverifiable answers on a draft a reviewer sees
                 * as finished. The package may now be schema-incomplete for
                 * these questions, because an auto-scored question is supposed
                 * to carry a key; that is the safer half of the trade, since
                 * the error finding above blocks approval either way and a
                 * blocked draft holding no answers beats one holding invented
                 * ones.
                 */
                json = StripModelAnswers(json, skill);
                changed = true;
                continue;
            }

            // A key is about to be written over every question, so a guess the
            // model left behind is worthless. Strip first, key second.
            json = StripModelAnswers(json, skill);

            var result = AnswerKeyInjection.Apply(json, entries, skill);
            json = result.PackageJson;

            /*
             * <b>An injection warning is a judgement, so it goes where
             * judgements are made.</b> `AnswerKeyInjection` reports six things
             * at severity "warning" — a question retyped to match its group
             * (ANSWER_KEY_TYPE_RETYPED), sibling choice questions folded into
             * one (ANSWER_KEY_FOLDED_CHOICE), a rubric letter added as an
             * option (ANSWER_KEY_OPTION_ADDED), a bank label accepted
             * alongside its word (ANSWER_KEY_BANK_LABEL_ALTERNATIVES), and a
             * question whose printed options contradicted its declared type,
             * whose answer was reinterpreted as an option key
             * (ANSWER_KEY_TYPE_MISMATCH at warning severity).
             *
             * Every one of those says the code changed the paper's own
             * description of a question in order to make the key fit. Filed on
             * `draft.Findings` they fall between both gates:
             * `ImportReviewWorkflow.ApproveAsync` blocks only on
             * `Severity == "error"`, and `ResolveWarningAsync` operates only
             * on `draft.Warnings` — so they never block, can never be cleared,
             * and are never audited. Before this branch they were unreachable
             * from the HTTP door (the pipeline did not call
             * `AnswerKeyInjection` at all), so this is the branch that makes
             * them matter.
             *
             * They become `ImportReviewWarning`s instead — the `P-19` shape:
             * approval is refused until a reviewer clears each one with a
             * recorded reason, audited as `WarningOverridden`. Error-severity
             * findings from the same call keep going to `draft.Findings`
             * unchanged: those are contradictions between two documents, and
             * no amount of reviewer authority makes them consistent.
             */
            foreach (var finding in result.Findings)
            {
                if (finding.Severity == "warning") injectionWarnings.Add(finding);
                else findings.Add(finding);
            }

            changed = true;
        }

        /*
         * <b>Layer 3 of the cross-check, run on every route.</b> A package with
         * no key folder gets checked too, deliberately: its answers were
         * written by the model, and a fabricated answer that contradicts its
         * own question type or exceeds the paper's stated word limit is caught
         * by exactly these checks. The fabrication warning above says a key
         * was invented; these findings say which of the invented answers are
         * impossible. → PaperKeyConsistency
         */
        Report(progress, ImportJobStage.Checking);

        findings.AddRange(PaperKeyConsistency.Inspect(json));

        /*
         * <b>Layer 4 of the cross-check, run on every route.</b> Counting and
         * shape checks accept a legal answer on the wrong question; this one
         * catches it by searching for the answer in the text it must have
         * come from, and by checking that a group's answers appear in the
         * passage in question order. → PassageAnchorCheck
         *
         * <b>Only 4c stays a PackageFinding.</b> A whole group anchoring
         * nothing against a passage that is present is a clean, strong signal
         * the paper was parsed wrong, and it is the one case where reporting
         * the individual misses would send an administrator to fix twelve
         * answers that are all correct. It has no override, like layers 1–3.
         *
         * 4b (order) and, since 2026-09-10, 4a (containment) are
         * ImportReviewWarnings: both still block approval, and both can be
         * cleared with a recorded, audited reason (P-19) — the same shape
         * FabricatedWarnings above already uses. 4a moved because its premise
         * turned out to be false: "the answer is not in the passage" depends
         * on normalisation choices that do not round-trip ("some 800km"
         * against a passage printing "800 km"; "35,000" against a passage
         * keeping the comma, since numeric equivalence applies to a short
         * answer and not to a whole passage), so a genuinely absent answer
         * cannot be told apart from one the matcher cannot see.
         *
         * Neither may be filed as a "warning"-severity PackageFinding — as
         * this line once did for 4b — because that falls between both gates:
         * ApproveAsync's blocking check reads only Severity == "error", and
         * there is no resolve path for a finding at all.
         * → docs/development/answer-key-cross-check.md
         */
        var anchorReport = PassageAnchorCheck.Inspect(json);
        findings.AddRange(anchorReport.Findings);
        IReadOnlyList<ImportReviewWarning> warnings =
        [
            .. FabricatedWarnings(json, keyed),
            .. InjectionWarnings(injectionWarnings),
            .. NotInPassageWarnings(anchorReport.MissingAnswerIssues),
            .. OrderWarnings(anchorReport.OrderIssues),
        ];

        if (!changed && warnings.Count == 0 && findings.Count == 0) return ExamImportAttempt.Accepted(draft);

        /*
         * `Version` was materialised by the validator from the *parser's*
         * output, so leaving it alone here would put the model's guess and the
         * supplier's key in the same record: `PackageJson` saying TRUE and
         * `Version` saying FALSE, two different answers to "what is correct for
         * question 1". Nothing publishes from `Version` today, but
         * `AdminImportEndpoints` already reads it, and something well-formed and
         * wrong sitting where a consumer will eventually find it is precisely
         * how 2026-09-02 happened.
         */
        var version = draft.Version;
        if (changed)
        {
            var revalidated = Revalidate(json, draft);

            if (revalidated is not null) version = revalidated;
            else
                findings.Add(new PackageFinding(
                    "error", RevalidationFailedCode, "/sections",
                    "The package no longer validates once the supplied key was applied, so the "
                    + "materialised exam version still reflects what the model produced. Read the "
                    + "package itself, not the version, until this is resolved. An unreadable key "
                    + "folder reaches this state on purpose: its questions are left with no answer "
                    + "at all, which the schema refuses, and no answer is safer than an invented one."));
        }

        var updated = draft with
        {
            PackageJson = json,
            PackageHash = ExamImportWorkflow.Hash(json),
            Version = version,
            Findings = [.. draft.Findings, .. findings],
            Warnings = [.. draft.Warnings, .. warnings],
            Revision = draft.Revision + 1,
        };

        var replaced = await drafts.ReplaceAsync(updated, draft.Revision, ct);
        return ExamImportAttempt.Accepted(replaced ? updated : draft);
    }

    public const string RevalidationFailedCode = "ANSWER_KEY_REVALIDATION_FAILED";

    /// <summary>
    /// Tells the caller where the import has got to, and never lets that
    /// telling break the import.
    ///
    /// <b>Swallowed on purpose.</b> The recipient is a worker writing a row to
    /// a database; a blip there must not throw out of a pipeline that has
    /// already spent money on a parse. The worker's own bookkeeping is what
    /// notices a lost stage — this call is a report, not a transaction.
    /// </summary>
    private static void Report(IProgress<ImportJobStage>? progress, ImportJobStage stage)
    {
        try
        {
            progress?.Report(stage);
        }
        catch (Exception)
        {
            // Deliberately nothing: see above.
        }
    }

    /// <summary>
    /// Carries <see cref="ArchiveFindingCodes.LayoutUnknownRoleFolder"/> — and
    /// nothing else from the inspection — onto the saved draft, as a blocking
    /// but clearable review warning.
    ///
    /// <b>Why this one finding and not the rest of the inspection.</b> On an
    /// acceptable inspection every other finding is either a refusal (the
    /// package never got here) or <c>LAYOUT_UNKNOWN_ENTRY</c>, which says a
    /// top-level folder or a root file was <i>ignored</i>. <c>__MACOSX/</c> is
    /// that case; routing it here would put an unresolved warning on every
    /// package a macOS ZIP tool wrote, and a check that blocks correct
    /// packages is a check somebody switches off. An unrecognised <i>role</i>
    /// folder is the opposite: the file was <b>used</b>, as paper, so a key
    /// folder spelled <c>dap_an</c> put the answer key in front of the model.
    /// The administrator who typed it needs to learn that, and the draft must
    /// not be approvable until they have.
    ///
    /// <b>Placed here rather than inside <see cref="ApplyKeysAndGuardAsync"/>
    /// on purpose.</b> The structured route (one accepted <c>.json</c> entry)
    /// never reaches that method, and <c>reading/dap_an/exam.json</c> is a
    /// package that takes it. One call site covers both routes; the cost is
    /// one extra draft revision on a package that has the problem, and none
    /// on a package that does not.
    /// </summary>
    private async Task<ExamImportAttempt> AttachRoleFolderWarningsAsync(
        ExamImportAttempt attempt, IReadOnlyList<PackageFinding> inspectionFindings, CancellationToken ct)
    {
        if (!attempt.IsAccepted || attempt.Draft is null) return attempt;

        var warnings = inspectionFindings
            .Where(f => f.Code == ArchiveFindingCodes.LayoutUnknownRoleFolder)
            .Select((f, i) => new ImportReviewWarning(
                $"{f.Code}:{i}", ImportReviewCategory.AcceptedVariants, f.Path, f.Message, false))
            .ToArray();

        if (warnings.Length == 0) return attempt;

        var draft = attempt.Draft;
        var updated = draft with
        {
            Warnings = [.. draft.Warnings, .. warnings],
            Revision = draft.Revision + 1,
        };

        var replaced = await drafts.ReplaceAsync(updated, draft.Revision, ct);
        return ExamImportAttempt.Accepted(replaced ? updated : draft);
    }

    /// <summary>
    /// The materialised version for a package the key has just been written
    /// onto, or <c>null</c> when it no longer validates.
    ///
    /// <b>Never throws.</b> A refusal here is a finding, not an exception: the
    /// draft is already on disk and the whole point of reaching this line is
    /// that the package changed after it was saved.
    /// </summary>
    private ExamVersion? Revalidate(string packageJson, ExamImportDraft draft)
    {
        try
        {
            var result = validator.Validate(packageJson, draft.DefinitionId, draft.VersionNumber);
            return result.IsValid ? result.Version : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// The fabrication guard, run only over the skills nobody supplied a key
    /// for.
    ///
    /// <b>Scoped at the call site, not inside the guard.</b>
    /// <see cref="FabricatedAnswerKeyGuard.Inspect"/> is a whole-package
    /// question and stays one; what changes per skill is whether its answer
    /// means anything, and the finding path already names the section it came
    /// from. A section whose module is not in <paramref name="keyed"/> is
    /// guarded — including a module the model invented that the layout never
    /// carried a folder for, which is the case a "present skills" loop alone
    /// would walk straight past.
    ///
    /// <b>Every finding becomes a review warning, not a silent rejection.</b>
    /// That reuses exactly the "cảnh báo, bắt buộc ghi lý do khi bỏ qua" shape
    /// `P-19` already requires for every other import warning: a fabricated
    /// answer key blocks approval (<see cref="ImportReviewWorkflow.ApproveAsync"/>
    /// refuses while any warning is unresolved) until a reviewer overrides it
    /// with a reason, and that override is what
    /// <c>AuditAction.WarningOverridden</c> records. A draft that is on disk
    /// with the problem named and gated is more useful to an operator than one
    /// silently discarded after a paid AI call — the same judgement
    /// <see cref="FabricatedAnswerKeyGuard"/>'s own remarks make about the CLI
    /// path ("the draft is on disk for inspection"). It also runs after the
    /// draft is saved rather than before, because the parser runs exactly once
    /// inside <see cref="ExamImportWorkflow.ImportExtractedAsync"/> and calling
    /// it again to check its output would double the AI cost of every upload
    /// and — the model not being deterministic — could check different output
    /// from what was actually saved.
    /// </summary>
    private static IReadOnlyList<ImportReviewWarning> FabricatedWarnings(
        string packageJson, IReadOnlySet<ExamModule> keyed)
    {
        var unkeyed = UnkeyedSectionPrefixes(packageJson, keyed);
        if (unkeyed.Count == 0) return [];

        return FabricatedAnswerKeyGuard
            .Inspect(packageJson, sourceIncludesAnswerKey: false)
            .Where(f => f.Path.EndsWith("/answerKey", StringComparison.Ordinal))
            .Where(f => unkeyed.Any(prefix => f.Path.StartsWith(prefix, StringComparison.Ordinal)))
            .Select((f, i) => new ImportReviewWarning(
                $"FABRICATED_ANSWER_KEY:{i}", ImportReviewCategory.AcceptedVariants, f.Path, f.Message, false))
            .ToArray();
    }

    /// <summary>
    /// Turns the warning-severity findings <see cref="AnswerKeyInjection.Apply"/>
    /// returns into review warnings a reviewer must actually clear, using the
    /// same <c>CODE:index</c> id idiom <see cref="OrderWarnings"/> uses. The
    /// index is per call rather than per code so that two occurrences of the
    /// same code get two ids: resolving one must never clear the other, since
    /// each names a different question.
    /// </summary>
    private static IReadOnlyList<ImportReviewWarning> InjectionWarnings(
        IReadOnlyList<PackageFinding> warnings) =>
        warnings
            .Select((f, i) => new ImportReviewWarning(
                $"{f.Code}:{i}", ImportReviewCategory.AcceptedVariants, f.Path, f.Message, false))
            .ToArray();

    /// <summary>
    /// Turns <see cref="PassageAnchorCheck"/>'s Layer 4a result — a key answer
    /// not found in its own passage or transcript — into a blocking but
    /// clearable review warning, the same idiom
    /// <see cref="OrderWarnings"/> uses. See
    /// <see cref="AnchorMissingAnswerIssue"/> for why it is no longer an
    /// error with no override.
    /// </summary>
    private static IReadOnlyList<ImportReviewWarning> NotInPassageWarnings(
        IReadOnlyList<AnchorMissingAnswerIssue> issues) =>
        issues
            .Select((issue, i) => new ImportReviewWarning(
                $"{PassageAnchorCheck.NotInPassageCode}:{i}", ImportReviewCategory.AcceptedVariants,
                issue.Path, issue.Message, false))
            .ToArray();

    /// <summary>
    /// Turns <see cref="PassageAnchorCheck"/>'s Layer 4b result — a group
    /// whose answers stop following the passage's order — into the same
    /// review-warning shape <see cref="FabricatedWarnings"/> already uses,
    /// rather than a <see cref="PackageFinding"/>. The stable id follows the
    /// same <c>CODE:index</c> idiom so <c>ResolveWarningAsync</c> has
    /// something to key on.
    /// </summary>
    private static IReadOnlyList<ImportReviewWarning> OrderWarnings(
        IReadOnlyList<AnchorOrderIssue> issues) =>
        issues
            .Select((issue, i) => new ImportReviewWarning(
                $"{PassageAnchorCheck.OutOfOrderCode}:{i}", ImportReviewCategory.AcceptedVariants,
                issue.Path, issue.Message, false))
            .ToArray();

    /// <summary>
    /// <c>/sections/{index}/</c> for every section no key was supplied for.
    /// The guard reports positions, not modules, so the mapping has to be made
    /// here from the package's own section order.
    /// </summary>
    private static IReadOnlyList<string> UnkeyedSectionPrefixes(
        string packageJson, IReadOnlySet<ExamModule> keyed)
    {
        var wanted = keyed.Select(Name).ToHashSet(StringComparer.Ordinal);
        var prefixes = new List<string>();

        JsonArray? sections;
        try
        {
            sections = JsonNode.Parse(packageJson)?["sections"]?.AsArray();
        }
        catch (JsonException)
        {
            // The validator reports malformed JSON; this is not that job.
            return prefixes;
        }

        if (sections is null) return prefixes;

        for (var i = 0; i < sections.Count; i++)
        {
            var module = sections[i]?["module"]?.GetValue<string>();
            if (module is not null && wanted.Contains(module)) continue;
            prefixes.Add($"/sections/{i}/");
        }

        return prefixes;
    }

    /// <summary>
    /// The module name as the schema and every neighbouring path spell it.
    /// </summary>
    private static string Name(ExamModule module) => module.ToString().ToLowerInvariant();

    /// <summary>
    /// Removes every answer the model wrote for <b>one skill</b>, leaving the
    /// other skills' questions exactly as they were.
    ///
    /// <b>Scoped, where <see cref="FabricatedAnswerKeyGuard.Strip"/> is not.</b>
    /// That one clears the whole package, which is what the operator CLI wants
    /// because it runs a single skill per invocation. The HTTP door does not:
    /// a package can carry a readable Reading key and an unreadable Listening
    /// one, and the loop visits them in turn — an unscoped strip on the second
    /// pass would delete the answers the first pass had just written from
    /// Reading's own key, silently. Reading and Listening also both number 1
    /// to 40, so the skill has to be named here for the same reason
    /// <see cref="AnswerKeyInjection.Apply"/> takes a module.
    /// </summary>
    private static string StripModelAnswers(string packageJson, ExamModule module)
    {
        var package = JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new ArgumentException(
                "The package did not parse as an object.", nameof(packageJson));

        var wanted = module.ToString().ToLowerInvariant();

        foreach (var section in package["sections"]?.AsArray() ?? [])
        {
            if (section?["module"]?.GetValue<string>() != wanted) continue;

            foreach (var part in section["parts"]?.AsArray() ?? [])
            foreach (var question in part?["questions"]?.AsArray() ?? [])
                (question as JsonObject)?.Remove("answerKey");
        }

        return package.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
