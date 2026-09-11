namespace Vni.Ielts.Application.Exams;

/// <summary>
/// The provider-neutral half of AI-assisted raw-package parsing: what a model
/// is shown, what it is allowed to answer, and what is recorded about the run.
///
/// <para>
/// <b>No vendor type appears here, and none may.</b> A GPT or Gemini request
/// shape is a wire detail owned by an Infrastructure adapter; what the rest of
/// the product needs is a source list in, a JSON string out, and enough
/// metadata to trace the second back to the first. → ADR-0005
/// </para>
/// </summary>
public static class ExamExtractionContract
{
    /// <summary>The <c>$id</c> of <c>contracts/schemas/exam-extraction.schema.json</c>.</summary>
    public const string SchemaId = "https://vni.edu.vn/schemas/exam-extraction.schema.json";

    /// <summary>
    /// The value <c>contractVersion</c> must carry.
    ///
    /// <para>
    /// <b>Pinned rather than range-checked.</b> A stored proposal has to name
    /// the contract that produced it, because the useful question months later
    /// is "was this shape the one we asked for" and a version that drifts
    /// silently cannot answer it. A new shape is a new value and a new
    /// constant.
    /// </para>
    /// </summary>
    public const string Version = "exam-extraction-v1";

    /// <summary>
    /// The largest provider response this product will read, when
    /// configuration does not say otherwise.
    ///
    /// <para>
    /// <b>A structural cap, not a product policy.</b> It exists so a runaway or
    /// hostile response cannot be buffered into memory before anything checks
    /// it — the same class of limit as <c>DocumentExtractionLimits</c>, and not
    /// the kind of number <c>G-11</c> is about. A whole Reading paper's
    /// proposal measures in the low hundreds of kilobytes, so two mebibytes is
    /// several times the largest legitimate answer.
    /// </para>
    /// </summary>
    public const int DefaultMaxResponseBytes = 2 * 1024 * 1024;
}

/// <summary>
/// One chunk of one source document, as the provider sees it.
///
/// <para>
/// <b><see cref="ChunkId"/> is issued by this product, not by the archive.</b>
/// The model quotes it back in <c>provenance</c>, which is what lets the server
/// map a proposal onto real bytes and reject a reference it never issued.
/// </para>
/// </summary>
public sealed record ExamExtractionSourceChunk(string ChunkId, int? Page, string Text);

/// <summary>
/// One extracted document, as the provider sees it.
///
/// <para>
/// <b><see cref="SourceId"/> is opaque on purpose.</b> The archive entry path
/// stays on this side of the boundary: it is how a reviewer will find the
/// document again, and it is also a string an uploader controls. The provider
/// gets <c>s1</c>; the server keeps the mapping. Nothing here carries an
/// account, an email, a user ID, or a filesystem path.
/// </para>
/// </summary>
public sealed record ExamExtractionSource(
    string SourceId,
    string MediaType,
    IReadOnlyList<ExamExtractionSourceChunk> Chunks);

/// <summary>
/// What an adapter is asked to do. Carries no identity of any person.
///
/// <para>
/// <b>Every policy value an adapter needs arrives here, and none of it is the
/// adapter's own.</b> The deadline, the attempt number and the size cap are
/// decisions about how much this product is willing to spend and wait; leaving
/// them to each client would give two providers two policies and no record of
/// either. <see cref="CorrelationId"/> is server-generated and opaque, so a
/// call can be found in a log without a learner, an author or a path appearing
/// in one.
/// </para>
/// </summary>
public sealed record ExamExtractionRequest(
    IReadOnlyList<ExamExtractionSource> Sources,
    string PromptVersion,
    string CorrelationId,
    int Attempt,
    int MaxResponseBytes,
    int TimeoutSeconds);

/// <summary>
/// What an adapter returns: the unvalidated JSON, and the facts about the call.
/// The JSON is a claim until the server validator has accepted it.
/// </summary>
public sealed record ExamExtractionResponse(
    string Json,
    string Provider,
    string Model,
    string RequestId,
    long InputTokens,
    long OutputTokens);

