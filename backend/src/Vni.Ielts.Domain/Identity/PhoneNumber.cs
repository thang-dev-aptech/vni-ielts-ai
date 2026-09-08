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
/// just typed in. → `[BUSINESS DECISION]`
/// </para>
///
/// <para>
/// <b>Unverified, but no longer merely contact information.</b> Registration
/// takes a phone number instead of an address and sign-in accepts one, so this
/// value is now an account handle and the repository holds a unique index over
/// it. That is what tightened the rules below: while a number was something a
/// learner typed into their profile for a human to read, an ambiguous spelling
/// cost nothing. As a handle, an ambiguous spelling costs one person two
/// accounts — and they will only find out when their history is missing from
/// the one they just signed in to.
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

        var international = digits.StartsWith('+');
        var national = international ? digits[1..] : digits;

        if (national.Length is < 8 or > 15) return false;

        if (international)
        {
            // `+0…` is not a number in any plan: E.164 country codes never
            // start with zero. It used to be stored verbatim, which meant
            // `+0912345678` sat in the collection next to `+84912345678` as a
            // separate account belonging to the same person.
            if (national.StartsWith('0')) return false;

            // `+84 091 234 5678` is how a Vietnamese number gets written when
            // somebody prefixes the country code without dropping the trunk 0.
            // Left alone it is a second spelling of a number we already store.
            if (national.StartsWith("840", StringComparison.Ordinal))
                return Ok("+84" + national[3..], out phone);

            return Ok(digits, out phone);
        }

        // A domestic number carries the trunk 0 that the international form
        // drops: 0912345678 → +84912345678.
        if (national.StartsWith('0'))
            return Ok("+84" + national[1..], out phone);

        /*
         * Everything else is ambiguous and is refused rather than guessed at.
         *
         * `912345678` is either a Vietnamese mobile typed without its trunk 0
         * or an Indian number with country code 91. The old code assumed the
         * second and produced `+912345678`, so the learner who dropped the
         * leading zero got a second account rather than an error — silently,
         * and only discoverable once their history went missing. Refusing
         * costs one corrected keystroke; guessing costs an account.
         */
        return false;
    }

    private static bool Ok(string normalised, out PhoneNumber phone)
    {
        phone = new PhoneNumber(normalised);
        return true;
    }

    public static PhoneNumber Create(string raw) =>
        TryCreate(raw, out var phone)
            ? phone
            : throw new ArgumentException($"Not a usable phone number: '{raw}'.", nameof(raw));
}
