namespace Vni.Ielts.Domain.Identity;

/// <summary>
/// A syntactically valid email address, normalised for comparison.
///
/// Normalisation matters more here than it looks. Account linking compares
/// addresses (M-1, threat T1), and if <c>Ann@Example.com</c> and
/// <c>ann@example.com</c> compare as different, one person ends up with two
/// accounts; if they compare as the same without verification, an attacker
/// controlling a social account bearing a victim's address inherits the
/// victim's account.
///
/// So: normalise consistently, and never link on a match <i>alone</i>. M-1 was
/// resolved on 2026-08-21 toward one account per address, which makes this
/// comparison load-bearing — but a match only links when the identity provider
/// itself asserts the address is verified. → ADR-0013
/// </summary>
public readonly record struct Email
{
    private Email(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    public static bool TryCreate(string? raw, out Email email)
    {
        email = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var trimmed = raw.Trim();
        if (trimmed.Length > 254)
            return false;

        // Deliberately not a full RFC 5322 parser. Those accept addresses no
        // provider will deliver to, and reject some that are fine. The real
        // proof that an address exists is the verification email.
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@') || at == trimmed.Length - 1)
            return false;

        var domain = trimmed[(at + 1)..];
        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.'))
            return false;
        if (trimmed.Contains(' '))
            return false;

        email = new Email(trimmed.ToLowerInvariant());
        return true;
    }

    public static Email Create(string raw) =>
        TryCreate(raw, out var email)
            ? email
            : throw new ArgumentException($"Not a valid email address: '{raw}'.", nameof(raw));
}
