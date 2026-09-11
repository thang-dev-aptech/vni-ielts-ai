using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Domain.Tests.Identity;

/// <summary>
/// Integration I1 — three CMS roles and ownership-scoped keys.
/// </summary>
public sealed class CmsRoleSeedContractTests
{
    [Fact]
    public void System_roles_are_learner_plus_three_cms_roles()
    {
        Assert.Equal("learner", SystemRoles.Learner);
        Assert.Equal("exam-author", SystemRoles.ExamAuthor);
        Assert.Equal("academic-lead", SystemRoles.AcademicLead);
        Assert.Equal("admin", SystemRoles.Admin);
    }

    [Fact]
    public void Permission_All_includes_ownership_media_and_content_rights()
    {
        Assert.Contains(PermissionKeys.ExamReadOwn, PermissionKeys.All);
        Assert.Contains(PermissionKeys.ExamReadAny, PermissionKeys.All);
        Assert.Contains(PermissionKeys.ContentRightsManage, PermissionKeys.All);
        Assert.Contains(PermissionKeys.MediaRead, PermissionKeys.All);
        Assert.Contains(PermissionKeys.UserResetPassword, PermissionKeys.All);
        Assert.DoesNotContain("exam.read", PermissionKeys.All);
    }
}
