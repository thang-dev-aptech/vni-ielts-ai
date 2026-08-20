namespace Vni.Ielts.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "vni-ielts";
    public string Audience { get; set; } = "vni-ielts-clients";

    /// <summary>
    /// From environment configuration only. Never committed — the .gitignore
    /// and a PreToolUse hook both block <c>.env*</c>, and CI scans for
    /// credential-shaped strings. → CLAUDE.md rule 6
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Short, because an access token cannot be revoked once issued — the
    /// window between a suspension taking effect and the token expiring is
    /// exactly this long.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;
}
