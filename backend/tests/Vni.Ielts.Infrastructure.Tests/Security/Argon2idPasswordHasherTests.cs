using Vni.Ielts.Infrastructure.Security;

namespace Vni.Ielts.Infrastructure.Tests.Security;

/// <summary>
/// Slow by design — each case derives a real Argon2id hash at 19 MiB. Kept few
/// and specific for that reason; the fast behavioural coverage lives in the
/// use-case tests, which is exactly why IPasswordHasher is a port.
/// </summary>
public sealed class Argon2idPasswordHasherTests
{
    private readonly Argon2idPasswordHasher _sut = new();

    [Fact]
    public void A_password_verifies_against_its_own_hash()
    {
        var hash = _sut.Hash("correct-horse-battery-staple");
        Assert.True(_sut.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void A_different_password_does_not_verify()
    {
        var hash = _sut.Hash("correct-horse-battery-staple");
        Assert.False(_sut.Verify("correct-horse-battery-stapl", hash));
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // Distinct salts. Without this, identical passwords across accounts
        // are visibly identical in a stolen database dump, and one cracked
        // hash reveals every account that shared it.
        var a = _sut.Hash("same-password-here");
        var b = _sut.Hash("same-password-here");

        Assert.NotEqual(a, b);
        Assert.True(_sut.Verify("same-password-here", a));
        Assert.True(_sut.Verify("same-password-here", b));
    }

    [Fact]
    public void The_encoded_hash_carries_its_parameters()
    {
        // This is what lets the cost be raised later without invalidating
        // every stored hash.
        var hash = _sut.Hash("some-password-value");
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$argon2id$v=19$broken")]
    [InlineData("$argon2id$v=19$m=x,t=y,p=z$c2FsdA$aGFzaA")]
    public void A_malformed_stored_hash_fails_verification_instead_of_throwing(string malformed)
    {
        // A corrupted record must not take the login endpoint down.
        Assert.False(_sut.Verify("any-password", malformed));
    }

    [Fact]
    public void Vietnamese_characters_survive_the_round_trip()
    {
        // UTF-8 encoding, not ASCII. A password of "mật khẩu tiếng Việt" must
        // not silently become a different byte sequence between hash and verify.
        const string password = "mật-khẩu-tiếng-Việt-2026";
        var hash = _sut.Hash(password);
        Assert.True(_sut.Verify(password, hash));
    }
}
