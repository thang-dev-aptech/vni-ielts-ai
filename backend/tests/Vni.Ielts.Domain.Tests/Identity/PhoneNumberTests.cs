using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Domain.Tests.Identity;

/// <summary>
/// Phone numbers, normalised so two spellings of one number are one value.
///
/// The point is not validation for its own sake — it is that `091 234 5678`
/// and `+84912345678` must not be able to sit in a database as two different
/// contact details for the same person.
/// </summary>
public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("0912345678")]
    [InlineData("091 234 5678")]
    [InlineData("091-234-5678")]
    [InlineData("(091) 234.5678")]
    [InlineData("+84912345678")]
    [InlineData("+84 912 345 678")]
    public void Every_way_of_writing_one_number_gives_the_same_value(string typed)
    {
        // The trunk 0 a domestic number starts with is exactly what the
        // international form drops.
        Assert.True(PhoneNumber.TryCreate(typed, out var phone));
        Assert.Equal("+84912345678", phone.Value);
    }

    [Fact]
    public void Two_spellings_of_one_number_compare_equal()
    {
        PhoneNumber.TryCreate("0912345678", out var typed);
        PhoneNumber.TryCreate("+84 912 345 678", out var pasted);

        Assert.Equal(typed, pasted);
    }

    [Theory]
    [InlineData("+6591234567", "+6591234567")]
    [InlineData("+1 415 555 0100", "+14155550100")]
    public void A_foreign_number_is_kept_rather_than_rejected(string typed, string expected)
    {
        // This product will have foreign teachers and parents long before it
        // has a reason to police dialling plans. → `[ASSUMPTION]`
        Assert.True(PhoneNumber.TryCreate(typed, out var phone));
        Assert.Equal(expected, phone.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("khong-phai-so")]
    [InlineData("12345")]
    [InlineData("1234567890123456")]
    [InlineData("091+2345678")]
    public void Anything_that_is_not_a_usable_number_is_refused(string? typed)
    {
        Assert.False(PhoneNumber.TryCreate(typed, out _));
    }

    [Fact]
    public void A_number_can_be_removed_again()
    {
        // The only way back out for someone who typed the wrong one.
        var user = User.Register(Email.Create("a@example.com"), "Học viên", DateTimeOffset.UnixEpoch);
        user.SetPhone(PhoneNumber.Create("0912345678"));

        user.SetPhone(null);

        Assert.Null(user.Phone);
    }
}
