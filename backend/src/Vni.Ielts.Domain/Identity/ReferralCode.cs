using System.Security.Cryptography;

namespace Vni.Ielts.Domain.Identity;

/// <summary>
/// The code a learner shares so that a registration can be attributed to
/// them. → `P-16`
///
/// <para>
/// <b>Short, URL-safe, and unambiguous when read aloud.</b> It travels in a
/// link and gets typed from a screenshot, so the alphabet drops the pairs
/// people confuse — <c>0/O</c>, <c>1/I/L</c> — and there is no punctuation to
/// escape. Ten characters over thirty-one symbols is about 2^49 possibilities:
/// unguessable in practice, short enough to read out.
/// </para>
///
/// <para>
/// <b>Not a secret.</b> Knowing somebody's code lets you credit them, which is
/// the point. What it must not be is <i>predictable</i>, or a script could
/// credit itself against every account it can enumerate — hence the CSPRNG
/// rather than a hash of the user id.
/// </para>
/// </summary>
public static class ReferralCode
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 10;

    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>
    /// What a typed or pasted code becomes before it is looked up. Case and
    /// surrounding whitespace are the learner's business, not the lookup's.
    /// Returns null when nothing usable was given.
    /// </summary>
    public static string? Normalise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim().ToUpperInvariant();
        return value.Length is 0 or > 64 ? null : value;
    }
}
