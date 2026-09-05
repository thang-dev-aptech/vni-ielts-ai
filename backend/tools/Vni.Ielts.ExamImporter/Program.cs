using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Ai;
using Vni.Ielts.Infrastructure.Ai.Importing;
using Vni.Ielts.Infrastructure.Content;

/*
 * ── AI-assisted exam import, driven from a terminal ───────────────────────
 *
 * <b>A tool rather than an endpoint, and the choice is about who is allowed to
 * do this.</b> Importing a paper is an administrative act on a catalogue that
 * learners read; the CMS route for it is reviewed and permissioned and does not
 * exist yet. A command that an operator runs against files already on their own
 * machine has the same effect without inventing an unreviewed HTTP surface that
 * would then need taking away.
 *
 * <b>It never publishes.</b> It writes a package JSON to disk and prints what
 * the validator said about it. Whether that package reaches a learner is
 * `import-exam-package.py` plus a person, exactly as it was before.
 *
 * usage:
 *   dotnet run --project backend/tools/Vni.Ielts.ExamImporter --
 *     --source "<absolute path to a .docx or .pdf>"
 *     --out    "<package.json to write>"
 *     [--rights-cleared]     mark the source as rights-cleared (see below)
 *     [--sandbox <dir>]      defaults to the source file's own directory
 *
 * `--rights-cleared` is the switch that says somebody holds the rights to send
 * this document to a third-party AI provider. Without it the source stays
 * `Restricted` and the parser refuses — which is the correct default for a file
 * that arrived from outside.
 */

var parsed = Args.Parse(args);
if (parsed is null) return 2;

var repositoryRoot = FindRepositoryRoot();
if (repositoryRoot is null)
{
    Console.Error.WriteLine("Could not locate the repository root (no contracts/schemas above the tool).");
    return 2;
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(
        Path.Combine(repositoryRoot, "backend", "src", "Vni.Ielts.Api", "secrets.develop.json"),
        optional: true,
        reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();

/*
 * <b>Ten minutes, because `HttpClient`'s default hundred seconds is not enough
 * and fails in the most misleading way available.</b> The first run of this
 * tool logged `200` twice and then reported a timeout: the headers arrive
 * quickly and the model then streams a whole exam paper, so `SendAsync` — which
 * does not return until the body is read — was still going when the clock ran
 * out. The provider had done the work and been paid for it both times.
 *
 * The ceiling is here rather than in the adapter because it is a property of
 * how long an operator is willing to sit in front of a terminal, not of the
 * protocol.
 */
/*
 * ResponseHeadersRead means HttpClient.Timeout alone does not reliably cancel
 * a stuck SSE body — Cam 17 Test 3 Reading sat open for >3 hours on
 * deepseek-v4-pro (2026-09-03). The adapter also wraps the stream read in its
 * own linked deadline; this ceiling is the operator's outer stop.
 */
services.AddHttpClient(nameof(OpenAiStructuredExamClient))
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(20));
services.AddLogging(builder => builder.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));
services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
services.AddSingleton(new ExamParsePromptSources(repositoryRoot, parsed.Template));
services.AddSingleton<IPrivateImportAssetStore, DiscardedImportAssetStore>();
services.AddSingleton<ISourceDocumentExtractor, SafeSourceDocumentExtractor>();
services.AddSingleton<IStructuredExamAiClient, OpenAiStructuredExamClient>();
services.AddSingleton<IAiImportCostMetric, NullAiImportCostMetric>();

using var provider = services.BuildServiceProvider();

var ai = provider.GetRequiredService<IOptions<AiOptions>>().Value;
if (!ai.OpenAi.IsConfigured)
{
    Console.Error.WriteLine(
        "Ai:OpenAi:ApiKey is not set. Fill backend/src/Vni.Ielts.Api/secrets.develop.json, "
        + "or export Ai__OpenAi__ApiKey.");
    return 2;
}

/*
 * ── `--package`: re-apply a key without paying for the paper again ────────
 *
 * A parse costs real money and takes a minute; an answer key is read locally in
 * milliseconds. Separating them means a key that failed to line up can be fixed
 * and retried against the draft already on disk, which is the loop this tool is
 * actually used in. It is also the only way to verify the key path at all once
 * the provider account runs out of credit — which it did on 2026-09-03, mid-batch.
 */
if (parsed.Package is { } existingPackage)
{
    return await ApplyKeyOnlyAsync(existingPackage, parsed, repositoryRoot, provider);
}

