using Vni.Ielts.Domain.Identity;
using Vni.Ielts.Infrastructure;

namespace Vni.Ielts.Infrastructure.Tests.Identity;

/// <summary>
/// The seeded permission grants for the CMS media library.
///
/// <para>
/// <b>Why this file exists.</b> The three <c>media.*</c> keys are the first
/// piece of the media library cutover, and the split between them is a
/// security decision copied straight from the confirmed CMS content model:
/// <c>media.retire</c> is separate from <c>media.upload</c> because the person
/// who uploads a file and the person who removes it from every picker are
/// different authorities. → docs/ux/cms-content-operations.md § 5. These tests
/// are what stops a future edit from quietly widening that grant.
/// </para>
/// </summary>
public sealed class SeededRolePermissionsTests
{
    [Fact]
    public void Every_media_key_appears_exactly_once_in_the_key_list()
    {
        // `PermissionKeys.All` drives the CMS roles matrix columns (the API
        // returns it verbatim as `permissions[]`). A duplicate would render
        // two identical columns; a missing key renders none — and the admin
        // UI already carries the labels for these three.
        var expected = new[]
        {
            PermissionKeys.MediaRead,
            PermissionKeys.MediaUpload,
            PermissionKeys.MediaRetire,
        };

        Assert.All(expected, key =>
        {
            var occurrences = PermissionKeys.All.Count(k => k == key);
            Assert.True(
                occurrences == 1,
                $"{key} must appear exactly once in PermissionKeys.All; found {occurrences}.");
        });
    }

    [Fact]
    public void The_author_role_can_upload_but_cannot_retire()
    {
        var editor = PermissionGrants(SystemRoles.ContentEditor);

        Assert.Contains(PermissionKeys.MediaRead, editor);
        Assert.Contains(PermissionKeys.MediaUpload, editor);
        Assert.DoesNotContain(PermissionKeys.MediaRetire, editor);
    }

    [Fact]
    public void Only_the_admin_holds_every_media_authority()
    {
        var admin = PermissionGrants(SystemRoles.Admin);

        Assert.Contains(PermissionKeys.MediaRead, admin);
        Assert.Contains(PermissionKeys.MediaUpload, admin);
        Assert.Contains(PermissionKeys.MediaRetire, admin);

        Assert.DoesNotContain(PermissionKeys.MediaRetire, PermissionGrants(SystemRoles.Support));
        Assert.DoesNotContain(PermissionKeys.MediaUpload, PermissionGrants(SystemRoles.Learner));
    }

    private static IReadOnlyCollection<string> PermissionGrants(string roleName) =>
        DependencyInjection.SeedRoles
            .Single(r => r.Name == roleName)
            .Permissions;
}
