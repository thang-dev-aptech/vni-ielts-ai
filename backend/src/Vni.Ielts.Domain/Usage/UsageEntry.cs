using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Sessions;

namespace Vni.Ielts.Domain.Usage;

/// <summary>
/// Which way a ledger row moves the balance, and why.
/// </summary>
public enum UsageKind
{
    /// <summary>Turns given for nothing — the welcome allowance. → `P-15`</summary>
    Grant,

    /// <summary>Turns the learner did something to receive. → `P-16`</summary>
    Earn,

    /// <summary>Turns spent on an action that costs something to run. → `P-14`</summary>
    Use,
}

/// <summary>
/// The action names a ledger row may carry.
///
/// Strings rather than an enum on purpose: a row is append-only and lives
/// forever, and an enum stored as its ordinal is exactly the representation
/// the architecture tests forbid. New actions are added here; old rows keep
/// reading.
/// </summary>
public static class UsageActions
{
    public const string AccountCreated = "account.created";
    public const string DailyLogin = "login.daily";
    /// <summary>
    /// Historical. Paid when an invitee proved their address, back when there
    /// was an address to prove. Kept because the ledger is append-only and
    /// rows carrying it must still read. → ADR-0018
    /// </summary>
    public const string ReferralVerified = "referral.verified";

    /// <summary>
    /// What replaced it: the invitee finished registering. The gate moved
    /// because verification no longer exists; the control that stops a
    /// self-referral farm is now the unique phone number. → `P-16`, threat T13
    /// </summary>
    public const string ReferralQualified = "referral.qualified";
    public const string SessionOpened = "session.opened";
    public const string WritingMarked = "writing.marked";
    public const string ExplanationGenerated = "explanation.generated";
    public const string CoachingAdvised = "coaching.advised";
}

/// <summary>
/// One line of the usage ledger.
///
/// <para>
/// <b>A ledger, not a counter.</b> `P-14` records and never blocks, and the
/// blueprint (§08) is explicit about why the shape matters now rather than
/// later: turning blocking on is one added check over the sum of these rows,
/// while growing a counter into a ledger is a schema rewrite with a history
/// nobody has. So the balance is never stored — it is the sum of
/// <see cref="Turns"/> over a learner's rows, every time.
/// </para>
///
/// <para>
/// <b>Append-only, and the port says so.</b> There is no update and no
/// delete anywhere above Infrastructure. A row that is wrong is corrected by
/// another row.
/// </para>
///
/// <para>
/// <b>The id is the idempotency key.</b> Earns and grants that must happen
/// once carry a deterministic id — <c>daily:{userId}:{day}</c>,
/// <c>referral:{inviteeId}</c>, <c>grant:{userId}</c> — so a retried request
/// or a second tab collides on the unique id instead of paying twice. Uses
/// carry a random id because each one is a real thing that happened.
/// </para>
/// </summary>
public sealed class UsageEntry
{
    private UsageEntry(
        string id,
        UserId userId,
        DateTimeOffset at,
        string action,
        UsageKind kind,
        decimal turns,
        ExamSessionId? sessionId,
        string? provider,
        string? model,
        long? tokensIn,
        long? tokensOut,
        decimal? costEstimate)
    {
        Id = id;
        UserId = userId;
        At = at;
        Action = action;
        Kind = kind;
        Turns = turns;
        SessionId = sessionId;
        Provider = provider;
        Model = model;
        TokensIn = tokensIn;
        TokensOut = tokensOut;
        CostEstimate = costEstimate;
    }

    public string Id { get; }
    public UserId UserId { get; }
    public DateTimeOffset At { get; }
    public string Action { get; }
    public UsageKind Kind { get; }

    /// <summary>
    /// The signed delta this row applies to the balance. Positive for a
    /// grant or an earn, zero or negative for a use. Zero is legal and
    /// expected: an amount nobody has decided yet is recorded as a row with
    /// no turns rather than not recorded at all. → `G-11`
    /// </summary>
    public decimal Turns { get; }

    public ExamSessionId? SessionId { get; }
    public string? Provider { get; }
    public string? Model { get; }
    public long? TokensIn { get; }
    public long? TokensOut { get; }

