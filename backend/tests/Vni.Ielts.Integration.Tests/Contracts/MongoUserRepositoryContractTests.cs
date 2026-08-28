using Microsoft.Extensions.DependencyInjection;
using Vni.Ielts.Application.Identity;

namespace Vni.Ielts.Integration.Tests.Contracts;

/// <summary>
/// The MongoDB provider, measured against <see cref="UserRepositoryContract"/>.
///
/// <b>This class holds no assertions, and that is the point.</b> Everything it
/// runs is inherited. When the PostgreSQL adapter is written, its contract
/// suite is a file this size — resolve the implementation, say when it is
/// reachable — and the two providers are then held to one specification
/// rather than to two suites that drifted.
///
/// Resolved from the DI container rather than constructed directly, because
/// <c>MongoUserRepository</c> is <c>internal</c> to Infrastructure by design
/// (ADR-0004) and a test that reached past that would be asserting against a
/// type the application itself cannot name.
/// </summary>
public sealed class MongoUserRepositoryContractTests(SsoAppFactory app)
    : UserRepositoryContract, IClassFixture<SsoAppFactory>
{
    /*
     * <b>One scope for the fixture, not one per test.</b> The repository is
     * registered scoped, and every assertion in the contract is about what
     * the STORE holds — not about what one scope cached — so a single scope
     * held for the class keeps the tests honest while avoiding a per-test
     * container resolution that proves nothing extra.
     */
    private IServiceScope? _scope;

    protected override IUserRepository Repository =>
        (_scope ??= app.Services.CreateScope())
            .ServiceProvider.GetRequiredService<IUserRepository>();

    protected override bool ProviderAvailable => SsoAppFactory.MongoAvailable;

    protected override string ProviderSkipReason => SsoAppFactory.SkipReason;
}
