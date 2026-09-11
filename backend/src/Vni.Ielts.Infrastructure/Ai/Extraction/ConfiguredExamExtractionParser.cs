using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Importing;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Observability;

namespace Vni.Ielts.Infrastructure.Ai.Extraction;

/// <summary>
/// The configured provider-neutral parser: extracted documents in, a validated
/// review candidate out.
///
/// ── What it decides, and what it refuses to decide ────────────────────────
///
/// <b>Provider selection, the rights gate, the attempt budget and the deadline
/// all live here</b>, so the two adapters carry no policy of their own and
/// cannot drift apart. Everything it decides comes from configuration; nothing
/// it cannot find in configuration gets a default.
///
/// <b>It never produces a Draft, and cannot.</b> Its output is a
/// <see cref="ParsedExamCandidate"/> in <c>PendingReview</c> — the same artefact
/// the deterministic parser produced, now with content in it. Confirmation is a
/// person's act, and canonical completion is a later plan.
///
/// <b>A failed parse persists nothing.</b> A refusal or an exhausted attempt
/// budget throws, which leaves the package at <c>Parsing</c> for staff to act
/// on: the same behaviour a candidate-write failure already had. It does not
/// invent a retry schedule, and it does not mark a package reviewable on the
/// strength of an answer that failed validation.
/// </summary>
public sealed class ConfiguredExamExtractionParser(
    IEnumerable<IExamExtractionClient> clients,
    IOptions<ExamParsingOptions> options,
    IExamExtractionRunStore runs,
    IClock clock,
    ExamParsingMetrics metrics,
    ILogger<ConfiguredExamExtractionParser> logger) : IExamContentParser
{
    public async Task<ParsedExamCandidate> ParseAsync(
        IReadOnlyList<ExtractedSourceDocument> documents, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
            throw new ArgumentException("At least one extracted document is required.", nameof(documents));

        var packageId = documents[0].PackageId;
        if (documents.Any(document => !string.Equals(document.PackageId, packageId, StringComparison.Ordinal)))
        {
            throw new ExamExtractionRejectedException(
                ExamExtractionRejection.PackageMismatch,
                "The extracted documents do not all belong to the same package.");
        }

        var candidateId = CandidateId(documents);
        var readable = documents.Where(document => document.HasText).ToArray();

        if (readable.Length == 0)
        {
            /*
             * <b>Nothing to send is not a provider failure.</b> An encrypted or
             * image-only upload is already an actionable extraction finding on
             * the package; calling a provider with an empty prompt would spend
             * money to be told nothing. The package still becomes reviewable,
             * with a candidate that proposes nothing — which is what the
             * deterministic parser produced for every package before this one
             * existed.
             */
            logger.LogInformation(
                "Package {PackageId} has no extractable text, so no provider was called.", packageId);

            return NeedsReview(candidateId, packageId, documents);
        }

        var policy = options.Value;
        var order = policy.ProviderOrder();
        if (order.Count == 0)
        {
            throw new InvalidOperationException(
                "ExamParsing names no provider, so the configured parser must not have been "
                + "composed. This is a composition fault, not a provider failure.");
        }

        var bindings = Bindings(readable);
        var sources = Sources(readable);
        var index = new ExamExtractionSourceIndex(bindings);
        var promptVersion = policy.PromptVersion ?? ExamExtractionPrompt.Version;
        var maxResponseBytes = policy.ResolvedMaxResponseBytes();
        var maxAttempts = policy.ResolvedMaxAttempts();
        var timeoutSeconds = policy.TimeoutSeconds
            ?? throw new InvalidOperationException("ExamParsing:TimeoutSeconds is required.");
        var rights = policy.ResolvedRights();
        var inputHash = InputHash(readable, promptVersion);

        Exception? last = null;
        var attempts = 0;

        foreach (var section in order)
        {
            var client = clients.FirstOrDefault(candidate =>
                string.Equals(candidate.Provider, section, StringComparison.OrdinalIgnoreCase));

            if (client is null)
            {
                last ??= new InvalidOperationException(
                    $"ExamParsing names provider '{section}', for which no adapter is registered.");
                continue;
            }

            if (RightsRefusal(client, rights) is { } refusedRights)
            {
                var refusedAt = Correlation();

                await TryRecordAsync(
                    Run(packageId, candidateId: null, Unanswered(client, refusedAt),
                        promptVersion, refusedAt, inputHash, readable.Length, clock.UtcNow,
                        refusedRights.Code),
                    ct);

                metrics.RecordFailure(client.Provider, refusedRights.Code);
                throw refusedRights;
            }

            for (var providerAttempt = 0; providerAttempt < maxAttempts; providerAttempt++)
            {
                attempts++;
                var requestedAt = clock.UtcNow;
                var correlationId = Correlation();

                /*
                 * Held outside the try so a validation refusal can be recorded
                 * against what the provider actually answered — its model, its
                 * request id, its token counts. Recording `model=unknown` for a
                 * response that named a model would make the one question a run
                 * record exists to answer ("which model produced this shape")
                 * unanswerable for exactly the runs where it matters.
                 */
                ExamExtractionResponse? answered = null;
                var attemptStartedAt = Stopwatch.GetTimestamp();

                try
                {
                    answered = await client.ExtractAsync(
                        new ExamExtractionRequest(
                            sources, promptVersion, correlationId, attempts, maxResponseBytes, timeoutSeconds),
                        ct);

                    var candidate = ExamExtractionValidator.ToCandidate(
                        answered.Json, candidateId, packageId, index, maxResponseBytes);

                    /*
                     * <b>Recorded before the candidate is returned, and a
                     * failure to record fails the parse.</b> The caller persists
                     * whatever this method returns, so a swallowed storage
                     * failure would produce a reviewable candidate with no
                     * record of which provider, model or prompt produced it —
                     * and nothing downstream could tell that apart from a
                     * candidate whose provenance was never required. The
                     * audit fact and the artefact land together or neither
                     * lands.
                     */
                    await RecordAsync(
                        Run(packageId, candidateId, answered, promptVersion, correlationId, inputHash,
                            readable.Length, requestedAt, failureCode: null),
                        ct);

                    metrics.RecordTokens(client.Provider, answered.InputTokens, answered.OutputTokens);
                    return candidate;
                }
                catch (ExamExtractionRejectedException rejected)
                {
                    /*
                     * A refused answer is recorded and not retried. Repeating a
                     * request that produced an invalid shape spends the budget
                     * to get the same shape, and the run record is what tells
                     * an operator later whether a provider changed behaviour.
                     */
                    await TryRecordAsync(
                        Run(packageId, candidateId: null, answered ?? Unanswered(client, correlationId),
                            promptVersion, correlationId, inputHash, readable.Length, requestedAt,
                            rejected.Code),
                        ct);

                    logger.LogWarning(
                        "Exam extraction for package {PackageId} was refused by validation with "
                        + "code {Code} from {Provider}, correlation {CorrelationId}.",
                        packageId,
                        rejected.Code,
                        client.Provider,
                        correlationId);

                    metrics.RecordFailure(client.Provider, rejected.Code);
                    throw;
                }
                catch (TransientExamExtractionException e)
                {
                    /*
                     * <b>A failed attempt is a run too.</b> Without this record
                     * a package that eventually parsed on the second provider
                     * looks like a package that parsed first time, and a route
                     * that times out on half its calls is invisible until
                     * somebody happens to read the logs.
                     */
                    await TryRecordAsync(
                        Run(packageId, candidateId: null, Unanswered(client, correlationId),
                            promptVersion, correlationId, inputHash, readable.Length, requestedAt,
                            ExamExtractionRejection.TransientFailure),
                        ct);

                    last = e;
                    metrics.RecordFailure(client.Provider, ExamExtractionRejection.TransientFailure);
                    logger.LogWarning(
                        "Transient exam extraction failure from {Provider} for package {PackageId}, "
                        + "attempt {Attempt}, correlation {CorrelationId}.",
                        client.Provider,
                        packageId,
                        attempts,
                        correlationId);
                }
                catch (AiEgressRefusedException e)
                {
                    // A configuration boundary, not a provider fault: retrying
                    // it would produce the identical refusal. Recorded anyway,
                    // because "the guard refused every call for a week" is a
                    // fact an operator needs and a log line does not keep.
                    await TryRecordAsync(
                        Run(packageId, candidateId: null, Unanswered(client, correlationId),
                            promptVersion, correlationId, inputHash, readable.Length, requestedAt,
                            ExamExtractionRejection.EgressRefused),
                        ct);

                    last = e;
                    metrics.RecordFailure(client.Provider, ExamExtractionRejection.EgressRefused);
                    break;
                }
                finally
                {
                    metrics.RecordDuration(
                        Stopwatch.GetElapsedTime(attemptStartedAt).TotalSeconds,
                        client.Provider);
                }

                /*
                 * `OperationCanceledException` is deliberately not caught, and
                 * so writes no run record. A cancelled call has no completion
                 * to describe — the worker is shutting down or the job was
                 * abandoned — and inventing a completion timestamp for it would
                 * put a run in the audit trail that never finished. The
                 * package is untouched and the next poll starts over.
                 */
            }
        }

        throw last ?? new InvalidOperationException(
            $"No configured extraction provider answered for package {packageId}.");
    }

    /// <summary>
    /// The rights gate, which is a different question from
    /// <see cref="AiEgress"/>.
    ///
    /// <para>
    /// <b>An exam paper is somebody's copyright and nobody's personal data; a
    /// learner's essay is the reverse.</b> So the egress guard asks whose
    /// <i>person</i> is in the payload and clears an exam paper trivially,
    /// while this asks whose <i>material</i> may be shown to a third
    /// organisation. Collapsing them would let an upload nobody has cleared
    /// pass because it contains no personal data.
    /// </para>
    /// </summary>
    private static ExamExtractionRejectedException? RightsRefusal(
        IExamExtractionClient client, ImportDataClassification rights)
    {
        if (!client.IsReseller) return null;
        if (rights is ImportDataClassification.Synthetic or ImportDataClassification.RightsCleared) return null;

        return new ExamExtractionRejectedException(
            ExamExtractionRejection.ResellerRestrictedSource,
            $"{client.Provider} is configured against a third-party endpoint, and "
            + $"ExamParsing:SourceRights is {rights}. Only synthetic or rights-cleared material may "
            + "pass through a third party, and an upload is restricted until somebody who holds the "
            + "rights says otherwise.");
    }

    /// <summary>
    /// Records the run, and fails the parse if it cannot.
    ///
    /// <para>
    /// <b>Used on the accepted path only, and deliberately not forgiving.</b>
    /// The caller persists the candidate this method returns, so a swallowed
    /// storage failure would put a reviewable proposal in front of staff with
    /// no record of the provider, model or prompt behind it — and a candidate
    /// whose provenance is merely missing is indistinguishable from one whose
    /// provenance was never required. Refusing to return the candidate keeps
    /// the package at <c>Parsing</c>, which is a state staff can act on.
    /// </para>
    /// </summary>
    private async Task RecordAsync(ExamExtractionRunMetadata run, CancellationToken ct)
    {
        try
        {
            await runs.RecordAsync(run, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(
                e,
                "Could not record the exam extraction run for package {PackageId}, so the parse "
                + "is failed rather than returning a candidate with no provider audit trail.",
                run.PackageId);

            throw new ExamExtractionRunNotRecordedException(run.PackageId);
        }
    }

    /// <summary>
    /// Records a failed attempt, and lets the original failure stand if it
    /// cannot.
    ///
    /// <para>
    /// <b>The asymmetry with <see cref="RecordAsync"/> is the point.</b> On a
    /// failure path the parse is already failing and no candidate will be
    /// persisted, so nothing reaches a reviewer unattributed; replacing the
    /// refusal with a storage error would only send whoever reads it to the
    /// wrong system. On the accepted path an unrecorded run is a real hole, so
    /// it fails closed.
    /// </para>
    /// </summary>
    private async Task TryRecordAsync(ExamExtractionRunMetadata run, CancellationToken ct)
    {
        try
        {
            await runs.RecordAsync(run, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(
                e,
                "Could not record the failed exam extraction run for package {PackageId}.",
                run.PackageId);
        }
    }

    private static string Correlation() => Guid.NewGuid().ToString("n");

    /// <summary>
    /// The stand-in for a run where no provider response exists — a timeout, a
    /// refused egress, a rights refusal.
    ///
    /// <para>
    /// <b>The model comes from the adapter's configuration, not from a
    /// placeholder.</b> "which model was this install pointed at when the calls
    /// started failing" is the first question asked about a run of failures, and
    /// <c>unknown</c> cannot answer it. The request identifier is this
    /// product's own correlation id, because the provider never issued one.
    /// </para>
    /// </summary>
    private static ExamExtractionResponse Unanswered(
        IExamExtractionClient client, string correlationId) =>
        new("", client.Provider, client.Model, correlationId, 0, 0);

    private ExamExtractionRunMetadata Run(
        string packageId,
        string? candidateId,
        ExamExtractionResponse response,
        string promptVersion,
        string correlationId,
        string inputHash,
        int sourceCount,
        DateTimeOffset requestedAt,
        string? failureCode) =>
        new(
            packageId,
            candidateId,
            response.Provider,
            response.Model,
            promptVersion,
            ExamExtractionContract.SchemaId,
            ExamExtractionContract.Version,
            string.IsNullOrWhiteSpace(response.RequestId) ? correlationId : response.RequestId,
            inputHash,
            sourceCount,
            response.InputTokens,
            response.OutputTokens,
            requestedAt,
            clock.UtcNow,
            failureCode);

    private static ParsedExamCandidate NeedsReview(
        string candidateId, string packageId, IReadOnlyList<ExtractedSourceDocument> documents) =>
        ParsedExamCandidate.Create(
            candidateId,
            packageId,
            null,
            ParsedExamClassification.NeedsReview,
            null,
            [],
            [.. documents.Select(document =>
                new ParsedSourceProvenance(document.EntryPath, null, null, document.Sha256))]);

    /// <summary>
    /// The provider-facing view of the documents.
    ///
    /// <b>Identifiers are ordinals, not paths.</b> <c>s1</c> and <c>s1c2</c>
    /// mean nothing outside this request, which is the point: the archive entry
    /// path is how a reviewer will find the document again and is also a string
    /// an uploader chose, so it stays here.
    /// </summary>
    private static IReadOnlyList<ExamExtractionSource> Sources(
        IReadOnlyList<ExtractedSourceDocument> documents) =>
        [.. documents.Select((document, index) => new ExamExtractionSource(
            SourceId(index),
            document.MediaType,
            [.. document.Chunks.Select((chunk, chunkIndex) => new ExamExtractionSourceChunk(
                ChunkId(index, chunkIndex), chunk.PageNumber, chunk.Text))]))];

    private static IReadOnlyList<ExamExtractionSourceBinding> Bindings(
        IReadOnlyList<ExtractedSourceDocument> documents) =>
        [.. documents.Select((document, index) => new ExamExtractionSourceBinding(
            SourceId(index),
            document.EntryPath,
            document.Sha256,
            [.. document.Chunks.Select((chunk, chunkIndex) => new ExamExtractionChunkBinding(
                ChunkId(index, chunkIndex), chunk.PageNumber, chunk.Section))]))];

    private static string SourceId(int index) => $"s{index + 1}";

    private static string ChunkId(int sourceIndex, int chunkIndex) =>
        $"s{sourceIndex + 1}c{chunkIndex + 1}";

    /// <summary>
    /// A stable candidate identifier for one package's document set, so a
    /// re-parse of the same bytes addresses the same candidate rather than
    /// accumulating one per attempt.
    /// </summary>
    private static string CandidateId(IReadOnlyList<ExtractedSourceDocument> documents) =>
        Hash(string.Join('\n', documents.Select(document => document.Sha256)))[..32];

    /// <summary>
    /// What was sent, as a hash rather than as itself.
    ///
    /// <b>Covers the document hashes and the prompt version, which together are
    /// the input.</b> Storing the prompt or the source text would put an exam
    /// paper — and any injected string in it — into a record that operators
    /// read; a hash answers "was this the same input" without keeping the
    /// input.
    /// </summary>
    private static string InputHash(IReadOnlyList<ExtractedSourceDocument> documents, string promptVersion)
    {
        var material = new StringBuilder()
            .Append(promptVersion).Append('\n')
            .Append(ExamExtractionContract.Version).Append('\n');

        foreach (var document in documents)
        {
            material.Append(document.Sha256).Append(':')
                .Append(document.Chunks.Count).Append('\n');
        }

        return Hash(material.ToString());
    }

    private static string Hash(string material) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
}