    /// <summary>
    /// What the action is believed to have cost, in whatever unit the caller
    /// used. Null today: no token price has been decided (`B-5a`/`B-5b`),
    /// and an estimate against a price that does not exist would be a number
    /// somebody later believes.
    /// </summary>
    public decimal? CostEstimate { get; }

    public static string GrantId(UserId userId) => $"grant:{userId.Value}";

    /// <summary>
    /// The welcome allowance for an account a social provider created, keyed on
    /// the provider subject instead of the account.
    ///
    /// <para>
    /// <b>Because the account is no longer the thing that happens once.</b>
    /// Changing an address frees the old one, and signing in at it again mints
    /// a fresh account — legitimately. Keyed on the account id, every turn of
    /// that loop is a new id and another grant, indefinitely, from a single
    /// Google login. The subject is the part that does not change.
    /// → threat T4, ADR-0018
    /// </para>
    /// </summary>
    public static string ProviderGrantId(string provider, string subject) =>
        $"grant:sso:{provider.ToLowerInvariant()}:{subject}";

    public static string DailyId(UserId userId, DateOnly day) =>
        $"daily:{userId.Value}:{day:yyyy-MM-dd}";

    public static string ReferralId(UserId inviteeId) => $"referral:{inviteeId.Value}";

    public static UsageEntry Grant(
        string id, UserId userId, DateTimeOffset at, string action, decimal turns)
    {
        if (turns < 0)
            throw new ArgumentOutOfRangeException(nameof(turns), "A grant cannot take turns away.");

        return Create(id, userId, at, action, UsageKind.Grant, turns, null, null, null, null, null, null);
    }

    public static UsageEntry Earn(
        string id, UserId userId, DateTimeOffset at, string action, decimal turns)
    {
        if (turns < 0)
            throw new ArgumentOutOfRangeException(nameof(turns), "An earn cannot take turns away.");

        return Create(id, userId, at, action, UsageKind.Earn, turns, null, null, null, null, null, null);
    }

    /// <param name="cost">How many turns the action costs. Recorded as a negative delta.</param>
    public static UsageEntry Use(
        UserId userId,
        DateTimeOffset at,
        string action,
        decimal cost,
        ExamSessionId? sessionId = null,
        string? provider = null,
        string? model = null,
        long? tokensIn = null,
        long? tokensOut = null,
        decimal? costEstimate = null)
    {
        if (cost < 0)
            throw new ArgumentOutOfRangeException(nameof(cost), "A use cannot add turns.");
        if (tokensIn is < 0 || tokensOut is < 0)
            throw new ArgumentOutOfRangeException(nameof(tokensIn), "Token counts cannot be negative.");

        return Create(
            Guid.NewGuid().ToString("n"), userId, at, action, UsageKind.Use, -cost,
            sessionId, provider, model, tokensIn, tokensOut, costEstimate);
    }

    /// <summary>Rehydration from storage. Infrastructure only.</summary>
    public static UsageEntry Rehydrate(
        string id,
        UserId userId,
        DateTimeOffset at,
        string action,
        UsageKind kind,
        decimal turns,
        ExamSessionId? sessionId,
        string? provider,
        string? model,
        long? tokensIn,
        long? tokensOut,
        decimal? costEstimate) =>
        new(id, userId, at, action, kind, turns, sessionId, provider, model, tokensIn, tokensOut, costEstimate);

    private static UsageEntry Create(
        string id, UserId userId, DateTimeOffset at, string action, UsageKind kind, decimal turns,
        ExamSessionId? sessionId, string? provider, string? model, long? tokensIn, long? tokensOut,
        decimal? costEstimate)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A ledger row needs an id.", nameof(id));
        if (string.IsNullOrWhiteSpace(userId.Value))
            throw new ArgumentException("A ledger row belongs to a learner.", nameof(userId));
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("A ledger row names the action it records.", nameof(action));

        return new UsageEntry(
            id, userId, at, action.Trim(), kind, turns, sessionId, provider, model, tokensIn, tokensOut,
            costEstimate);
    }
}
