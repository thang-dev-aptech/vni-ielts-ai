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

    [Theory]
    [InlineData("912345678")]
    [InlineData("+0912345678")]
    public void A_spelling_that_could_be_two_different_numbers_is_refused(string typed)
    {
        /*
         * `912345678` is either a Vietnamese mobile typed without its trunk 0
         * or an Indian number whose country code is 91. The old code assumed
         * the second and produced `+912345678`, so a learner who dropped the
         * leading zero silently got a *second* account — discoverable only
         * once their history went missing from it. That was tolerable while
         * this was contact information; it is not tolerable now the number is
         * the handle people sign in with. `+0…` is refused for the same
         * reason: no country code starts with zero, so it can only ever be a
         * second spelling of a number already stored.
         */
        Assert.False(PhoneNumber.TryCreate(typed, out _));
    }

    [Fact]
    public void A_country_code_written_over_the_trunk_zero_is_still_the_same_number()
    {
        // How a Vietnamese number gets written by someone who prefixes +84
        // without dropping the 0. Left alone it is a third spelling.
        Assert.True(PhoneNumber.TryCreate("+84 091 234 5678", out var phone));
        Assert.Equal("+84912345678", phone.Value);
    }

    [Fact]
    public void A_number_can_be_removed_again()
    {
        // The only way back out for someone who typed the wrong one — but only
        // while the account keeps some other way in.
        var user = User.RegisterFromProvider(
            Email.Create("a@example.com"), "Hoc vien", DateTimeOffset.UnixEpoch);
        user.SetPhone(PhoneNumber.Create("0912345678"), hasLinkedProvider: false);

        user.SetPhone(null, hasLinkedProvider: false);

        Assert.Null(user.Phone);
    }

    [Fact]
    public void A_learner_with_no_email_cannot_clear_their_phone()
    {
        // Registration takes a phone and no address, so this is the ordinary
        // account — and its number is the only thing anyone can type to reach
        // it. Clearing it looks like an ordinary profile edit and locks the
        // account shut for good.
        var user = User.Register(PhoneNumber.Create("0912345678"), "Hoc vien", DateTimeOffset.UnixEpoch);

        Assert.Throws<InvalidOperationException>(
            () => user.SetPhone(null, hasLinkedProvider: false));

        Assert.Equal("+84912345678", user.Phone!.Value.Value);
    }

    [Fact]
    public void The_guard_counts_a_linked_provider_as_a_way_in()
    {
        // An account created through Google has neither number nor password;
        // the link is its door, and the invariant has to know that.
        var user = User.Register(PhoneNumber.Create("0912345678"), "Hoc vien", DateTimeOffset.UnixEpoch);

        user.SetPhone(null, hasLinkedProvider: true);

        Assert.Null(user.Phone);
    }
}
