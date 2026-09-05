using Vni.Ielts.Infrastructure.Storage;

namespace Vni.Ielts.Infrastructure.Tests.Storage;

/// <summary>
/// The prefix seam behind `[QUYẾT ĐỊNH]` chủ sản phẩm 04/09/2026 — one bucket,
/// one folder per class (ADR-0016).
///
/// <b>A prefix is configuration that becomes part of every object key</b>, so
/// it gets the same treatment the key validators give a package's own paths:
/// one normal form, and a refusal for anything that could step outside its
/// folder. These tests pin both, and pin that the empty prefix — every
/// deployment before 2026-09-04 — still produces exactly the old keys.
/// </summary>
public sealed class ObjectKeyPrefixTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("examassets", "examassets/")]
    [InlineData("examassets/", "examassets/")]
    [InlineData("/examassets/", "examassets/")]
    [InlineData(" dev/examassets ", "dev/examassets/")]
    public void A_prefix_has_one_normal_form(string? configured, string expected)
    {
        Assert.Equal(expected, ObjectStorageOptions.NormalisePrefix(configured));
    }

    [Theory]
    [InlineData("a//b")]
    [InlineData("./examassets")]
    [InlineData("examassets/..")]
    [InlineData("a/../b")]
    public void A_prefix_that_could_leave_its_folder_is_refused(string configured)
    {
        Assert.Throws<ArgumentException>(() => ObjectStorageOptions.NormalisePrefix(configured));
    }

    [Fact]
    public void The_empty_prefix_leaves_the_key_exactly_as_it_was()
    {
        // The compatibility guarantee: a bucket laid out before the prefix
        // existed is read with the same keys it was written with.
        Assert.Equal("listening-part-1.m4a", ObjectStorageOptions.Under("", "listening-part-1.m4a"));
        Assert.Equal("recordings/s/q.webm", ObjectStorageOptions.Under(null, "recordings/s/q.webm"));
    }

    [Fact]
    public void A_key_lands_under_its_class_folder()
    {
        Assert.Equal(
            "examassets/cam16-test-1-listening-part1.mp3",
            ObjectStorageOptions.Under("examassets", "cam16-test-1-listening-part1.mp3"));
        Assert.Equal(
            "speakingrecord/recordings/s/q.webm",
            ObjectStorageOptions.Under("speakingrecord/", "recordings/s/q.webm"));
        Assert.Equal(
            "examassets/imports/2026/x.png",
            ObjectStorageOptions.Under("examassets", "imports/2026/x.png"));
    }
}
