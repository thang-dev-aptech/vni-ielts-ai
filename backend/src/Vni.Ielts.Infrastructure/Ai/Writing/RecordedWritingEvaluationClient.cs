using System.Text.Json;

namespace Vni.Ielts.Infrastructure.Ai.Writing;

/// <summary>
/// Returns canned provider JSON from fixtures — no network, no credentials.
/// </summary>
public sealed class RecordedWritingEvaluationClient(string provider, string fixturePath) : IWritingEvaluationClient
{
    public string Provider { get; } = provider;

    public Task<WritingEvaluationResponse> EvaluateAsync(
        WritingEvaluationRequest request, CancellationToken ct)
    {
        var path = Resolve(fixturePath);
        var json = File.ReadAllText(path);

        // Validate the fixture itself matches the contract under test.
        WritingEvaluationValidator.EnsureSchemaValid(json);

        return Task.FromResult(new WritingEvaluationResponse(
            json,
            Provider,
            $"recorded-{Provider.ToLowerInvariant()}",
            request.IdempotencyKey,
            InputTokens: 0,
            OutputTokens: 0));
    }

    private static string Resolve(string fixturePath)
    {
        if (Path.IsPathRooted(fixturePath) && File.Exists(fixturePath)) return fixturePath;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fixturePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Recorded writing evaluation fixture not found: {fixturePath}");
    }
}

/// <summary>Fixture catalogue for golden-seed regression tests.</summary>
public static class WritingGoldenSeeds
{
    public sealed record Seed(
        string Id,
        string ProviderFixture,
        string EssayFixture,
        string TaskPrompt,
        int? MinWords,
        decimal? ReferenceTaskResponse,
        decimal? ReferenceOverall);

    public static IReadOnlyList<Seed> All { get; } =
    [
        new(
            "task2-opinion-band6",
            "fixtures/ai/writing/openai-band6-task2.json",
            "fixtures/ai/writing/essays/task2-opinion-band6.txt",
            "Some people think governments should fund public transport. Discuss both views and give your opinion.",
            250,
            6m,
            6m),
        new(
            "task1-chart-band65",
            "fixtures/ai/writing/gemini-band65-task1.json",
            "fixtures/ai/writing/essays/task1-chart-band65.txt",
            "Summarise the information by selecting and reporting the main features, and make comparisons where relevant.",
            150,
            6.5m,
            6.5m),
    ];
}
