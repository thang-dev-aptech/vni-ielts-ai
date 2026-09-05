using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Infrastructure.Ai.Writing;

namespace Vni.Ielts.Infrastructure.Tests.Ai.Writing;

public sealed class WritingGoldenSeedTests
{
    [Theory]
    [InlineData("task2-opinion-band6", "OpenAi")]
    [InlineData("task1-chart-band65", "Gemini")]
    public async Task Recorded_provider_fixtures_validate_and_mark(string seedId, string providerLabel)
    {
        var seed = WritingGoldenSeeds.All.Single(s => s.Id == seedId);
        var client = new RecordedWritingEvaluationClient(providerLabel, seed.ProviderFixture);
        var essay = await File.ReadAllTextAsync(Find(seed.EssayFixture));

        var response = await client.EvaluateAsync(
            new WritingEvaluationRequest(
                seed.TaskPrompt, essay,
                WritingEvaluationPromptBuilder.CountWords(essay),
                seed.MinWords,
                "ielts-writing-synthetic-v1", "golden-seed", "descriptors", "writing-eval-prompt-v1",
                seed.Id, 1),
            default);

        var claim = WritingEvaluationValidator.ToClaimedEvaluation(response.Json);
        var rubric = Rubric.Create(
            "ielts-writing-synthetic-v1", Domain.Exams.ExamModule.Writing,
            CriterionKeys.Writing, "golden-seed");

        var marking = CriterionMarking.Mark(rubric, claim.Criteria, claim.ReportedBand, essay, 1);

        Assert.Equal(seed.ReferenceOverall, marking.Band.Value);
        Assert.Equal(seed.ReferenceTaskResponse,
            marking.Criteria.First(c => c.Criterion == CriterionKeys.TaskResponse).Band.Value);
    }

    private static string Find(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
