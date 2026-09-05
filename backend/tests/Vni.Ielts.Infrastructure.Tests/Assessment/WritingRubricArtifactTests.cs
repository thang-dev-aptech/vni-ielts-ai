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
