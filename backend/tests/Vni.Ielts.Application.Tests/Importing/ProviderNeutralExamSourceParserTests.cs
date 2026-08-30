using Vni.Ielts.Application.Importing;

namespace Vni.Ielts.Application.Tests.Importing;

public sealed class ProviderNeutralExamSourceParserTests
{
    [Theory]
    [InlineData("openai")]
    [InlineData("gemini")]
    public async Task Selects_provider_and_requests_strict_versioned_schema_output(string provider)
    {
        var selected = new RecordedClient(provider, failures: 0);
        var other = new RecordedClient(provider == "openai" ? "gemini" : "openai", failures: 0);
        var metric = new Metric();
        var parser = new ProviderNeutralExamSourceParser(
            [other, selected], metric, new(provider, "exam-import/1", 2));

        var result = await parser.ParseAsync(Source(ImportDataClassification.Synthetic), default);

        var request = Assert.Single(selected.Requests);
        Assert.True(request.StrictJsonSchema);
        Assert.Equal(ProviderNeutralExamSourceParser.ExamSchemaId, request.SchemaId);
        Assert.Equal("exam-import/1", result.Metadata.PromptVersion);
        Assert.Equal("recorded-model", result.Metadata.Model);
        Assert.Equal(1, metric.Calls);
        Assert.Empty(other.Requests);
    }

    [Fact]
    public async Task Transient_failures_retry_only_to_the_configured_bound()
    {
        var client = new RecordedClient("openai", failures: 3);
        var parser = new ProviderNeutralExamSourceParser([client], new Metric(), new("openai", "p1", 2));

        await Assert.ThrowsAsync<TransientAiParserException>(() =>
            parser.ParseAsync(Source(ImportDataClassification.Synthetic), default));

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal([1, 2], client.Requests.Select(r => r.Attempt));
    }

    [Fact]
    public async Task Reseller_is_refused_real_or_restricted_source_before_egress()
    {
        var client = new RecordedClient("reseller", failures: 0, isReseller: true);
        var parser = new ProviderNeutralExamSourceParser([client], new Metric(), new("reseller", "p1"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            parser.ParseAsync(Source(ImportDataClassification.RightsCleared), default));

        Assert.Empty(client.Requests);
    }

    private static ExtractedImportSource Source(ImportDataClassification classification) =>
        new("synthetic.txt", "text/plain", "synthetic", new string('a', 64),
            ExamImportWorkflow.Hash("synthetic"), classification);

    private sealed class RecordedClient(string provider, int failures, bool isReseller = false)
        : IStructuredExamAiClient
    {
        public string Provider => provider;
        public bool IsReseller => isReseller;
        public List<StructuredParseRequest> Requests { get; } = [];

        public Task<StructuredParseResponse> ParseAsync(StructuredParseRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            if (Requests.Count <= failures) throw new TransientAiParserException("recorded transient");
            return Task.FromResult(new StructuredParseResponse(
                "{}", "recorded-model", "req-123", 100, 20, 0.001m));
        }
    }

    private sealed class Metric : IAiImportCostMetric
    {
        public int Calls { get; private set; }
        public void Record(string provider, string model, long inputTokens, long outputTokens, decimal cost) => Calls++;
    }
}
