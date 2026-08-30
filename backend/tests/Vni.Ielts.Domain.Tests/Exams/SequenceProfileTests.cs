using Vni.Ielts.Domain.Exams;

namespace Vni.Ielts.Domain.Tests.Exams;

public sealed class SequenceProfileTests
{
    private static readonly HashSet<ExamModule> AllFour =
    [
        ExamModule.Reading, ExamModule.Listening, ExamModule.Writing, ExamModule.Speaking,
    ];

    [Fact]
    public void Absent_profile_resolves_to_canonical_order()
    {
        var resolved = SequenceProfile.Resolve(null, AllFour);

        Assert.Equal(SequenceProfile.CanonicalOrder, resolved);
    }

    [Fact]
    public void Declared_listening_first_is_honoured()
    {
        var declared = new[]
        {
            ExamModule.Listening, ExamModule.Reading, ExamModule.Writing, ExamModule.Speaking,
        };

        var resolved = SequenceProfile.Resolve(declared, AllFour);

        Assert.Equal(declared, resolved);
    }

    [Fact]
    public void Partial_version_filters_canonical_order()
    {
        var present = new HashSet<ExamModule> { ExamModule.Reading, ExamModule.Writing };

        var resolved = SequenceProfile.Resolve(null, present);

        Assert.Equal([ExamModule.Reading, ExamModule.Writing], resolved);
    }

    [Fact]
    public void IsFullMock_requires_all_four_skills()
    {
        Assert.True(SequenceProfile.IsFullMock(AllFour));
        Assert.False(SequenceProfile.IsFullMock(new HashSet<ExamModule> { ExamModule.Reading }));
    }
}
