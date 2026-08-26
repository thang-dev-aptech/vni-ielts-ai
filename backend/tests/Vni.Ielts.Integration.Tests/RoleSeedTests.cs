using MongoDB.Bson;
using MongoDB.Driver;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The role reseed of 24 Aug (`C-25`) — the three-operator-role model in
/// <c>apps/admin/src/lib/permissions.ts</c>'s <c>ROLE_PRESETS</c>, now pinned
/// server-side too. Reads straight from Mongo after
/// <c>InitialiseInfrastructureAsync</c> has run at app startup — no HTTP call
/// needed, since what is under test is the seed itself, not an endpoint.
/// </summary>
public sealed class RoleSeedTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private async Task<List<BsonDocument>> RolesAsync()
    {
        // Booting the factory once is what triggers the seed — the client
        // itself is never used, but creating it is what starts the host.
        _ = app.CreateClient();

        var roles = new MongoClient("mongodb://localhost:27018/?directConnection=true")
            .GetDatabase(app.Database)
            .GetCollection<BsonDocument>("roles");

        return await roles.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
    }

    [SkippableFact]
    public async Task Exactly_four_roles_are_seeded()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var roles = await RolesAsync();

        Assert.Equal(
            new[] { SystemRoles.Learner, SystemRoles.ExamAuthor, SystemRoles.AcademicLead, SystemRoles.Admin }
                .OrderBy(n => n, StringComparer.Ordinal),
            roles.Select(r => r["name"].AsString).OrderBy(n => n, StringComparer.Ordinal));
    }

    [SkippableFact]
    public async Task Only_admin_holds_publish_unpublish_and_learner_content_read()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var roles = await RolesAsync();

        foreach (var permission in new[]
                 {
                     PermissionKeys.ExamPublish, PermissionKeys.ExamUnpublish,
                     PermissionKeys.LearnerContentRead,
                 })
        {
            var holders = roles
                .Where(r => r["permissions"].AsBsonArray.Select(p => p.AsString).Contains(permission))
                .Select(r => r["name"].AsString)
                .ToList();

            Assert.Equal([SystemRoles.Admin], holders);
        }
    }

    [SkippableFact]
    public async Task Exam_author_never_holds_review_and_academic_lead_never_holds_publish()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var roles = await RolesAsync();
        var byName = roles.ToDictionary(r => r["name"].AsString, r => r["permissions"].AsBsonArray
            .Select(p => p.AsString).ToHashSet());

        Assert.DoesNotContain(PermissionKeys.ExamReview, byName[SystemRoles.ExamAuthor]);
        Assert.DoesNotContain(PermissionKeys.ExamPublish, byName[SystemRoles.AcademicLead]);

        // The asymmetry that makes the review gate real: an author can act on
        // their own content, a lead can act on anyone's, and only the lead
        // can review it at all.
        Assert.Contains(PermissionKeys.ExamReadOwn, byName[SystemRoles.ExamAuthor]);
        Assert.DoesNotContain(PermissionKeys.ExamReadAny, byName[SystemRoles.ExamAuthor]);
        Assert.Contains(PermissionKeys.ExamReadAny, byName[SystemRoles.AcademicLead]);
        Assert.Contains(PermissionKeys.ExamReview, byName[SystemRoles.AcademicLead]);
    }
}
