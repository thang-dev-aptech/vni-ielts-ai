using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Vni.Ielts.Application.Common;

namespace Vni.Ielts.Api.Common;

/// <summary>
/// Rate-limit policies, one per cost class.
///
/// <para>
/// <b>Keyed on authenticated identity first, IP only as a fallback.</b> IP
/// alone is both shared — mobile carrier NAT puts thousands of learners behind
/// one address — and trivially rotatable, so it is a poor primary key in both
/// directions.
/// </para>
///
/// <para>
/// <b>The classes exist because the costs differ by orders of magnitude.</b> A
/// list request costs a database read; a submission costs a paid provider call.
/// One global limit would either throttle reading or leave spending unguarded.
/// </para>
/// </summary>
public static class RateLimitPolicies
{
    public const string Authentication = "auth";
    public const string Registration = "registration";
    public const string Submission = "submission";
    public const string InSessionRead = "in-session-read";

    public static void AddVniRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Sign-in happens BEFORE authentication, so this partitions by IP —
            // and that is exactly why the limit cannot be tight.
            //
            // An earlier value of 10/minute reasoned "a human does not sign in
            // ten times a minute", which is true of a human and false of an
            // address. Vietnamese mobile carriers put very large numbers of
            // subscribers behind one NAT address, and a school or office is one
            // address too. At 10/minute the first ten people to open the app
            // lock out everyone else on their network — a self-inflicted outage
            // that looks exactly like the attack it was meant to stop.
            //
            // So this bound is deliberately loose. It exists to stop a crude
            // flood, not to stop credential stuffing.
            //
            // Credential stuffing is stopped by `ILoginThrottle` instead — a
            // per-address consecutive-failure lockout in the application layer,
            // which is the only layer that knows WHICH account is being
            // attacked. The two controls are complements, not alternatives:
            // this one bounds traffic from an address, that one bounds guesses
            // against an account. → threats T4, T5
            options.AddPolicy(Authentication, http => Fixed(http.PartitionKey(), permit: 120, minutes: 1));

            // Same NAT reasoning, but registration is genuinely rare per person
            // and each account is a potential free AI spend, so it stays tighter
            // than sign-in. → threats T4, T13
            options.AddPolicy(Registration, http => Fixed(http.PartitionKey(), permit: 30, minutes: 10));

            // Each of these can cost a real provider call. → threats T11, T22
            options.AddPolicy(Submission, http => Fixed(http.PartitionKey(), permit: 20, minutes: 1));

            // Deliberately generous. Never rate-limit a learner out of an
            // in-progress exam — that converts a defensive control into a
            // scoring incident, which is worse than the abuse it prevents.
            options.AddPolicy(InSessionRead, http => Fixed(http.PartitionKey(), permit: 300, minutes: 1));

            options.OnRejected = async (context, ct) =>
            {
                // Always tell the client when to come back. A 429 with no
                // Retry-After invites an immediate retry, which makes the
                // problem worse.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    $$"""{"title":"Too many requests","status":429,"code":"{{ErrorCodes.RateLimited}}","detail":"Too many requests. Please wait before trying again."}""",
                    ct);
            };
        });

    private static RateLimitPartition<string> Fixed(string key, int permit, int minutes) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit,
            Window = TimeSpan.FromMinutes(minutes),
            QueueLimit = 0,          // Reject immediately rather than queueing.
            AutoReplenishment = true,
        });

}
