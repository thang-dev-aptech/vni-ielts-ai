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

    public async Task<ParsedExamPackage> ParseAsync(ExtractedImportSource source, CancellationToken ct)
    {
        if (client.IsReseller && source.Classification != ImportDataClassification.Synthetic)
            throw new InvalidOperationException("Reseller AI parsers may receive synthetic sources only.");

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