var extractor = provider.GetRequiredService<ISourceDocumentExtractor>();
var sandbox = parsed.Sandbox ?? Path.GetDirectoryName(Path.GetFullPath(parsed.Source))!;
var relative = Path.GetRelativePath(sandbox, Path.GetFullPath(parsed.Source));

Console.WriteLine($"source     {relative}");
Console.WriteLine($"sandbox    {sandbox}");

var extraction = await extractor.ExtractAsync(
    sandbox, relative, SourceExtractionLimits.Operator, CancellationToken.None);

if (!extraction.IsSuccess || extraction.Source is null)
{
    Report("extraction refused", extraction.Findings);
    return 1;
}

/*
 * ── The key document, and why it is a separate argument ───────────────────
 *
 * <b>VOL 9 keeps the paper and its answer key in two files, and the first run
 * of this tool was given only the first one.</b> The model produced forty
 * answers anyway, five of them wrong, and every one passed validation. So the
 * key is now something the operator hands over explicitly, and
 * `FabricatedAnswerKeyGuard` refuses the package when they did not.
 *
 * The two texts are concatenated under headings rather than sent as two calls:
 * a key like "24-26. A, B, D" only means anything beside the question it
 * answers, and a model asked to align them across two requests has to remember
 * the first, which is the thing it is worst at.
 */
var sourceText = extraction.Source.Text;
string? keyText = null;

if (parsed.Key is { } keyPath)
{
    var keyRelative = Path.GetRelativePath(sandbox, Path.GetFullPath(keyPath));
    var keyExtraction = await extractor.ExtractAsync(
        sandbox, keyRelative, SourceExtractionLimits.Operator, CancellationToken.None);

    if (!keyExtraction.IsSuccess || keyExtraction.Source is null)
    {
        Report("key extraction refused", keyExtraction.Findings);
        return 1;
    }

    keyText = keyExtraction.Source.Text;
    Console.WriteLine($"key        {keyRelative} ({keyText.Length:N0} characters)");
}
else
{
    Console.WriteLine("key        none supplied — the paper will have no answers");
}

/*
 * ── The model transcribes the paper. It never sees the key ────────────────
 *
 * An earlier version concatenated the two and asked for both at once. Measured
 * on VOL 9 Test 2: 36 of 38 answers right — Q13 and Q36 wrong, both alignment
 * rather than reading. Two wrong answers in forty moves a learner's band.
 *
 * So the key is read by `AnswerKeyDocument` and written on by
 * `AnswerKeyInjection`, after this call and outside it. That also keeps
 * `FabricatedAnswerKeyGuard` armed: the source carries no key, so any
 * `answerKey` in the model's output is a guess and is refused.
 */
var source = extraction.Source with
{
    Text = sourceText,
    TextSha256 = ExamImportWorkflow.Hash(sourceText),
    Classification = parsed.RightsCleared
        ? ImportDataClassification.RightsCleared
        : ImportDataClassification.Restricted,
};

Console.WriteLine($"extracted  {source.Text.Length:N0} characters, classification {source.Classification}");

var parser = new ProviderNeutralExamSourceParser(
    [provider.GetRequiredService<IStructuredExamAiClient>()],
    provider.GetRequiredService<IAiImportCostMetric>(),
    new ExamParserOptions("OpenAi", ExamSourceParsePrompt.Version, MaxAttempts: 3));

ParsedExamPackage package;
try
{
    package = await parser.ParseAsync(source, CancellationToken.None);
}
catch (Exception e) when (e is InvalidOperationException or TransientAiParserException)
{
    Console.Error.WriteLine($"parse refused: {e.Message}");
    return 1;
}

Console.WriteLine(
    $"parsed     {package.PackageJson.Length:N0} characters by {package.Metadata.Provider}/"
    + $"{package.Metadata.Model}, request {package.Metadata.RequestId}");

/*
 * ── The envelope is assembled here, not asked for ─────────────────────────
 *
 * Timing rules and a raw-to-band table are equated per exam version and attach
 * to it as data (CLAUDE.md rule 4). A model reproducing a band table can round
 * a boundary, and a rounded boundary is a wrong band for every learner sitting
 * near it — an error that is invisible in the JSON and visible only in someone's
 * result. So the model is asked for `sections` and the rest is copied from a
 * template package that a person already accepted.
 */
