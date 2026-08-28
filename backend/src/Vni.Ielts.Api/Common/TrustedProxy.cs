using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// Which peers this deployment believes when they say "I am forwarding for
/// someone else".
///
/// <b>F2.4 — before this, nothing read `X-Forwarded-For` at all.</b>
/// <c>context.Connection.RemoteIpAddress</c> — what the rate limiter and the
/// audit trail both key on — is the TCP peer, and behind any real reverse
/// proxy or load balancer that peer is the proxy itself, not the caller.
/// Every learner behind the same proxy shared one rate-limit partition; the
/// generous NAT-aware limits (120/min sign-in, 30/10min registration) that
/// were sized for "a whole mobile carrier's subscribers" then applied to
/// this API's *entire* anonymous traffic, behind one address, at once. The
/// first handful of real requests would have exhausted it — a self-inflicted
/// outage that presents exactly like the flood the limit exists to stop.
///
/// <b>Configured, not invented — and empty means "process nothing", not
/// "restrict nothing".</b> Which addresses or networks a deployment's own
/// reverse proxy runs on is an operational fact nobody here can guess, so
/// `TrustedProxy:Addresses` / `TrustedProxy:Networks` name it.
///
/// <b>The footgun this file exists to avoid, found by its own test suite,
/// not by reading documentation first.</b> `ForwardedHeadersMiddleware`'s
/// trust check is skipped entirely when both `KnownProxies` and
/// `KnownIPNetworks` are empty — an empty list does not mean "trust no
/// peer", it means "no restriction is configured", and the middleware then
/// honours `X-Forwarded-For` from *any* caller. A test asserting the
/// opposite failed with the forwarded (spoofed) address showing up as the
/// resolved `RemoteIpAddress` even with nothing configured — confirmed by
/// tracing the actual partition key at runtime, not inferred. So with
/// nothing configured, this sets `ForwardedHeaders.None`: the middleware
/// does not look at the header at all, and `RemoteIpAddress` stays the real
/// TCP peer. Anything less would make "unconfigured" the least safe state
/// instead of the safest one.
///
/// <b>What "trusted" actually buys.</b> `ForwardedHeadersMiddleware` only
/// honours `X-Forwarded-For` from a peer in this list. A caller who is not
/// itself the configured proxy cannot spoof the header to relabel its own
/// address — the immediate TCP peer has to be the trusted proxy for its
/// claim about who it is forwarding for to be believed at all.
/// </summary>
public sealed class TrustedProxyOptions
{
    public const string SectionName = "TrustedProxy";

    /// <summary>Exact addresses of proxies allowed to set forwarding headers.</summary>
    public string[] Addresses { get; set; } = [];

    /// <summary>Networks (CIDR, e.g. "10.0.0.0/8") allowed to set forwarding headers.</summary>
    public string[] Networks { get; set; } = [];
}

public static class TrustedProxyExtensions
{
    /// <summary>
    /// Builds the options ASP.NET Core's own forwarded-headers middleware
    /// uses, from this deployment's configured trust list.
    /// </summary>
    public static ForwardedHeadersOptions ToForwardedHeadersOptions(this TrustedProxyOptions trusted)
    {
        var configured = trusted.Addresses.Length > 0 || trusted.Networks.Length > 0;

        var options = new ForwardedHeadersOptions
        {
            /*
             * <b>`None` when nothing is configured — not "the flags, with an
             * empty trust list".</b> An empty `KnownProxies`/`KnownIPNetworks`
             * does not mean "trust no peer" to this middleware; it means "no
             * restriction is configured", and it then honours the header from
             * every peer. `None` is the only setting that actually makes
             * "unconfigured" the safe state.
             */
            ForwardedHeaders = configured
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                : ForwardedHeaders.None,

            // <b>One hop, not "however many the header claims".</b> A proxy
            // chain longer than this deployment actually has is not a fact
            // to trust from the request itself — `ForwardLimit` bounds how
            // many entries of a comma-separated X-Forwarded-For are walked,
            // so a caller cannot pad the header to smuggle an address past
            // the trust check.
            ForwardLimit = 1,
        };

        // Cleared rather than left at the framework's own default (which
        // pre-populates loopback) — the trust list is exactly what
        // TrustedProxy:Addresses/Networks says and nothing implicit.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var address in trusted.Addresses)
        {
            if (!IPAddress.TryParse(address, out var parsed))
            {
                throw new InvalidOperationException(
                    $"TrustedProxy:Addresses contains '{address}', which is not a valid IP address.");
            }

            options.KnownProxies.Add(parsed);
        }

        foreach (var network in trusted.Networks)
        {
            // Qualified: Microsoft.AspNetCore.HttpOverrides also declares an
            // IPNetwork type (for the now-obsolete KnownNetworks), and the two
            // are ambiguous unqualified in a file that uses HttpOverrides.
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException(
                    $"TrustedProxy:Networks contains '{network}', which is not valid CIDR notation " +
                    "(e.g. \"10.0.0.0/8\").");
            }

            options.KnownIPNetworks.Add(parsed);
        }

        return options;
    }
}
