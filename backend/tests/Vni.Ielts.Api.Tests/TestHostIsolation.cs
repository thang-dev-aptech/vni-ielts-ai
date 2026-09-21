using System.Runtime.CompilerServices;
using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Api.Tests;

/// <summary>
/// Keeps every test host in this assembly off the developer's secrets file.
/// Same reason as <c>Vni.Ielts.Integration.Tests.TestHostIsolation</c>: Development
/// loads <c>secrets.develop.json</c>, and a config/secret-leak suite must not
/// inherit real keys from the laptop it runs on.
/// </summary>
internal static class TestHostIsolation
{
    [ModuleInitializer]
    internal static void KeepTheDeveloperSecretsFileOut() =>
        Environment.SetEnvironmentVariable(SecretsFileConfigurationExtensions.SkipVariable, "off");
}