string packageJson;
try
{
    packageJson = ExamPackageEnvelope.Wrap(
        package.PackageJson,
        await File.ReadAllTextAsync(parsed.Template ?? DefaultTemplate(repositoryRoot)),
        parsed.Title);
}
catch (Exception e) when (e is InvalidOperationException or System.Text.Json.JsonException)
{
    await File.WriteAllTextAsync(parsed.Out, package.PackageJson);
    Console.Error.WriteLine($"the model's output could not be assembled into a package: {e.Message}");
    Console.Error.WriteLine($"raw output written to {parsed.Out}");
    return 1;
}

package = package with { PackageJson = packageJson };

if (parsed.KeepModule is { } keep)
{
    /*
     * The operator named the module; the model guessed it. A VOL 9 Listening
     * paper carries no "LISTENING" heading and came back as a lone `reading`
     * section on every test (T2–T5, 2026-09-04), so `--keep-module listening`
     * found nothing to keep and the paid transcription was refused. When the
     * output has exactly one section, its module is the one the operator
     * asked for; relabel it and say so. Two or more sections are left alone —
     * that is a real ambiguity.
     */
    var relabelled = RelabelLoneSection(package.PackageJson, keep);
    if (relabelled is not null)
    {
        package = package with { PackageJson = relabelled };
        Console.Error.WriteLine($"module     the model's only section was relabelled to {keep} (--keep-module)");
    }

    try
    {
        packageJson = ExamPackageEnvelope.KeepModule(package.PackageJson, keep);
        package = package with { PackageJson = packageJson };
        Console.WriteLine($"kept       {keep} section only");
    }
    catch (InvalidOperationException e)
    {
        await File.WriteAllTextAsync(parsed.Out, package.PackageJson);
        Console.Error.WriteLine($"keep-module refused: {e.Message}");
        Console.Error.WriteLine($"raw output written to {parsed.Out}");
        return 1;
    }
}

/*
 * <b>Written before it is judged, and written whether or not it passes.</b> A
 * package the validator rejects is the most useful artefact this tool produces:
 * it is what a person edits. Throwing it away on a failed validation would mean
 * paying the provider again to see the same output.
 */
await File.WriteAllTextAsync(parsed.Out, package.PackageJson);
Console.WriteLine($"written    {parsed.Out}");

// Before the guard and the key: a Writing task filed inside Reading carries
// questions numbered 1 and 2, which the key would otherwise be written onto.
package = package with { PackageJson = DropForeignParts(package.PackageJson) };
await File.WriteAllTextAsync(parsed.Out, package.PackageJson);

/*
 * With an official key in hand, a model that solved the paper anyway has
 * produced guesses that the key is about to overwrite. Strip them and say so;
 * the guard below still refuses when there is no key to overwrite them with
 * (VOL 9 T3, 2026-09-04: 40 model answers under prompt v7).
 */
if (keyText is not null)
{
    var (stripped, removed) = FabricatedAnswerKeyGuard.Strip(package.PackageJson);
    if (removed > 0)
    {
        package = package with { PackageJson = stripped };
        await File.WriteAllTextAsync(parsed.Out, package.PackageJson);
        Console.Error.WriteLine(
            $"model answers  {removed} answerKey(s) the model wrote were removed; the key document is applied instead");
    }
}

var fabricated = FabricatedAnswerKeyGuard.Inspect(package.PackageJson, sourceIncludesAnswerKey: false);
if (fabricated.Count > 0)
{
    Report("package refused", [fabricated[0]]);
    Console.Error.WriteLine(
        $"  ({fabricated.Count - 1} question(s) affected; the draft is on disk for inspection.)");

    return 1;
}

/*
 * ── The answer key, written on by a counter rather than by a model ────────
 *
 * After the guard, so a model that produced answers anyway is refused before
 * this could overwrite them and hide it. Before validation, so the package the
 * validator judges is the one that would actually be published.
 */
if (keyText is { } key)
{
    var entries = ParseAnswerKey(key);
    Console.WriteLine(
        $"key read   {entries.Count} entr(ies) covering "
        + $"{entries.Sum(e => e.Marks)} mark(s)");

    var injected = AnswerKeyInjection.Apply(package.PackageJson, entries);
    await File.WriteAllTextAsync(parsed.Out, injected.PackageJson);

    if (!injected.IsSuccess)
    {
        Report("answer key refused", injected.Findings);
        Console.Error.WriteLine(
            "The paper is on disk without answers. A key that does not line up with the paper "
            + "is the failure that marks every learner wrongly and shows no symptom, so nothing "
            + "is written rather than guessed.");

        return 1;
    }

    package = package with { PackageJson = injected.PackageJson };
    Console.WriteLine($"key applied {parsed.Out}");
}

