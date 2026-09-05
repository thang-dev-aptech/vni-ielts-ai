namespace Vni.Ielts.Domain.Identity;

/// <summary>
/// A phone number the learner typed, normalised so two spellings of the same
/// number compare equal.
///
/// <para>
/// <b>Self-declared, and deliberately not verified.</b> There is no OTP behind
/// this and no requirement asking for one — whether a number must be proven is
/// a business decision nobody has taken. So this type carries no notion of
/// "verified", and no code should grow one here by accident: a number that
/// looks verified without being verified is worse than one that is plainly
/// just typed in. → `[BUSINESS DECISION]`, next-actions.md
/// </para>
///
/// <para>
/// <b>Vietnamese first, not Vietnamese only.</b> `0912345678` and
/// `+84912345678` are the same number and both normalise to the second form,
/// because that is how the audience writes it. Other country codes are stored
/// as given rather than rejected — this product will have foreign teachers and
/// parents long before it has a reason to police dialling plans.
/// `[ASSUMPTION]`
/// </para>
/// </summary>
public readonly record struct PhoneNumber
{
    private PhoneNumber(string value) => Value = value;

    public string Value { get; }

    public override string ToString() => Value;

    public static bool TryCreate(string? raw, out PhoneNumber phone)
    {
        phone = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // Spaces, dots, dashes and brackets are how people write a number, not
        // part of it. Stripping them is what makes "091 234 56 78" and
        // "0912-345-678" the same value rather than two accounts' worth of
        // confusion later.
        var digits = new string([.. raw.Where(c => char.IsAsciiDigit(c) || c == '+')]);
        if (digits.Length == 0) return false;

        // A leading + may only be first, and only once.
        if (digits.LastIndexOf('+') > 0) return false;

        var national = digits.TrimStart('+');
        if (national.Length is < 8 or > 15) return false;

        var normalised = digits.StartsWith('+')
            ? digits
            // A domestic number starts with a trunk 0 that the international
            // form drops: 0912345678 → +84912345678.
            : national.StartsWith('0')
                ? "+84" + national[1..]
                : "+" + national;

        phone = new PhoneNumber(normalised);
        return true;
    }

    public static PhoneNumber Create(string raw) =>
        TryCreate(raw, out var phone)
            ? phone
            : throw new ArgumentException($"Not a usable phone number: '{raw}'.", nameof(raw));
}