/// <summary>
/// The safe record of one extraction run.
///
/// <para>
/// <b>Every field here is either an identifier, a version, a count, or a
/// hash.</b> There is deliberately no source text, no answer key, no prompt
/// body, and no credential — a run record is read by operators and support, so
/// it has to be safe by construction rather than by each caller remembering.
/// <see cref="InputHash"/> is what makes a run reproducible without storing
/// what was sent.
/// </para>
/// </summary>
public sealed record ExamExtractionRunMetadata(
    string PackageId,
    string? CandidateId,
    string Provider,
    string Model,
    string PromptVersion,
    string SchemaId,
    string ContractVersion,
    string RequestId,
    string InputHash,
    int SourceCount,
    long InputTokens,
    long OutputTokens,
    DateTimeOffset RequestedAt,
    DateTimeOffset CompletedAt,
    string? FailureCode);

/// <summary>
/// A provider adapter. Implemented once per vendor in Infrastructure; both
/// implementations feed the same server-side validator.
/// </summary>
public interface IExamExtractionClient
{
    /// <summary><c>OpenAi</c> or <c>Gemini</c> — the configuration section name.</summary>
    string Provider { get; }

    /// <summary>
    /// The model this adapter is configured against, readable without making a
    /// call.
    ///
    /// <para>
    /// <b>It exists so a failed attempt can still be recorded honestly.</b> A
    /// timeout and a refused egress produce no response and therefore no
    /// model name, and a run record that says <c>unknown</c> cannot answer the
    /// first question asked about a run of failures — which model this install
    /// was pointed at. It is a configured string, not a vendor type.
    /// </para>
    /// </summary>
    string Model { get; }

    /// <summary>
    /// Whether the configured endpoint puts a third organisation in the path.
    /// A reseller may be shown synthetic or rights-cleared material only.
    /// </summary>
    bool IsReseller { get; }

    Task<ExamExtractionResponse> ExtractAsync(ExamExtractionRequest request, CancellationToken ct);
}

/// <summary>
/// Where a run record goes. Separate from the candidate repository because a
/// run is an audit fact about a provider call, not part of the artefact a
/// reviewer edits — and because a run exists for failures, which never produce
/// a candidate.
/// </summary>
public interface IExamExtractionRunStore
{
    Task RecordAsync(ExamExtractionRunMetadata run, CancellationToken ct);
}

/// <summary>
/// The call may work if repeated: a timeout, a 5xx, a rate limit, a stalled
/// socket. Whether it <i>is</i> repeated is a configured decision, not this
/// exception's business.
/// </summary>
public sealed class TransientExamExtractionException(string message) : Exception(message);

/// <summary>
/// The response was read and refused, and repeating the call will not help.
///
/// <para>
/// <b><see cref="Code"/> is stable and safe to log.</b> The message names what
/// failed; the code is what an operator filters on and what a test asserts, so
/// a reworded message cannot quietly break either.
/// </para>
/// </summary>
public sealed class ExamExtractionRejectedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// The provider answered acceptably and the run could not be recorded, so the
/// parse fails rather than returning a candidate nothing can attribute.
///
/// <para>
/// <b>Carries a package identifier and nothing else.</b> No provider payload,
/// no prompt, no source text — the thing that failed is a write to this
/// product's own storage, and the message is read by whoever is on support.
/// </para>
/// </summary>
public sealed class ExamExtractionRunNotRecordedException(string packageId)
    : Exception(
        $"The extraction run for package {packageId} could not be recorded, so no candidate was "
        + "returned. A reviewable proposal with no provider audit trail is not an acceptable "
        + "outcome; the package stays in parsing for staff to act on.")
{
    public string PackageId { get; } = packageId;
}

/// <summary>
/// The stable refusal codes. Grouped here so a reviewer can see the whole set
/// of ways an extraction can be rejected in one place.
/// </summary>
public static class ExamExtractionRejection
{
    public const string ResponseTooLarge = "EXTRACTION_RESPONSE_TOO_LARGE";
    public const string ProviderRejected = "EXTRACTION_PROVIDER_REJECTED";