package = package with { PackageJson = RepairGroups(package.PackageJson) };
await File.WriteAllTextAsync(parsed.Out, package.PackageJson);

var validator = new ExamPackageValidator(
    ExamPackageReader.FromSchemaFile(
        Path.Combine(repositoryRoot, "contracts", "schemas", "exam.schema.json")));

var validation = validator.Validate(package.PackageJson, ExamDefinitionId.New(), 1);

if (!validation.IsValid || validation.Version is null)
{
    Report("package rejected", validation.Findings);
    Console.Error.WriteLine(
        "The draft is on disk. Fix it by hand or rerun; nothing was published either way.");
    return 1;
}

foreach (var section in validation.Version.Sections.OrderBy(s => s.Order))
{
    Console.WriteLine(
        $"section    {section.Module}: {section.Questions.Count()} question(s), "
        + $"{section.AutoScoredMarks} auto-scored mark(s)");
}

Report("package accepted with warnings", validation.Findings);
Console.WriteLine("publication=not attempted (this tool never publishes)");
return 0;

static void Report(string headline, IReadOnlyList<PackageFinding> findings)
{
    if (findings.Count == 0)
    {
        if (!headline.StartsWith("package accepted", StringComparison.Ordinal)
            && !headline.StartsWith("question groups", StringComparison.Ordinal))
            Console.Error.WriteLine(headline);

        return;
    }

    Console.Error.WriteLine(headline);
    foreach (var finding in findings)
        Console.Error.WriteLine($"  {finding.Severity} {finding.Code} {finding.Path}: {finding.Message}");
}

/// <summary>
/// The package with its only section's module set to <paramref name="keep"/>,
/// or null when there is not exactly one section or it already matches.
/// Listening parts become <c>recording</c> so the assembler can wire audio.
/// </summary>
static string? RelabelLoneSection(string packageJson, string keep)
{
    var root = System.Text.Json.Nodes.JsonNode.Parse(packageJson)?.AsObject();
    if (root?["sections"] is not System.Text.Json.Nodes.JsonArray { Count: 1 } sections) return null;
    if (sections[0] is not System.Text.Json.Nodes.JsonObject section) return null;
    if (string.Equals(section["module"]?.GetValue<string>(), keep, StringComparison.Ordinal)) return null;

    section["module"] = keep;
    if (keep == "listening" && section["parts"] is System.Text.Json.Nodes.JsonArray parts)
    {
        foreach (var part in parts.OfType<System.Text.Json.Nodes.JsonObject>())
            part["kind"] = "recording";
    }

    return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}

static string DropForeignParts(string packageJson)
{
    var pruned = SectionPartRepair.DropForeignParts(packageJson);
    Report("foreign parts dropped", pruned.Findings);
    return pruned.PackageJson;
}

static string RepairGroups(string packageJson)
{
    var repaired = QuestionGroupRepair.SplitDivergentBanks(packageJson);
    Report("question groups repaired", repaired.Findings);
    return repaired.PackageJson;
}

