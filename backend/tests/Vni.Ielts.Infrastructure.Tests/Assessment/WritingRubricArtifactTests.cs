using Vni.Ielts.Domain.Assessment;
using Vni.Ielts.Infrastructure.Assessment;

namespace Vni.Ielts.Infrastructure.Tests.Assessment;

public sealed class WritingRubricArtifactTests
{
    [Fact]
    public void Default_fixture_loads_with_matching_hash()
    {
        var artifact = WritingRubricLoader.Load(
            null,
            "sha256:ee8088bebe50fbbe19430452ef06cd95f72638d50eaecb92570e486d0b047d81");

        Assert.Equal("ielts-writing-synthetic-v1", artifact.Version);
        Assert.Equal("writing-eval-prompt-v1", artifact.PromptVersion);
        Assert.Equal(4, artifact.Criteria.Count);
        Assert.True(artifact.Descriptors.ContainsKey("taskResponse"));
        Assert.False(artifact.IsV2);
    }

    [Fact]
    public void V2_fixture_loads_with_separate_task_criterion_sets()
    {
        var artifact = WritingRubricLoader.Load(
            "fixtures/assessment/writing-rubric-v2.json", expectedHash: null);

        Assert.True(artifact.IsV2);
        Assert.Equal("vni-writing-v2", artifact.Version);
        Assert.Equal("vni-authored", artifact.DescriptorSource);
        Assert.Equal(CriterionKeys.WritingTask1, WritingRubricLoader.CriteriaFor(artifact, 1));
        Assert.Equal(CriterionKeys.WritingTask2, WritingRubricLoader.CriteriaFor(artifact, 2));

        var academic = WritingRubricLoader.FormatDescriptorsForPrompt(artifact, 1, generalTraining: false);
        var gt = WritingRubricLoader.FormatDescriptorsForPrompt(artifact, 1, generalTraining: true);
        var task2 = WritingRubricLoader.FormatDescriptorsForPrompt(artifact, 2, generalTraining: false);

        Assert.Contains("graphic", academic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("letter fully meets", gt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("letter fully meets", academic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("whole graphic", gt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("taskResponse", task2, StringComparison.Ordinal);
        Assert.DoesNotContain("taskAchievement", task2, StringComparison.Ordinal);
    }

    [Fact]
    public void Descriptor_source_that_claims_the_official_scale_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vni-rubric-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "version": "bad",
              "descriptorSource": "official IELTS band descriptors",
              "effectiveDate": "2026-09-09",
              "contentHash": "sha256:pending",
              "promptVersion": "x",
              "criteria": ["taskResponse"],
              "descriptors": { "taskResponse": { "9": "x" } }
            }
            """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                WritingRubricLoader.Load(path, expectedHash: null));
            Assert.Contains("official", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Hash_mismatch_is_refused()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            WritingRubricLoader.Load(null, "sha256:deadbeef"));

        Assert.Contains("hash mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Descriptor_prompt_includes_every_criterion()
    {
        var artifact = WritingRubricLoader.Load(null, expectedHash: null);
        var prompt = WritingRubricLoader.FormatDescriptorsForPrompt(artifact);

        foreach (var criterion in artifact.Criteria)
            Assert.Contains(criterion, prompt, StringComparison.Ordinal);
    }
}
