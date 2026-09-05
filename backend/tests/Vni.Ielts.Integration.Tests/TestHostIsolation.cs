using System.Runtime.CompilerServices;
using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Keeps every test host in this assembly off the developer's secrets file.
///
/// <b>Why a module initializer and not a line in each factory.</b> Every
/// factory here boots the API as Development, and Development is also the
/// environment that loads <c>backend/src/Vni.Ielts.Api/secrets.develop.json</c>
/// — the file on the developer's laptop with the real Google client, the real
/// Writing rubric and provider key, and (since 2026-09-04) a Cloudflare R2
/// bucket for Speaking recordings. Each of those leaked into a test host on a
/// different day and failed a different suite for a reason that had nothing to
/// do with the test: eleven <c>SessionsTests</c> with a null redirect, the
/// journey test hanging on a real provider call, and
/// <c>Re_uploading_one_answer_replaces_the_recording</c> finding zero
/// recordings in GridFS because the upload had gone to R2.
///
/// Pinning keys one factory at a time (which <c>ExamAppFactory</c> and
/// <c>ObjectStorageAppFactory</c> still do, harmlessly) chases the file's
/// contents. This says the one thing that is actually true: a test host wants
/// <i>no</i> developer file. Factories that need a value set it themselves
/// with <c>UseSetting</c>, which is how a test states its premise anyway.
///
/// Runs before any test in the assembly; <c>WebApplicationFactory</c> boots the
/// API in this process, so the process-wide variable is what the API reads.
/// </summary>
internal static class TestHostIsolation
{
    [ModuleInitializer]
    internal static void KeepTheDeveloperSecretsFileOut() =>
        Environment.SetEnvironmentVariable(SecretsFileConfigurationExtensions.SkipVariable, "off");
}
