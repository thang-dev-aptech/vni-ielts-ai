using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Infrastructure.Assessment;

namespace Vni.Ielts.Infrastructure.Ai.Writing;

/// <summary>
/// Selects primary/fallback writing clients and retries transient failures.
/// </summary>
public sealed class WritingEvaluationRouter(
    IEnumerable<IWritingEvaluationClient> clients,
    IOptions<AssessmentOptions> assessment,
    IOptions<AiOptions> aiOptions,
    IWritingEvaluationCostMetric costs,
    ILogger<WritingEvaluationRouter> logger)
{
    public async Task<WritingEvaluationResponse> EvaluateAsync(
        WritingEvaluationRequest request, AiEgressTicket ticket, CancellationToken ct)
    {
        var options = assessment.Value.WritingMarking;
        var order = ProviderOrder(options);

        var attempts = 0;
        Exception? last = null;

        foreach (var provider in order)
        {
            var client = clients.FirstOrDefault(c =>
                string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase));

            if (client is null) continue;

            var scopedTicket = TicketFor(provider, ticket);

            for (var providerAttempt = 0; providerAttempt < options.MaxAttempts; providerAttempt++)
            {
                attempts++;
                var attemptRequest = request with { Attempt = attempts };

                try
                {
                    SetTicketScope(client, scopedTicket);
                    var response = await client.EvaluateAsync(attemptRequest, ct);
                    costs.Record(response.Provider, response.Model, response.InputTokens, response.OutputTokens);
                    return response;
                }
                catch (TransientWritingEvaluationException e)
                {
                    last = e;
                    logger.LogWarning(
                        e,
                        "Transient writing evaluation failure from {Provider}, attempt {Attempt}.",
                        provider,
                        attempts);
                }
                finally
                {
                    ClearTicketScope(client);
                }
            }
        }

        throw last ?? new MarkingRejectedException("No writing evaluation provider is configured.");
    }

    private AiEgressTicket TicketFor(string provider, AiEgressTicket primaryTicket) =>
        string.Equals(provider, primaryTicket.ProviderSection, StringComparison.OrdinalIgnoreCase)
            ? primaryTicket
            : AiEgress.Authorise(aiOptions.Value, provider, primaryTicket.Classification);

    private static IReadOnlyList<string> ProviderOrder(WritingMarkingOptions options)
    {
        var list = new List<string>();

        if (!string.IsNullOrWhiteSpace(options.PrimaryProvider))
            list.Add(options.PrimaryProvider);

        if (!string.IsNullOrWhiteSpace(options.FallbackProvider)
            && !list.Contains(options.FallbackProvider, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(options.FallbackProvider);
        }

        return list;
    }

    private static void SetTicketScope(IWritingEvaluationClient client, AiEgressTicket ticket)
    {
        if (client is OpenAiWritingEvaluationClient)
            OpenAiWritingEvaluationClient.CurrentTicket.Value = ticket;
        else if (client is GeminiWritingEvaluationClient)
            GeminiWritingEvaluationClient.CurrentTicket.Value = ticket;
    }

    private static void ClearTicketScope(IWritingEvaluationClient client)
    {
        if (client is OpenAiWritingEvaluationClient)
            OpenAiWritingEvaluationClient.CurrentTicket.Value = null;
        else if (client is GeminiWritingEvaluationClient)
            GeminiWritingEvaluationClient.CurrentTicket.Value = null;
    }
}

public sealed class NullWritingEvaluationCostMetric : IWritingEvaluationCostMetric
{
    public void Record(string provider, string model, long inputTokens, long outputTokens) { }
}