/// <summary>
/// The `--package` path: read the key, write it onto a draft already on disk,
/// validate, and report. No extraction, no provider, no cost.
/// </summary>
static async Task<int> ApplyKeyOnlyAsync(
    string packagePath, Args args, string repositoryRoot, IServiceProvider services)
{
    if (args.Key is not { } keyPath)
    {
        Console.Error.WriteLine("--package needs --key: there is nothing else for it to do.");
        return 2;
    }

    var extractor = services.GetRequiredService<ISourceDocumentExtractor>();
    var sandbox = Path.GetDirectoryName(Path.GetFullPath(keyPath))!;
    var relative = Path.GetRelativePath(sandbox, Path.GetFullPath(keyPath));

    var extraction = await extractor.ExtractAsync(
        sandbox, relative, SourceExtractionLimits.Operator, CancellationToken.None);

    if (!extraction.IsSuccess || extraction.Source is null)
    {
        Report("key extraction refused", extraction.Findings);
        return 1;
    }

    var entries = ParseAnswerKey(extraction.Source.Text);
    Console.WriteLine(
        $"key read   {entries.Count} entr(ies) covering {entries.Sum(e => e.Marks)} mark(s)");

    var packageJson = DropForeignParts(await File.ReadAllTextAsync(packagePath));
    var (withoutModelKeys, removed) = FabricatedAnswerKeyGuard.Strip(packageJson);
    if (removed > 0)
    {
        packageJson = withoutModelKeys;
        Console.Error.WriteLine($"model answers  {removed} existing answerKey(s) removed before the key document is applied");
    }
    var injected = AnswerKeyInjection.Apply(packageJson, entries);

    await File.WriteAllTextAsync(args.Out, injected.PackageJson);
    Console.WriteLine($"written    {args.Out}");

    if (!injected.IsSuccess)
    {
        Report("answer key refused", injected.Findings);
        return 1;
    }

    // Warnings survive a success. A question whose type was corrected still
    // renders as the wrong control, and nobody sees that from a clean exit.
    Report("answer key applied with warnings", injected.Findings);

    injected = injected with { PackageJson = RepairGroups(injected.PackageJson) };
    await File.WriteAllTextAsync(args.Out, injected.PackageJson);

    var validation = new ExamPackageValidator(
        ExamPackageReader.FromSchemaFile(
            Path.Combine(repositoryRoot, "contracts", "schemas", "exam.schema.json")))
        .Validate(injected.PackageJson, ExamDefinitionId.New(), 1);

    if (!validation.IsValid || validation.Version is null)
    {
        Report("package rejected", validation.Findings);
        return 1;
    }

    foreach (var section in validation.Version.Sections.OrderBy(s => s.Order))
    {
        Console.WriteLine(
            $"section    {section.Module}: {section.Questions.Count()} question(s), "
            + $"{section.AutoScoredMarks} auto-scored mark(s)");
    }

    Console.WriteLine("publication=not attempted (this tool never publishes)");
    return 0;
}

static string DefaultTemplate(string repositoryRoot) =>
    Path.Combine(repositoryRoot, "fixtures", "exams", "vol9-test-1.json");

/// <summary>
/// Cambridge keys are two-column and use <c>IN EITHER ORDER</c>; VOL 9 keys
/// are a single column or <c>Câu số N:</c>. Detect the Cambridge shape and
/// normalise it before the shared reader sees it.
/// </summary>
static IReadOnlyList<AnswerKeyEntry> ParseAnswerKey(string text)
{
    /*
     * The letter-spaced badge test used to be `LIS\s*TE\s*N\s*ING`, which also
     * matches the plain word LISTENING — present in every VOL 9 key+transcript
     * document — so VOL 9 T2 Listening's "Câu số N:" key went through the
     * Cambridge normaliser and came out as 0 entries. A Cambridge key is
     * recognised by its own furniture, not by the name of the module.
     */
    var looksCambridge =
        text.Contains("IN EITHER ORDER", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Listening and Reading answer keys", StringComparison.OrdinalIgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(
            text, @"\bL\s*I\s+S\s*T\s*E\s*N\s*I\s*N\s*G\b|\bR\s*E\s+A\s*D\s*I\s*N\s*G\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(text, @"(?m)^\s*Reading Passage \d,");

    if (looksCambridge)
    {
        text = CambridgeAnswerKeyNormalizer.Normalise(text);
        Console.WriteLine("key shape  Cambridge (two-column / IN EITHER ORDER) → normalised");
    }

    return AnswerKeyDocument.Parse(text);
}

static string? FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "contracts", "schemas")))
            return directory.FullName;

        directory = directory.Parent;
    }

    return null;
}

