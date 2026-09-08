using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Application.Tests.Exams;

/// <summary>
/// Catalogue delete is a hard remove — the status gate lives at the API.
/// These tests lock the port contract the Mongo adapter mirrors.
/// </summary>
public sealed class ExamCatalogueDeleteTests
{
    [Fact]
    public async Task DeleteAsync_removes_the_version()
    {
        var draft = ExamVersion.CreateDraft(
            ExamDefinitionId.New(), 1, "Temporary", ExamVariant.Academic,
            new ScoringProfile(new Dictionary<ExamModule, IReadOnlyList<BandBoundary>>(), AnswerMatchingRules.Default),
            new TimingProfile(new Dictionary<ExamModule, int>(), null, []),
            [],
            authorId: UserId.New());

        var catalogue = new FakeExamCatalogue(draft);
        await catalogue.DeleteAsync(draft.Id, CancellationToken.None);

        Assert.Null(await catalogue.FindAsync(draft.Id, CancellationToken.None));
    }
}
