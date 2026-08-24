namespace Vni.Ielts.Domain.Identity;

/// <summary>
/// The permission keys shipped in the initial seed.
///
/// <b>This is not an enum, and authorisation checks are not restricted to it.</b>
/// Permissions are seeded data compared as strings, so a new one can be added
/// without a deployment — requirement C-13 states explicitly that the example
/// keys are not final. These constants exist only so the seeder and the tests
/// stop typoing them.
///
/// Three separations below are security decisions rather than taxonomy taste:
///
/// <list type="bullet">
/// <item><b><c>exam.publish</c> is separate from <c>exam.update</c></b> — the
/// person who imports content and the person who ships it to learners need not
/// be the same account. Direct mitigation of threat T20; merging them means one
/// compromised editor account can push content to every candidate.</item>
///
/// <item><b><c>learner-content.read</c> is separate from <c>evaluation.read</c></b>
/// — reading a band and its metadata is one thing; reading a learner's essay or
/// listening to their recording is personal-data processing under PDPL. Merged,
/// everyone debugging a score can read learner work.</item>
///
/// <item><b><c>evaluation.rerun</c> stands alone</b> — it is a permission that
/// spends real money on a provider call.</item>
/// </list>
///
/// → docs/ux/cms-spec.md § 1
/// </summary>
public static class PermissionKeys
{
    public const string ExamRead = "exam.read";
    public const string ExamCreate = "exam.create";
    public const string ExamUpdate = "exam.update";
    public const string ExamDelete = "exam.delete";
    public const string ExamPublish = "exam.publish";
    public const string ExamUnpublish = "exam.unpublish";

    public const string PackageUpload = "package.upload";
    public const string PackageRead = "package.read";
    public const string PackageDelete = "package.delete";

    public const string EvaluationRead = "evaluation.read";
    public const string EvaluationRerun = "evaluation.rerun";
    public const string EvaluationOverride = "evaluation.override";

    public const string LearnerContentRead = "learner-content.read";

    public const string UserRead = "user.read";
    public const string UserUpdate = "user.update";
    public const string UserSuspend = "user.suspend";
    public const string UserDelete = "user.delete";
    public const string UserExport = "user.export";

    public const string RoleRead = "role.read";
    public const string RoleAssign = "role.assign";
    public const string RoleManage = "role.manage";

    public const string ConfigRead = "config.read";
    public const string ConfigUpdate = "config.update";

    public const string AuditRead = "audit.read";

    /// <summary>
    /// Every key, in the order the CMS's permission matrix renders its columns.
    ///
    /// <b>Derived here, not restated in the client.</b> The matrix needs a
    /// column for a permission no role holds yet, and a second list in
    /// TypeScript is a second thing to forget when a key is added — the column
    /// would simply not appear, and nobody would notice until someone tried to
    /// grant it.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        ExamRead, ExamCreate, ExamUpdate, ExamDelete, ExamPublish, ExamUnpublish,
        PackageUpload, PackageRead, PackageDelete,
        EvaluationRead, EvaluationRerun, EvaluationOverride,
        LearnerContentRead,
        UserRead, UserUpdate, UserSuspend, UserDelete, UserExport,
        RoleRead, RoleAssign, RoleManage,
        ConfigRead, ConfigUpdate,
        AuditRead,
    ];
}

/// <summary>
/// The three seeded roles. <c>M-11</c> settled that the first release has no
/// teacher role, so there is deliberately no <c>Class</c>, no <c>Assignment</c>,
/// and no teacher-student relationship anywhere in this model.
/// </summary>
public static class SystemRoles
{
    public const string Learner = "learner";
    public const string Admin = "admin";
    public const string ContentEditor = "content-editor";
    public const string Support = "support";
}
