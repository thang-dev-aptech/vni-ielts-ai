using System.Reflection;
using NetArchTest.Rules;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;

namespace Vni.Ielts.Architecture.Tests;

/// <summary>
/// The one strict boundary in this system.
///
/// ADR-0004 resolves a genuine tension: requirement D-3 asks that the
/// MongoDB to PostgreSQL migration stay manageable, while D-5 forbids
/// prematurely building an elaborate Clean Architecture. The resolution is
/// one strict rule instead of five:
///
///   Repository interfaces live in Application.
///   Persistence models and mapping live in Infrastructure.
///   Domain entities carry no persistence attributes.
///
/// That rule is what reduces the blast radius of switching databases to a
/// single project. It is also the rule most likely to decay quietly, because
/// violating it never breaks anything on the day it happens — it only makes
/// the migration progressively more expensive.
///
/// So it is enforced here rather than remembered. A rule nobody checks is a
/// rule that decays.
/// </summary>
public sealed class PersistenceBoundaryTests
{
    private static readonly Assembly Domain = DomainAssembly.Instance;
    private static readonly Assembly Application = ApplicationAssembly.Instance;

    /// <summary>
    /// Namespaces that must never appear in Domain or Application. Storage
    /// drivers and vendor SDKs both — CLAUDE.md rule 5 forbids an AI provider
    /// type in the domain layer for the same reason rule 7 forbids a
    /// persistence attribute: the moment one appears, the layer is no longer
    /// swappable.
    /// </summary>
    private static readonly string[] ForbiddenDependencies =
    [
        "MongoDB",
        "Npgsql",
        "Microsoft.EntityFrameworkCore",
        "System.Data.SqlClient",
        "Microsoft.Data.SqlClient",
        "OpenAI",
        "Google.Cloud",
        "Google.Apis",
        "Azure.AI",
        "Amazon",
        // Identity-provider token handling. The SSO adapters validate ID
        // tokens against a provider's JWKS, and that machinery belongs in
        // Infrastructure for the same reason a Mongo driver type does: the
        // sign-in rules in Application are about accounts, not about JWT
        // signatures. → ADR-0014
        "Microsoft.IdentityModel",
        "System.IdentityModel",
    ];

    [Fact]
    public void Domain_has_no_dependency_on_a_storage_driver_or_vendor_sdk()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenDependencies)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            Explain("Vni.Ielts.Domain", result.FailingTypeNames));
    }

    [Fact]
    public void Application_has_no_dependency_on_a_storage_driver_or_vendor_sdk()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenDependencies)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            Explain("Vni.Ielts.Application", result.FailingTypeNames));
    }

    /// <summary>
    /// Dependencies point inward only. Domain references nothing of ours.
    /// </summary>
    [Fact]
    public void Domain_does_not_reference_any_other_project_in_this_solution()
    {
        var referenced = Domain
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Vni.Ielts", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            referenced.Length == 0,
            $"Vni.Ielts.Domain must reference nothing of ours, but references: "
                + $"{string.Join(", ", referenced)}. Dependencies point inward only.");
    }

    /// <summary>
    /// Application may know Domain, and nothing further out.
    /// </summary>
    [Fact]
    public void Application_does_not_reference_Infrastructure_Api_or_Worker()
    {
        string[] outward = ["Vni.Ielts.Infrastructure", "Vni.Ielts.Api", "Vni.Ielts.Worker"];

        var violations = Application
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => outward.Contains(n, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Vni.Ielts.Application must not reference {string.Join(" or ", outward)}, "
                + $"but references: {string.Join(", ", violations)}.");
    }

    private static string Explain(string layer, IEnumerable<string>? failing)
    {
        var names = failing?.ToArray() ?? [];
        return $"""
            {layer} depends on a storage driver or a vendor SDK.

            Offending types:
              {string.Join("\n  ", names)}

            This is the one boundary that keeps the MongoDB to PostgreSQL
            migration a rewrite of Infrastructure alone. Move the persistence
            model and its mapping into Vni.Ielts.Infrastructure and keep the
            port interface in Application.

            -> docs/decisions/0004-persistence-abstraction-boundary.md
            -> CLAUDE.md rules 5 and 7
            """;
    }
}
