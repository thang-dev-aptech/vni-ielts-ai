using Vni.Ielts.Application.Exams;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Domain.Sessions;
using Vni.Ielts.Domain.Usage;

namespace Vni.Ielts.Application.Usage;

/// <summary>
/// The one place a ledger row is written from.
///
/// <para>
/// <b>Record only, never block — and never fail the caller either.</b> `P-14`
/// says the ledger observes; it does not gate. That has a second half which
/// is easy to miss: a ledger that can throw is a ledger that can refuse a
/// registration or an exam start on a database hiccup, which is blocking by
/// accident. So every method here swallows storage failures, the way
/// <c>LearnerPresence</c> does for the activity log. The row is lost; the
/// learner's action is not.
/// </para>
///
/// <para>
/// <b>`[QUYẾT ĐỊNH kỹ thuật, 07/09/2026]`</b> — the cost of that choice is a
/// row silently missing when storage is down, and the amounts here are
/// advisory today, so a missing row costs nothing anyone is owed. The day
/// blocking is switched on, a missing <i>use</i> row is a free turn and a
/// missing <i>earn</i> row is a complaint; both are visible in the ledger by
/// their absence and both are fixed with one more row. Turning this into a
/// hard failure then is a one-line change per method.
/// </para>
///
/// <para>
/// <b>Amounts come from <see cref="UsageOptions"/> and nowhere else.</b> A
/// zero is what an undecided amount looks like, and the row is written
/// anyway. → `G-11`
/// </para>
/// </summary>
public sealed class UsageRecorder(
    IUsageLedger ledger,
    UsageOptions options,
    IExamSessionRepository sessions,
    IClock clock)
{
    /// <summary>
    /// A new account, registered with a phone number: the welcome allowance,
    /// plus the referral credit if somebody's link brought them here.
    ///
    /// <para>
    /// <b>Takes the <see cref="User"/> rather than an id because the referral
    /// is now settled here.</b> It used to be settled when the invitee verified
    /// their address; there is no such moment any more, so the owner moved the
    /// credit to registration (08/09/2026). What stops the obvious farm is no
    /// longer a verified mailbox but a unique phone number — a real cost per
    /// account rather than a free one. → `P-16`, threat T13, ADR-0018
    /// </para>
    /// </summary>
    public async Task AccountCreatedAsync(User user, CancellationToken ct)
    {
        await SafeAppend(
            UsageEntry.Grant(
                UsageEntry.GrantId(user.Id), user.Id, clock.UtcNow, UsageActions.AccountCreated,
                options.InitialGrantTurns),
            ct);

        await ReferralQualifiedAsync(user, ct);
    }

    /// <summary>
    /// The same, for an account a social provider created — but the welcome
    /// allowance is keyed on the provider subject.
    /// See <see cref="UsageEntry.ProviderGrantId"/> for why.
    /// </summary>
    public async Task AccountCreatedFromProviderAsync(
        User user, IdentityProvider provider, string subject, CancellationToken ct)
    {
        await SafeAppend(
            UsageEntry.Grant(
                UsageEntry.ProviderGrantId(provider.ToString(), subject), user.Id, clock.UtcNow,
                UsageActions.AccountCreated, options.InitialGrantTurns),
            ct);

        await ReferralQualifiedAsync(user, ct);
    }

    /// <summary>
    /// The first activity of a calendar day. Id <c>daily:{userId}:{day}</c>,
    /// so a second touch on the same day collides and nothing is paid twice.
    /// Returns true only when this call wrote the row. → `P-16`
    /// </summary>
    public Task<bool> DailyActivityAsync(UserId userId, DateOnly day, CancellationToken ct) =>
        SafeAppend(
            UsageEntry.Earn(
                UsageEntry.DailyId(userId, day), userId, clock.UtcNow, UsageActions.DailyLogin,
                options.DailyLoginTurns),
            ct);

    /// <summary>
    /// An invitee has completed registration. Pays the <i>referrer</i>, once
    /// per invitee — the id is <c>referral:{inviteeId}</c>, so a retried
    /// request cannot pay twice, and rows written under the old
    /// verification gate keep that same id and still block a second payment.
    /// Nothing happens for an account nobody referred. → `P-16`, threat T13
    ///
    /// <para>
    /// <b>Deliberately not named for verification any more.</b> Leaving a
    /// method called <c>EmailVerifiedAsync</c> in place would be the quiet way
    /// "paid once proven" turns into "paid on signup" the day somebody sets
    /// <c>Usage:ReferralTurns</c> above zero believing the old gate still
    /// exists. It does not; the gate is the unique phone number.
    /// </para>
    /// </summary>
    public Task<bool> ReferralQualifiedAsync(User invitee, CancellationToken ct)
    {
        if (invitee.ReferredByUserId is not { } referrer) return Task.FromResult(false);

        return SafeAppend(
            UsageEntry.Earn(
                UsageEntry.ReferralId(invitee.Id), referrer, clock.UtcNow, UsageActions.ReferralQualified,
                options.ReferralTurns),
            ct);
    }

    public Task SessionOpenedAsync(UserId userId, ExamSessionId sessionId, CancellationToken ct) =>
        SafeAppend(
            UsageEntry.Use(
                userId, clock.UtcNow, UsageActions.SessionOpened,
                options.CostOf(UsageActions.SessionOpened), sessionId),
            ct);

    /// <summary>
    /// A Writing evaluator was called for this sitting. The runner knows the
    /// sitting and not the learner, so the owner is read from the sitting —
    /// one query against a paid provider call.
    /// </summary>
    public async Task WritingMarkedAsync(
        ExamSessionId sessionId, EvaluationUsage? usage, CancellationToken ct)
    {
        try
        {
            var session = await sessions.FindAsync(sessionId, ct);
            if (session is null) return;

            await ledger.AppendAsync(
                UsageEntry.Use(
                    session.UserId, clock.UtcNow, UsageActions.WritingMarked,
                    options.CostOf(UsageActions.WritingMarked), sessionId,
                    usage?.Provider, usage?.Model, usage?.TokensIn, usage?.TokensOut),
                ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Swallowed by design; see the class remarks.
        }
    }

    public Task ExplanationGeneratedAsync(
        UserId userId, ExamSessionId sessionId, string? provider, string? model, CancellationToken ct) =>
        SafeAppend(
            UsageEntry.Use(
                userId, clock.UtcNow, UsageActions.ExplanationGenerated,
                options.CostOf(UsageActions.ExplanationGenerated), sessionId, provider, model),
            ct);

    public Task CoachingAdvisedAsync(
        UserId userId, string? provider, string? model, CancellationToken ct) =>
        SafeAppend(
            UsageEntry.Use(
                userId, clock.UtcNow, UsageActions.CoachingAdvised,
                options.CostOf(UsageActions.CoachingAdvised), null, provider, model),
            ct);

    private async Task<bool> SafeAppend(UsageEntry entry, CancellationToken ct)
    {
        try
        {
            return await ledger.AppendAsync(entry, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Swallowed by design; see the class remarks.
            return false;
        }
    }
}
