using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// Two things a deployment depends on and neither existed.
///
/// <b>Health was a single endpoint returning `ok` unconditionally.</b> It
/// answered the same way whether the database was reachable or not — which is
/// worse than having none: a load balancer routes traffic to it, a deployment
/// goes green on it, and an operator watching it sees a healthy system serving
/// 500s.
///
/// <b>Configuration was checked for exactly one setting.</b> The signing key had
/// a guard because somebody had been bitten by it; a wrong issuer, an empty CORS
/// list and a missing SSO callback all surfaced at runtime as user problems —
/// and the CORS one leaves no server-side trace at all, because the browser
/// refuses the response after this API has already answered 200.
/// </summary>
public sealed class StartupAndHealthTests(SsoAppFactory app) : IClassFixture<SsoAppFactory>
{
    private HttpClient NewClient() =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [SkippableFact]
    public async Task Liveness_answers_without_touching_anything_external()
    {
        /*
         * <b>Nothing external, and that is the whole design.</b> This is what a
         * restart policy reads. A liveness probe that consulted the database
         * would restart every API process during a database outage — turning
         * one failure into two, and losing the in-flight requests that would
         * have survived it.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var response = await NewClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("live", body.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Readiness_reports_the_dependencies_it_actually_checked()
    {
        // A readiness endpoint that says "ready" without naming what it checked
        // is a readiness endpoint nobody can debug when it lies.
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var response = await NewClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("ready", body.GetProperty("status").GetString());

        var checks = body.GetProperty("checks").EnumerateArray().ToArray();
        var mongo = Assert.Single(checks, c => c.GetProperty("name").GetString() == "mongo");

        Assert.Equal("ok", mongo.GetProperty("status").GetString());
    }

    [SkippableFact]
    public async Task Readiness_does_not_leak_what_it_could_not_reach()
    {
        /*
         * The endpoint is anonymous — reachable by anything that can reach the
         * process — so a driver exception message would publish a connection
         * string to it. The type is enough to tell a timeout from a refusal,
         * and the log has the rest.
         */
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        var body = await (await NewClient().GetAsync("/health/ready"))
            .Content.ReadAsStringAsync();

        Assert.DoesNotContain("mongodb://", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost:27018", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A configuration that cannot work refuses to start.</b>
    ///
    /// An empty CORS list produces the failure with no server-side trace at
    /// all: the browser refuses the response after this API has answered 200,
    /// so the log says the request succeeded and the user says the app is
    /// broken. It presents as a client bug and is not.
    /// </summary>
    [Fact]
    public void A_production_configuration_with_no_cors_origins_refuses_to_boot()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            using var broken = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(host =>
                {
                    host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Production);
                    host.UseSetting("Jwt:SigningKey", new string('k', 48));
                    host.UseSetting("Mongo:ConnectionString", "mongodb://localhost:27018/?replicaSet=rs0");
                    host.UseSetting("Cors:Origins:0", string.Empty);
                });

            broken.CreateClient();
        });

        Assert.Contains("Cors:Origins", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And it names every problem, not the first one.
    ///
    /// A validator that throws on the first fault turns bringing up a fresh
    /// environment into as many deploy cycles as there are mistakes, and the
    /// person doing it has no way to know how many are left.
    /// </summary>
    [Fact]
    public void A_broken_configuration_names_every_problem_at_once()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            using var broken = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(host =>
                {
                    host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Production);
                    host.UseSetting("Jwt:SigningKey", new string('k', 48));
                    host.UseSetting("Jwt:Issuer", string.Empty);
                    host.UseSetting("Jwt:Audience", string.Empty);
                    host.UseSetting("Mongo:Database", string.Empty);
                });

            broken.CreateClient();
        });

        Assert.Contains("Jwt:Issuer", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Jwt:Audience", failure.Message, StringComparison.Ordinal);
        Assert.Contains("Mongo:Database", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stub SSO provider issues a session to any caller. Outside
    /// Development that is an authentication bypass, and it must not boot.
    /// </summary>
    [Fact]
    public void The_stub_sign_in_provider_cannot_be_enabled_outside_development()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            using var broken = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(host =>
                {
                    host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Production);
                    host.UseSetting("Jwt:SigningKey", new string('k', 48));
                    host.UseSetting("Cors:Origins:0", "https://app.example.com");
                    host.UseSetting("Sso:EnableStubProvider", "true");
                });

            broken.CreateClient();
        });

        Assert.Contains("EnableStubProvider", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A production process with no object storage has no media at all.</b>
    ///
    /// The fixtures reader is registered only in Development, so without a
    /// bucket a production deployment serves no exam audio, no exam images and
    /// no dictation audio — and each of those presents as a broken player
    /// rather than as a missing adapter, which is the worst way for a
    /// deployment mistake to surface.
    /// </summary>
    [Fact]
    public void A_production_configuration_with_no_object_storage_refuses_to_boot()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            using var broken = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(host =>
                {
                    host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Production);
                    host.UseSetting("Jwt:SigningKey", new string('k', 48));
                    host.UseSetting("Cors:Origins:0", "https://app.example.com");
                });

            broken.CreateClient();
        });

        Assert.Contains("ObjectStorage", failure.Message, StringComparison.Ordinal);
    }


    /// <summary>
    /// <b>A production process with no mail sender reports success and sends
    /// nothing.</b>
    ///
    /// The only other sender writes the link to the server log. That is right
    /// in Development and an outright lie in production: an account is created,
    /// the API says a verification mail was sent, and the address can never be
    /// verified. Password reset is worse — the learner is locked out and the
    /// recovery path silently does nothing.
    /// </summary>
    [Fact]
    public void A_production_configuration_with_no_mail_sender_refuses_to_boot()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            using var broken = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(host =>
                {
                    host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Production);
                    host.UseSetting("Jwt:SigningKey", new string('k', 48));
                    host.UseSetting("Cors:Origins:0", "https://app.example.com");
                    host.UseSetting("ObjectStorage:ServiceUrl", "https://s3.example.com");
                    host.UseSetting("ObjectStorage:AccessKey", "key");
                    host.UseSetting("ObjectStorage:SecretKey", "secret");
                });

            broken.CreateClient();
        });

        Assert.Contains("Email is not configured", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Port 25 is unauthenticated relay with no expectation of encryption.
    ///
    /// A password-reset link travelling in the clear is a link anybody on the
    /// path can use, and it arrives looking exactly like the real one.
    /// </summary>
    [Fact]
    public void A_mail_sender_on_port_25_refuses_to_boot()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            using var broken = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(host =>
                {
                    host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Production);
                    host.UseSetting("Jwt:SigningKey", new string('k', 48));
                    host.UseSetting("Cors:Origins:0", "https://app.example.com");
                    host.UseSetting("ObjectStorage:ServiceUrl", "https://s3.example.com");
                    host.UseSetting("ObjectStorage:AccessKey", "key");
                    host.UseSetting("ObjectStorage:SecretKey", "secret");
                    host.UseSetting("Email:Host", "smtp.example.com");
                    host.UseSetting("Email:Port", "25");
                    host.UseSetting("Email:FromAddress", "no-reply@example.com");
                    host.UseSetting("Email:ClientBaseUrl", "https://app.example.com");
                });

            broken.CreateClient();
        });

        Assert.Contains("Email:Port is 25", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a fully configured production process starts.
    ///
    /// <b>The check that keeps the ones above honest.</b> A gate that refuses
    /// everything passes every negative test and ships a product nobody can
    /// deploy; this is what says the requirements are satisfiable.
    /// </summary>
    [SkippableFact]
    public void A_complete_production_configuration_starts()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        using var configured = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Production);
                host.UseSetting("Jwt:SigningKey", new string('k', 48));
                host.UseSetting("Cors:Origins:0", "https://app.example.com");
                host.UseSetting("Mongo:ConnectionString", "mongodb://localhost:27018/?directConnection=true");
                host.UseSetting("Mongo:Database", $"vni_prod_smoke_{Guid.NewGuid():n}");
                host.UseSetting("ObjectStorage:ServiceUrl", "https://s3.example.com");
                host.UseSetting("ObjectStorage:AccessKey", "key");
                host.UseSetting("ObjectStorage:SecretKey", "secret");
                host.UseSetting("Email:Host", "smtp.example.com");
                host.UseSetting("Email:FromAddress", "no-reply@example.com");
                host.UseSetting("Email:ClientBaseUrl", "https://app.example.com");
            });

        // Creating the client is what builds and starts the host.
        Assert.NotNull(configured.CreateClient());
    }

}