    /// <summary>
    /// A timeout, a 5xx, a rate limit or a stalled socket. <b>Recorded as a
    /// run</b> even though the attempt produced nothing: a route that fails
    /// half its calls is otherwise invisible until somebody reads the logs.
    /// </summary>
    public const string TransientFailure = "EXTRACTION_TRANSIENT_FAILURE";

    /// <summary>
    /// The egress guard refused the call — no key, no model, an excluded model
    /// family, or a border the configuration does not permit crossing.
    /// </summary>
    public const string EgressRefused = "EXTRACTION_EGRESS_REFUSED";

    public const string NoContent = "EXTRACTION_NO_CONTENT";
    public const string MalformedJson = "EXTRACTION_MALFORMED_JSON";
    public const string SchemaInvalid = "EXTRACTION_SCHEMA_INVALID";
    public const string ContractVersion = "EXTRACTION_CONTRACT_VERSION";
    public const string InvalidEnum = "EXTRACTION_INVALID_ENUM";
    public const string MissingField = "EXTRACTION_MISSING_FIELD";
    public const string ConfidenceRange = "EXTRACTION_CONFIDENCE_RANGE";
    public const string DanglingSource = "EXTRACTION_DANGLING_SOURCE";
    public const string DanglingChunk = "EXTRACTION_DANGLING_CHUNK";
    public const string DanglingPage = "EXTRACTION_DANGLING_PAGE";
    public const string DuplicateOptionKey = "EXTRACTION_DUPLICATE_OPTION_KEY";
    public const string AnswerKeyNotAnOption = "EXTRACTION_ANSWER_KEY_NOT_AN_OPTION";
    public const string AnswerKeyWithoutOptions = "EXTRACTION_ANSWER_KEY_WITHOUT_OPTIONS";
    public const string CandidateInvariant = "EXTRACTION_CANDIDATE_INVARIANT";
    public const string PackageMismatch = "EXTRACTION_PACKAGE_MISMATCH";
    public const string ResellerRestrictedSource = "EXTRACTION_RESELLER_RESTRICTED_SOURCE";
}

/// <summary>
/// The untrusted shape, exactly as the contract describes it — every member
/// nullable.
///
/// <para>
/// <b>Nullable on purpose, even where the schema says required.</b> These types
/// are populated from a provider's bytes, and a deserializer that throws on a
/// missing member turns a refusal with a stable code into an exception a caller
/// has to guess at. Requiredness is checked by the schema and then again by
/// the mapper, which is the layer that can name what was absent.
/// </para>
/// </summary>
public sealed record ExamExtractionProvenanceDto(string? SourceId, string? ChunkId, int? Page);

/// <inheritdoc cref="ExamExtractionProvenanceDto"/>
public sealed record ExamExtractionOptionDto(string? Key, string? Text);

/// <inheritdoc cref="ExamExtractionProvenanceDto"/>
public sealed record ExamExtractionAnswerKeyDto(IReadOnlyList<string>? Accepted, string? MatchingRule);

/// <inheritdoc cref="ExamExtractionProvenanceDto"/>
public sealed record ExamExtractionQuestionDto(
    string? Id,
    int? Order,
    string? Type,
    string? Prompt,
    IReadOnlyList<ExamExtractionOptionDto>? Options,
    ExamExtractionAnswerKeyDto? AnswerKey,
    ExamExtractionProvenanceDto? Provenance);

/// <inheritdoc cref="ExamExtractionProvenanceDto"/>
public sealed record ExamExtractionPartDto(
    string? Id,
    int? Order,
    string? Title,
    string? Body,
    IReadOnlyList<ExamExtractionQuestionDto>? Questions,
    ExamExtractionProvenanceDto? Provenance);

/// <inheritdoc cref="ExamExtractionProvenanceDto"/>
public sealed record ExamExtractionModuleDto(
    string? Module,
    string? Classification,
    decimal? Confidence,
    IReadOnlyList<ExamExtractionPartDto>? Parts,
    ExamExtractionProvenanceDto? Provenance);

/// <inheritdoc cref="ExamExtractionProvenanceDto"/>
public sealed record ExamExtractionDto(
    string? ContractVersion,
    string? Title,
    string? Classification,
    decimal? Confidence,
    IReadOnlyList<ExamExtractionModuleDto>? Modules);
