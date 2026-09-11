using System.Diagnostics.Metrics;

namespace Vni.Ielts.Infrastructure.Observability;

/// <summary>
/// Low-cardinality operational metrics for AI-assisted exam parsing.
///
/// <para>
/// Package ids, correlation ids, source paths and source content are deliberately
/// absent from tags. They would both leak import details and create an unbounded
/// metric series. Provider and stable failure code are the dimensions operators
/// need to distinguish an outage from a rejected response.
/// </para>
/// </summary>
public sealed class ExamParsingMetrics
{
    private static readonly Histogram<double> Duration = Telemetry.Meter.CreateHistogram<double>(
        "vni.exam_parsing.duration",
        unit: "s",
        description: "How long one exam extraction provider attempt took.");

    private static readonly Counter<long> Failures = Telemetry.Meter.CreateCounter<long>(
        "vni.exam_parsing.failures",
        unit: "{failure}",
        description: "Exam extraction attempts that failed, tagged by provider and stable failure code.");

    private static readonly Counter<long> InputTokens = Telemetry.Meter.CreateCounter<long>(
        "vni.exam_parsing.tokens.input",
        unit: "{token}",
        description: "Input tokens reported for accepted exam extraction responses.");

    private static readonly Counter<long> OutputTokens = Telemetry.Meter.CreateCounter<long>(
        "vni.exam_parsing.tokens.output",
        unit: "{token}",
        description: "Output tokens reported for accepted exam extraction responses.");

    public void RecordDuration(double seconds, string provider) =>
        Duration.Record(seconds, ProviderTag(provider));

    public void RecordFailure(string provider, string failureCode) =>
        Failures.Add(
            1,
            ProviderTag(provider),
            new KeyValuePair<string, object?>("failure_code", failureCode));

    public void RecordTokens(string provider, long inputTokens, long outputTokens)
    {
        InputTokens.Add(inputTokens, ProviderTag(provider));
        OutputTokens.Add(outputTokens, ProviderTag(provider));
    }

    private static KeyValuePair<string, object?> ProviderTag(string provider) =>
        new("provider", provider);
}
