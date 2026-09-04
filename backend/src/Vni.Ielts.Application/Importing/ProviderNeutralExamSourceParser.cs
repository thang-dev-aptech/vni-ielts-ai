namespace Vni.Ielts.Application.Importing;

public sealed record StructuredParseRequest(
    string SourceText,
    string SourceMediaType,
    string SchemaId,
    string PromptVersion,
    int Attempt,
    bool StrictJsonSchema);

public sealed record StructuredParseResponse(
    string Json,
    string Model,
    string RequestId,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd);

public sealed class TransientAiParserException(string message) : Exception(message);

public interface IStructuredExamAiClient
{
    string Provider { get; }
    bool IsReseller { get; }
    Task<StructuredParseResponse> ParseAsync(StructuredParseRequest request, CancellationToken ct);
}

public interface IAiImportCostMetric
{
    void Record(string provider, string model, long inputTokens, long outputTokens, decimal estimatedCostUsd);
}

public sealed record ExamParserOptions(string Provider, string PromptVersion, int MaxAttempts = 2);

/// <summary>Provider selection, retry policy and structured-output contract without vendor types.</summary>
public sealed class ProviderNeutralExamSourceParser : IExamSourceParser
{
    public const string ExamSchemaId = "https://vni.edu.vn/schemas/exam.schema.json";
    private readonly IStructuredExamAiClient client;
    private readonly IAiImportCostMetric costs;
    private readonly ExamParserOptions options;

    public ProviderNeutralExamSourceParser(
        IEnumerable<IStructuredExamAiClient> clients,
        IAiImportCostMetric costs,
        ExamParserOptions options)
    {
        this.costs = costs;
        this.options = options;
        if (options.MaxAttempts is < 1 or > 3)
            throw new ArgumentOutOfRangeException(nameof(options), "Parser attempts must be between 1 and 3.");
        client = clients.SingleOrDefault(c =>
            string.Equals(c.Provider, options.Provider, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"AI parser provider '{options.Provider}' is not configured.");
    }

    /// <summary>
    /// What a reseller endpoint is allowed to be shown.
    ///
    /// <para>
    /// <b><see cref="ImportDataClassification"/> asks about rights, not about
    /// privacy, and the two gates must not be read as one.</b> An exam paper is
    /// somebody's copyright and nobody's personal data; a learner's essay is
    /// the reverse. So this check governs <i>whose material</i> may be shown to
    /// a third party, while <c>AiEgress</c> governs <i>whose person</i> — and
    /// an exam source clears the second gate trivially because there is no data
    /// subject in it.
    /// </para>
    ///
    /// <para>
    /// <b><see cref="ImportDataClassification.RightsCleared"/> was added to this
    /// set on 2026-09-02</b>, when the owner directed that the supplied papers
    /// be published (<i>"đẩy hết luôn đi"</i>). Before that only
    /// <see cref="ImportDataClassification.Synthetic"/> passed, which meant no
    /// real paper could be parsed at all.
    /// <see cref="ImportDataClassification.Restricted"/> is still refused, and
    /// it is what <c>SafeSourceDocumentExtractor</c> stamps on every upload —
    /// so clearing a source stays a deliberate act by whoever holds the rights,
    /// never a default.
    /// </para>
    /// </summary>
    private static readonly ImportDataClassification[] ResellerPermitted =
        [ImportDataClassification.Synthetic, ImportDataClassification.RightsCleared];

    public async Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct)
    {
        if (client.IsReseller && !ResellerPermitted.Contains(source.Classification))
        {
            throw new InvalidOperationException(
                $"A reseller AI parser may not be shown a source classified "
                + $"{source.Classification}. Only synthetic or rights-cleared material may pass "
                + "through a third party; an upload is Restricted until somebody who holds the "
                + "rights says otherwise.");
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await client.ParseAsync(new StructuredParseRequest(
                    source.Text, source.MediaType, ExamSchemaId, options.PromptVersion, attempt,
                    StrictJsonSchema: true), ct);
                costs.Record(client.Provider, response.Model, response.InputTokens,
                    response.OutputTokens, response.EstimatedCostUsd);
                return new ParsedExamPackage(response.Json, new ParserRunMetadata(
                    client.Provider, response.Model, options.PromptVersion, response.RequestId));
            }
            catch (TransientAiParserException) when (attempt < options.MaxAttempts)
            {
                // Deliberately bounded and immediate here; provider adapters own Retry-After delays.
            }
        }
    }
}