internal sealed record Args(
    string Source, string Out, string? Key, string? Template, string? Title,
    bool RightsCleared, string? Sandbox, string? Package, string? KeepModule)
{
    public static Args? Parse(string[] argv)
    {
        string? source = null, output = null, key = null, template = null, title = null, sandbox = null;
        string? package = null, keepModule = null;
        var rightsCleared = false;

        for (var i = 0; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--source" when i + 1 < argv.Length: source = argv[++i]; break;
                case "--out" when i + 1 < argv.Length: output = argv[++i]; break;
                case "--key" when i + 1 < argv.Length: key = argv[++i]; break;
                case "--template" when i + 1 < argv.Length: template = argv[++i]; break;
                case "--title" when i + 1 < argv.Length: title = argv[++i]; break;
                case "--sandbox" when i + 1 < argv.Length: sandbox = argv[++i]; break;
                case "--package" when i + 1 < argv.Length: package = argv[++i]; break;
                case "--keep-module" when i + 1 < argv.Length: keepModule = argv[++i]; break;
                case "--rights-cleared": rightsCleared = true; break;
                default:
                    Console.Error.WriteLine($"unrecognised argument: {argv[i]}");
                    return null;
            }
        }

        if (keepModule is not null
            && keepModule is not ("reading" or "listening" or "writing" or "speaking"))
        {
            Console.Error.WriteLine("--keep-module must be reading, listening, writing or speaking");
            return null;
        }

        if ((source is null && package is null) || output is null)
        {
            Console.Error.WriteLine(
                "usage: --source <file.docx|file.pdf> --out <package.json>\n"
                + "   or: --package <draft.json> --key <key.docx> --out <package.json>\n"
                + "       [--key <key.docx>] [--template <accepted-package.json>] [--title <name>]\n"
                + "       [--keep-module reading|listening|writing|speaking]\n"
                + "       [--rights-cleared] [--sandbox <dir>]");

            return null;
        }

        return new Args(
            source ?? string.Empty, output, key, template, title, rightsCleared, sandbox, package,
            keepModule);
    }
}

/// <summary>
/// Puts the model's <c>sections</c> inside an envelope taken from a package a
/// person already accepted.
/// </summary>
internal static class ExamPackageEnvelope
{
    public static string Wrap(string sectionsJson, string templateJson, string? title)
    {
        var parsed = System.Text.Json.Nodes.JsonNode.Parse(sectionsJson)?.AsObject()
            ?? throw new InvalidOperationException("the output was not a JSON object");

        var sections = parsed["sections"]
            ?? throw new InvalidOperationException("the output had no `sections` property");

        /*
         * Anything else the model produced is dropped rather than merged.
         * Merging would let it override a band table by naming one, which is
         * the single thing this assembly step exists to prevent.
         */
        var extras = parsed.Select(p => p.Key).Where(k => k != "sections").ToList();
        if (extras.Count > 0)
            Console.Error.WriteLine($"note       ignored model properties: {string.Join(", ", extras)}");

        var package = System.Text.Json.Nodes.JsonNode.Parse(templateJson)?.AsObject()
            ?? throw new InvalidOperationException("the template package was not a JSON object");

        package["sections"] = sections.DeepClone();
        if (title is not null) package["title"] = title;

        return package.ToJsonString(
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Drops every section the operator did not ask to keep.
    ///
    /// A one-skill extract still sometimes comes back with empty Writing and
    /// Speaking shells, and those shells then fail answer-key coverage for a
    /// skill that was never in the source. Filtering here is cheaper than
    /// paying for a second parse.
    /// </summary>
    public static string KeepModule(string packageJson, string module)
    {
        var package = System.Text.Json.Nodes.JsonNode.Parse(packageJson)?.AsObject()
            ?? throw new InvalidOperationException("the package was not a JSON object");

        var sections = package["sections"]?.AsArray()
            ?? throw new InvalidOperationException("the package had no sections");

        var kept = new System.Text.Json.Nodes.JsonArray();
        var order = 1;
        foreach (var section in sections)
        {
            if (section?["module"]?.GetValue<string>() is not { } name) continue;
            if (!string.Equals(name, module, StringComparison.OrdinalIgnoreCase)) continue;

            var clone = section.DeepClone();
            clone["order"] = order++;
            kept.Add(clone);
        }

        if (kept.Count == 0)
            throw new InvalidOperationException($"the package had no {module} section to keep");

        package["sections"] = kept;
        return package.ToJsonString(
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }
}

/// <summary>
/// Accepts the embedded images a DOCX carries and keeps none of them.
///
/// <b>The extractor needs somewhere to put media before it will hand back
/// text</b>, and this tool's job ends at a package JSON — asset wiring is
/// `import-exam-package.py`'s, working from files the operator already has. So
/// the store records the reference and discards the bytes rather than writing a
/// second copy of every image into a directory nobody reads.
/// </summary>
internal sealed class DiscardedImportAssetStore : IPrivateImportAssetStore
{
    public Task<string> PutPrivateAsync(
        string key, Stream content, string contentType, string sha256, CancellationToken ct) =>
        Task.FromResult($"discarded:{key}");
}
