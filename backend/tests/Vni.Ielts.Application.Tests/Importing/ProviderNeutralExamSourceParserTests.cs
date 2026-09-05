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

    /// <summary>
    /// <b>A restricted source never reaches a reseller, and the refusal happens
    /// before the call rather than after it.</b> After it would mean the
    /// document had already been sent, which is the entire event being
    /// prevented — hence the assertion on <c>Requests</c> as well as on the
    /// exception.
    /// </summary>
    [Fact]
    public async Task Reseller_is_refused_a_restricted_source_before_the_call()
    {
        var client = new RecordedClient("reseller", failures: 0, isReseller: true);
        var parser = new ProviderNeutralExamSourceParser([client], new Metric(), new("reseller", "p1"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            parser.ParseAsync(Source(ImportDataClassification.Restricted), default));

        Assert.Empty(client.Requests);
    }

    /// <summary>
    /// <b>Rights-cleared material does reach a reseller, and that changed on
    /// 2026-09-02.</b> This test asserted the opposite until then, because until
    /// then only synthetic sources passed — which meant no real paper could be
    /// imported at all.
    ///
    /// The property being kept is that the classification is what decides, and
    /// that <see cref="ImportDataClassification.Restricted"/> — what
    /// <c>SafeSourceDocumentExtractor</c> stamps on every upload — still does
    /// not pass. Clearing a source stays a deliberate act by whoever holds the
    /// rights.
    /// </summary>
    [Theory]
    [InlineData(ImportDataClassification.Synthetic)]
    [InlineData(ImportDataClassification.RightsCleared)]
    public async Task Reseller_accepts_synthetic_and_rights_cleared_sources(
        ImportDataClassification classification)
    {
        var client = new RecordedClient("reseller", failures: 0, isReseller: true);
        var parser = new ProviderNeutralExamSourceParser([client], new Metric(), new("reseller", "p1"));

        await parser.ParseAsync(Source(classification), default);

        Assert.Single(client.Requests);
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
