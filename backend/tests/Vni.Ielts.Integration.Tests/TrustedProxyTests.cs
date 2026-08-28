using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace Vni.Ielts.Integration.Tests;

/// <summary>
/// F2.4 — before this, nothing processed `X-Forwarded-For` at all, so
/// <c>Connection.RemoteIpAddress</c> — what rate-limit partitioning keys on
/// — was always the direct TCP peer. Behind a real reverse proxy that is the
/// proxy, not the caller: every anonymous learner behind one proxy would
/// have shared one partition, turning the NAT-aware "generous" limits into a
/// bound on the API's entire anonymous traffic.
///
/// Both tests drive the real rate limiter to its actual 429, through the
/// real pipeline — the property under test is partitioning behaviour, and a
/// mocked limiter would only prove the mock's opinion of it.
/// </summary>
public sealed class TrustedProxyTests
{
    /// <summary>30/10min — small enough to exhaust quickly and still real.</summary>
    private const int RegistrationLimit = 30;

    [SkippableFact]
    public async Task Without_a_trusted_proxy_a_spoofed_header_does_not_change_the_partition()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        // No TrustedProxy configured — the default a bare deployment has.
        using var app = NewFactory(trustedProxyAddress: null);
        var client = app.CreateClient();

        // Every one of these claims a different forwarded address. If the
        // header were honoured with nothing configured to trust it, each
        // would land in its own partition and none would ever see 429.
        for (var i = 0; i < RegistrationLimit; i++)
        {
            await SendRegisterAsync(client, forwardedFor: $"203.0.113.{i}");
        }

        // One more claimed address, still unconfigured to trust it — must
        // still land in the same (real-peer) bucket as all the others,
        // which is now exhausted.
        var response = await SendRegisterAsync(client, forwardedFor: "203.0.113.250");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [SkippableFact]
    public async Task With_a_trusted_proxy_different_forwarded_addresses_get_separate_partitions()
    {
        Skip.IfNot(SsoAppFactory.MongoAvailable, SsoAppFactory.SkipReason);

        // TestServer's own connecting peer is loopback — trusting it here
        // mirrors trusting a real reverse proxy running in front of the API.
        using var app = NewFactory(trustedProxyAddress: "127.0.0.1");
        var client = app.CreateClient();

        for (var i = 0; i < RegistrationLimit; i++)
        {
            await SendRegisterAsync(client, forwardedFor: "203.0.113.5");
        }

        var blocked = await SendRegisterAsync(client, forwardedFor: "203.0.113.5");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        // A different caller, forwarded through the same trusted proxy, has
        // its own untouched partition — proof this is keyed on the real
        // client address, not shared across everyone behind the proxy.
        var fresh = await SendRegisterAsync(client, forwardedFor: "203.0.113.6");
        Assert.NotEqual(HttpStatusCode.TooManyRequests, fresh.StatusCode);
    }

    private static Task<HttpResponseMessage> SendRegisterAsync(HttpClient client, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Content = JsonContent.Create(new
        {
            email = "trusted-proxy-fixture@example.com",
            password = "Password123!Aa",
            displayName = "Fixture",
        });

        return client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> NewFactory(string? trustedProxyAddress)
    {
        var database = $"vni_ielts_trustedproxy_test_{Guid.NewGuid():n}";

        return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting(WebHostDefaults.EnvironmentKey, Environments.Development);
            host.UseSetting(
                "Mongo:ConnectionString", "mongodb://localhost:27018/?directConnection=true");
            host.UseSetting("Mongo:Database", database);
            host.UseSetting("Jwt:SigningKey", new string('k', 48));
            host.UseSetting("Sso:EnableStubProvider", "true");
            host.UseSetting("Sso:Google:ClientId", string.Empty);
            host.UseSetting("Sso:Google:ClientSecret", string.Empty);
            host.UseSetting("Sso:ClientBaseUrl", "http://localhost:5173");
            host.UseSetting("Sso:Google:RedirectUri", "http://localhost/api/v1/auth/sso/google/callback");

            if (trustedProxyAddress is not null)
            {
                host.UseSetting("TrustedProxy:Addresses:0", trustedProxyAddress);
            }
        });
    }
}
