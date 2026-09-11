using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Exams;
using Vni.Ielts.Infrastructure.Persistence.Exams;

namespace Vni.Ielts.Infrastructure.Tests.Persistence;

/// <summary>
/// Feature named this field <c>CreatedBy</c>; main persists it as
/// <see cref="ExamVersion.AuthorId"/>. Round-trip must keep ownership.
/// </summary>
public sealed class ExamAuthorIdMappingTests
{
    [Fact]
    public void AuthorId_survives_document_round_trip()
    {
        var owner = new UserId("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var version = ExamVersion.CreateBlankDraft(
            ExamDefinitionId.New(), 1, "Owned draft", ExamVariant.Academic, owner);

        var roundTrip = version.ToDocument().ToDomain();

        Assert.Equal(owner, roundTrip.AuthorId);
    }
}
