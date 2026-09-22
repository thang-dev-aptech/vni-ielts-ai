using Xunit;
using Vni.Ielts.Application.Exams;
using Vni.Ielts.Application.Media;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Application.Tests.Media;

/// <summary>
/// Test the extraction of media-version references from exam content.
/// </summary>
public sealed class MediaVersionReferencesTests
{
    [Fact]
    public async Task ExtracsAudioAndImageReferencesFromExamVersions()
    {
        // Arrange: Create an exam version that references media assets
        var audioRef = "media/audio-abc123";
        var imageRef = "media/image-xyz789";

        var part = new SectionPart(
            Order: 1,
            Kind: "passage",
            Title: "Part 1",
            Body: null,
            AudioKey: audioRef,
            ImageKey: imageRef,
            Transcript: null,
            TaskNumber: null,
            PartNumber: 1,
            CueCard: null,
            MinWords: null,
            Questions: new List<Question>());

        var section = new Section(ExamModule.Reading, 1, new[] { part });
        var version = ExamVersion.CreateDraft(
            ExamDefinitionId.New(),
            versionNumber: 1,
            title: "Test Paper",
            variant: ExamVariant.Academic,
            scoring: new ScoringProfile(
                new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            timing: new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            sections: new[] { section });

        // Create a fake catalogue
        var catalogue = new FakeCatalogue(new[] { version });
        var handler = new ListMediaVersionReferences(catalogue);

        // Act
        var mapping = await handler.HandleAsync(CancellationToken.None);

        // Assert
        Assert.Contains("audio-abc123", mapping.Keys);
        Assert.Contains("image-xyz789", mapping.Keys);

        var audioRefs = mapping["audio-abc123"];
        Assert.Single(audioRefs);
        Assert.Equal("Test Paper", audioRefs[0].Title);
        Assert.Equal("Draft", audioRefs[0].State);
    }

    [Fact]
    public async Task DistinguishesBetweenPublishedAndDraftVersions()
    {
        // Arrange: Create published and draft versions referencing the same asset
        var mediaRef = "media/shared-audio";
        var definitionId = ExamDefinitionId.New();

        var part = new SectionPart(
            Order: 1,
            Kind: "passage",
            Title: null,
            Body: null,
            AudioKey: mediaRef,
            ImageKey: null,
            Transcript: null,
            TaskNumber: null,
            PartNumber: 1,
            CueCard: null,
            MinWords: null,
            Questions: new List<Question>());

        var section = new Section(ExamModule.Listening, 1, new[] { part });

        var draftVersion = ExamVersion.CreateDraft(
            definitionId,
            versionNumber: 1,
            title: "Draft Paper",
            variant: ExamVariant.Academic,
            scoring: new ScoringProfile(
                new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            timing: new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            sections: new[] { section });

        var publishedVersion = ExamVersion.Rehydrate(
            ExamVersionId.New(),
            definitionId,
            versionNumber: 2,
            title: "Published Paper",
            variant: ExamVariant.Academic,
            status: ExamVersionStatus.Published,
            publishedAt: DateTimeOffset.UtcNow,
            scoring: new ScoringProfile(
                new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            timing: new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            sections: new[] { section });

        var catalogue = new FakeCatalogue(new[] { draftVersion, publishedVersion });
        var handler = new ListMediaVersionReferences(catalogue);

        // Act
        var mapping = await handler.HandleAsync(CancellationToken.None);

        // Assert
        var refs = mapping["shared-audio"];
        Assert.Equal(2, refs.Count);
        Assert.Contains(refs, r => r.State == "Draft");
        Assert.Contains(refs, r => r.State == "Published");
    }

    private sealed class FakeCatalogue(IReadOnlyList<ExamVersion> versions) : IExamCatalogue
    {
        public Task<IReadOnlyList<ExamVersion>> ListSittableAsync(CancellationToken ct) =>
            Task.FromResult(versions.Where(v => v.IsSittable).ToList() as IReadOnlyList<ExamVersion>);

        public Task<IReadOnlyList<ExamVersion>> ListAllAsync(CancellationToken ct) =>
            Task.FromResult(versions);

        public Task<ExamVersion?> FindAsync(ExamVersionId id, CancellationToken ct) =>
            Task.FromResult(versions.FirstOrDefault(v => v.Id == id));

        public Task<IReadOnlyDictionary<ExamVersionId, ExamVersion>> FindManyAsync(
            IReadOnlyCollection<ExamVersionId> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<ExamVersionId, ExamVersion>>(
                versions.Where(v => ids.Contains(v.Id))
                    .ToDictionary(v => v.Id) as IReadOnlyDictionary<ExamVersionId, ExamVersion>);

        public Task UpsertAsync(ExamVersion version, CancellationToken ct) =>
            Task.CompletedTask;

        public Task SetStatusAsync(ExamVersionId id, ExamVersionStatus status, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
