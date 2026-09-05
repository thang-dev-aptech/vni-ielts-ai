using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// The committed contract, and the gate that keeps it true.
///
/// <b>One test does both jobs, deliberately.</b> A generator somebody has to
/// remember to run produces a spec that is wrong within a week — and a wrong
/// spec is worse than none, because the clients generated from it look correct
/// while describing an API that has moved. Making the check <i>be</i> the
/// generator means the artefact cannot rot: the only way to make this test pass
/// is to commit what the application actually serves.
///
/// <b>Why a committed contract is worth this much trouble here.</b> The most
/// expensive bug this product has had — `A17` — was two sides of one contract
/// disagreeing while both had passing tests. The client spelled a multi-select
/// pick <c>"A|D"</c>; the marker accepted <c>"A,D"</c>. Nobody owned the
/// sentence between them, and it cost six Reading marks and seven Listening
/// marks on every sitting. A generated client makes that class of bug
/// impossible rather than unlikely. → `I7.1`, `I7.2`, `I7.3`
/// </summary>
public sealed class OpenApiContractTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    /// <summary>
    /// Where the committed document lives.
    ///
    /// Walked up to from the test assembly rather than assumed relative to the
    /// working directory: `dotnet test` and an IDE runner disagree about what
    /// that is, and a path that resolves in one and not the other is a test
    /// that passes on somebody's machine.
    /// </summary>
    private static string ContractPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;

        Assert.True(directory is not null, "Repository root not found from the test assembly.");

        return Path.Combine(directory!.FullName, "contracts", "openapi", "v1.json");
    }

    /// <summary>
    /// Formatted the same way every time, so a diff is about the contract.
    ///
    /// Without a canonical rendering, a serializer setting or a property order
    /// changing would produce a diff of the whole file and hide the one line
    /// that mattered.
    /// </summary>
    private static string Canonical(string json) =>
        JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(json),
            new JsonSerializerOptions { WriteIndented = true });

    [SkippableFact]
    public async Task The_committed_contract_matches_what_this_api_serves()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = app.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        var served = Canonical(await response.Content.ReadAsStringAsync());
        var path = ContractPath();

        var committed = File.Exists(path) ? Canonical(await File.ReadAllTextAsync(path)) : null;

        if (committed == served) return;

        /*
         * <b>The fix is applied before the failure is reported.</b>
         *
         * A gate that only says "these differ" leaves the reader to work out
         * how to regenerate, and the instruction rots the first time the
         * command changes. Writing the new document into the working tree means
         * the answer to "what do I do" is always `git diff` — and the diff is
         * the thing that actually needs reviewing, because it is the contract
         * two clients are generated from.
         */
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, served + "\n");

        Assert.Fail(
            committed is null
                ? $"There was no committed contract. One has been written to {path} — review "
                  + "it and commit it. `packages/api-client` is generated from this file, so it "
                  + "is what both the learner app and the CMS are typed against."
                : "The API no longer matches the committed contract, and the regenerated "
                  + $"document has been written to {path}.\n\n"
                  + "Read the diff before committing it. A change here changes what two "
                  + "generated clients believe, and the most expensive bug this product has had "
                  + "was two sides of one contract disagreeing while both had passing tests.");
    }

    [SkippableFact]
    public async Task Every_authenticated_route_says_it_needs_a_token()
    {
        /*
         * <b>A generated client with no way to send a token is a generated
         * client nobody can use.</b> The security scheme is declared on the
         * document and attached to each guarded operation from its own
         * metadata — nothing is guessed, so this asserts the wiring works
         * rather than that somebody remembered.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = app.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        Assert.True(
            document.RootElement.GetProperty("components")
                .GetProperty("securitySchemes").TryGetProperty("bearer", out _),
            "The document declares no bearer scheme, so nothing generated from it could "
            + "authenticate at all.");

        // A route everybody agrees needs a token.
        var results = document.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/sessions/{sessionId}/results")
            .GetProperty("get");

        Assert.True(
            results.TryGetProperty("security", out var security) && security.GetArrayLength() > 0,
            "A learner's own results are described as needing no token.");

        Assert.True(
            results.GetProperty("responses").TryGetProperty("401", out _),
            "An authenticated route does not describe the 401 every client will meet.");
    }

    [SkippableFact]
    public async Task Rate_limited_routes_say_so()
    {
        // A client that does not know a 429 is possible is a client that
        // retries into one — and every 429 here carries a Retry-After the
        // caller is supposed to honour.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var client = app.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var submit = document.RootElement.GetProperty("paths")
            .GetProperty("/api/v1/sessions/{sessionId}/submit")
            .GetProperty("post");

        Assert.True(
            submit.GetProperty("responses").TryGetProperty("429", out _),
            "Submit is rate limited per sitting and the contract does not say so.");
    }
}
