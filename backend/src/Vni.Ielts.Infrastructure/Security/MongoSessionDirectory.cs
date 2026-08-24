using MongoDB.Driver;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Infrastructure.Persistence;

namespace Vni.Ielts.Infrastructure.Security;

/// <summary>
/// The account's live sign-ins, assembled from refresh-token families.
///
/// <para>
/// One family is one sign-in. Rotation inserts a new document into the same
/// family on every refresh, so a family is a run of documents: the earliest
/// says when the person signed in, the latest says when they were last active,
/// and the User-Agent is whatever the device announced at the time.
/// </para>
///
/// <para>
/// <b>Grouped in application code, not with <c>$group</c>.</b> An aggregation
/// pipeline here would be a relational query wearing a disguise and would not
/// survive the PostgreSQL move cleanly — the same rule that keeps <c>$lookup</c>
/// out of the permission resolver. A person has a handful of devices; fetching
/// their live tokens and folding them is cheaper than the coupling.
/// → ADR-0004
/// </para>
/// </summary>
internal sealed class MongoSessionDirectory(MongoContext ctx, IClock clock) : ISessionDirectory
{
    public async Task<IReadOnlyList<LearnerSession>> ListAsync(
        UserId userId, string? currentFamilyId, CancellationToken ct)
    {
        var now = clock.UtcNow.UtcDateTime;

        // Live only. A revoked family is a session someone already ended, and
        // an expired one is a device that can no longer reach anything —
        // neither is something to offer a "sign out" button for.
        var live = await ctx.RefreshTokens
            .Find(t => t.UserId == userId.Value && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

        return
        [
            .. live
                .GroupBy(t => t.FamilyId)
                .Select(family =>
                {
                    var ordered = family.OrderBy(t => t.CreatedAt).ToList();
                    var first = ordered[0];
                    var last = ordered[^1];

                    return new LearnerSession(
                        family.Key,
                        // The newest one wins: a browser that updated should
                        // show its current version, not the one from a month ago.
                        last.UserAgent ?? first.UserAgent,
                        Utc(first.CreatedAt),
                        Utc(last.CreatedAt),
                        Utc(family.Max(t => t.ExpiresAt)),
                        family.Key == currentFamilyId);
                })
                // Most recently active first — the device in your hand is the
                // one you are least likely to be looking for, and the one you
                // are most likely to want to recognise at a glance.
                .OrderByDescending(session => session.LastUsedAt),
        ];
    }

    /// <summary>
    /// Mongo hands back <c>Unspecified</c>, which silently becomes local time
    /// when it meets a <c>DateTimeOffset</c>. Every timestamp in this file
    /// would then be wrong by the server's offset.
    /// </summary>
    private static DateTimeOffset Utc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
