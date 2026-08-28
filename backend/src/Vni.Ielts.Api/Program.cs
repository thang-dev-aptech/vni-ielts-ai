using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Api.Endpoints;
using Vni.Ielts.Infrastructure;
using Vni.Ielts.Infrastructure.Security;

/*
 * ── `--healthcheck`, before anything is built ─────────────────────────────
 *
 * <b>A container's health probe should not need a second binary.</b> The
 * `aspnet` runtime image ships no `curl` and no `wget`, so a `HEALTHCHECK` in
 * the Dockerfile has three options: install a package into the image, run a
 * shell that cannot make an HTTP request, or ask the application itself. The
 * third is the smallest and the only one that stays true if the port changes.
 *
 * Deliberately before `CreateBuilder`: this mode must not construct the
 * application, resolve services or touch the database. It is a client, not a
 * server, and a probe that boots a second copy of the process would be a probe
 * that fails under memory pressure it caused.
 */
if (args.Contains("--healthcheck"))
{
    var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080";
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

    try
    {
        var answer = await probe.GetAsync($"http://127.0.0.1:{port}/health/ready");
        return answer.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception)
    {
        // Unreachable, refused, or too slow. All of them mean the same thing to
        // an orchestrator, and none of them is worth a stack trace on stdout.
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

// The token service records which device a sign-in came from, and only an HTTP
// request knows that. Infrastructure must not reference ASP.NET Core, so the
// Api supplies it through a port. → RequestDevice
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Vni.Ielts.Application.Identity.IRequestDevice, RequestDevice>();
builder.Services.AddVniRateLimiting();

// A request body cap, enforced by Kestrel before a byte reaches application
// code. Without it a single large POST can exhaust memory, and Argon2id makes
// that cheap for an attacker — a multi-megabyte password field would be hashed
// at 19 MiB of working memory per attempt. Exam submissions are the largest
// legitimate payload here and are far under this.
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 1 * 1024 * 1024;          // 1 MB
    kestrel.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
    // A slow-drip request holds a connection open indefinitely otherwise.
    kestrel.Limits.MinRequestBodyDataRate =
        new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(
            bytesPerSecond: 100, gracePeriod: TimeSpan.FromSeconds(10));
});
/*
 * <b>The OpenAPI document, and it is a product artefact rather than a dev toy.</b>
 *
 * `packages/api-client` is generated from it, so the shape below is what both
 * the learner app and the CMS are typed against. Two hand-written copies of one
 * contract is how `A17` happened: the client spelled a multi-select pick
 * `"A|D"`, the marker accepted `"A,D"`, both had passing tests, and nobody
 * owned the sentence between them — six Reading marks and seven Listening
 * marks lost on every sitting.
 *
 * The document is committed under `contracts/openapi` and a test fails when the
 * two disagree, so a contract change cannot merge without its diff.
 * → `OpenApiContractTests`, `I7.1`
 */
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new Microsoft.OpenApi.OpenApiInfo
        {
            Title = "VNI IELTS AI",
            Version = "v1",
            Description =
                "The exam engine, identity, and content APIs. Generated from the running "
                + "application — never hand-edited.",
        };

        /*
         * <b>Declared once, at the document, rather than per operation.</b>
         * Every authenticated route uses the same bearer scheme, and a
         * generator that cannot see it emits a client with no way to send a
         * token — which is the one thing every call needs.
         */
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
        document.Components.SecuritySchemes ??=
            new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["bearer"] =
            new Microsoft.OpenApi.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description =
                    "The access token from /api/v1/auth/login, /auth/refresh or the SSO "
                    + "handoff. Fifteen minutes; the client refreshes ahead of expiry.",
            };

        return Task.CompletedTask;
    });

    /*
     * <b>A cleared answer is `null`, and the contract said it could not be.</b>
     *
     * `changes` is `IReadOnlyDictionary<string, string?>` and the `null` is
     * load-bearing: a patch carries an entry with a null value precisely when
     * the learner rubbed that answer out, and "absent" means "untouched". The
     * generator honours nullable reference types on ordinary properties and
     * does not reach inside a dictionary's value, so the emitted schema said
     * `string` — and a client generated from it would refuse to send an erase.
     *
     * Fixed here rather than by reshaping the DTO, because the DTO is right.
     */
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(SaveAnswersRequest)
            && schema.Properties?.TryGetValue("changes", out var changes) == true
            && changes is Microsoft.OpenApi.OpenApiSchema concrete)
        {
            concrete.AdditionalProperties = new Microsoft.OpenApi.OpenApiSchema
            {
                Type = Microsoft.OpenApi.JsonSchemaType.String
                    | Microsoft.OpenApi.JsonSchemaType.Null,
            };
        }

        /*
         * ── Every integer in the contract said it might be a string ─────────
         *
         * <b>Found 2026-08-28 by the parity test, on the first run that ever
         * had a generated client to compare against.</b>
         *
         * .NET 10's OpenAPI emitter writes an integer as
         * `"type": ["integer", "string"]` with a numeric `pattern`, describing
         * a JSON Schema dialect that permits a string-encoded number. This API
         * does not: nothing configures `JsonNumberHandling`, so
         * `System.Text.Json` neither reads nor writes a number as a string, and
         * every response it has ever sent carries a bare number.
         *
         * <b>The contract was therefore wrong about its own output, and the
         * damage lands on the generated client.</b> `attempts` came out as
         * `number | string`, `remainingSeconds` as `number | string`,
         * `overallBand` as `null | number | string` — so a screen that subtracts
         * one from a countdown either fails to compile or, if somebody reaches
         * for `+`, concatenates. That is precisely the class of defect
         * `packages/api-client` exists to make impossible, arriving through the
         * generator rather than through a hand-written copy.
         *
         * Collapsed back to the type the API actually serialises. `Null` is
         * preserved where the property allows it — that one is real.
         */
        if (schema is Microsoft.OpenApi.OpenApiSchema editable)
        {
            NarrowStringUnions(editable);
        }

        return Task.CompletedTask;

        static void NarrowStringUnions(Microsoft.OpenApi.OpenApiSchema target)
        {
            var type = target.Type;

            if (type is { } present
                && (present.HasFlag(Microsoft.OpenApi.JsonSchemaType.Integer)
                    || present.HasFlag(Microsoft.OpenApi.JsonSchemaType.Number))
                && present.HasFlag(Microsoft.OpenApi.JsonSchemaType.String))
            {
                target.Type = present & ~Microsoft.OpenApi.JsonSchemaType.String;

                // The pattern only existed to constrain the string alternative.
                // Left behind, it is a numeric regex on a numeric type — noise
                // that some generators turn into a runtime check.
                target.Pattern = null;
            }

            if (target.Properties is not null)
            {
                foreach (var property in target.Properties.Values)
                    if (property is Microsoft.OpenApi.OpenApiSchema nested)
                        NarrowStringUnions(nested);
            }

            if (target.Items is Microsoft.OpenApi.OpenApiSchema items)
                NarrowStringUnions(items);

            if (target.AdditionalProperties is Microsoft.OpenApi.OpenApiSchema additional)
                NarrowStringUnions(additional);
        }
    });

    /*
     * <b>Two responses every guarded route can give, added from metadata
     * rather than by hand.</b>
     *
     * A `401` on anything requiring authorization and a `429` on anything rate
     * limited are both facts the endpoint already carries — so writing them out
     * on ninety routes would be ninety chances to forget one, and a generated
     * client would then have no type for an error it will certainly meet.
     *
     * Nothing is guessed: an operation gets these only when its own metadata
     * says the route has them.
     */
    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        // Minimal APIs always populate this; the null check is the compiler's
        // price for the property being declared nullable on the interface.
        operation.Responses ??= [];

        if (metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any())
        {
            operation.Responses.TryAdd(
                "401",
                new Microsoft.OpenApi.OpenApiResponse
                {
                    Description = "No token, or one this API will not accept.",
                });

            operation.Security =
            [
                new Microsoft.OpenApi.OpenApiSecurityRequirement
                {
                    [new Microsoft.OpenApi.OpenApiSecuritySchemeReference("bearer", context.Document)]
                        = [],
                },
            ];
        }

        if (metadata.OfType<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>().Any())
        {
            operation.Responses.TryAdd(
                "429",
                new Microsoft.OpenApi.OpenApiResponse
                {
                    Description =
                        "Rate limited. Always carries Retry-After — a 429 without it invites "
                        + "an immediate retry, which makes the problem worse.",
                });
        }

        return Task.CompletedTask;
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<VniExceptionHandler>();

// The signing key: generated locally, demanded everywhere else.
//
// A misconfigured key that only surfaces when someone tries to sign in is a
// production incident; one that refuses to boot is a deployment failure, and
// that is the cheaper of the two. So outside Development this throws.
//
// Inside Development it generates one instead. Requiring every developer to
// export a variable before the API would start bought nothing: the value was
// not secret, everyone pasted the same literal from the README, and the only
// real effect was a confusing first-run failure. A per-run random key is
// strictly better — nothing to leak, nothing to paste, nothing to accidentally
// carry into a real deployment.
//
// The cost is stated out loud below rather than left to be discovered.
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey is missing or shorter than 32 bytes. Supply it through environment "
            + "configuration — never a committed file. See CLAUDE.md rule 6.");
    }

    jwt.SigningKey = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));

    builder.Configuration[$"{JwtOptions.SectionName}:SigningKey"] = jwt.SigningKey;

    Console.WriteLine(
        "[dev] No Jwt:SigningKey supplied, so one was generated for this run.\n"
        + "[dev] Existing sessions do not survive a restart — you will be signed out.\n"
        + "[dev] Set Jwt__SigningKey in the environment to keep sessions across restarts.");
}

/*
 * <b>Everything else the process needs, checked before anything starts.</b>
 *
 * The signing key above had a guard because somebody had been bitten by it;
 * nothing else did. A wrong issuer, an empty CORS list, a Mongo string pointing
 * at a standalone node and a missing SSO callback all surfaced at runtime, as
 * user problems, with no server-side trace in the CORS case at all.
 * → `StartupConfiguration`
 */
StartupConfiguration.ValidateOrThrow(builder);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the token's own claim names. By default the handler rewrites
        // `sub` to a WS-Federation URI, which made three call sites disagree
        // about how to read the caller's id — and two of them silently got
        // null. See CallerIdentity for what that cost.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            // Default is five minutes, which would let an expired token keep
            // working. On a product with a server-authoritative exam clock,
            // silently tolerating clock drift is the wrong default.
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "name",
            // `sub` now arrives unmapped, so this is the id claim.
            RoleClaimType = ClaimTypes.Role,
        };
    });

builder.Services.AddAuthorization();

// The learner web app and the CMS are separate origins in development.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(
        // A header the browser cannot read may as well not be sent. Only a
        // short safelist is readable by default, and none of these are on it.
        ServerTimeMiddleware.HeaderName,   // exam clock reconciliation
        "Retry-After",                     // without it a 429 tells the client nothing
        "Idempotency-Replayed")));         // lets a client tell a replay from a fresh write

var app = builder.Build();

// Before everything else, so even an error or a rate-limit rejection carries
// the server clock — the client reconciles its exam timer from it, and a
// throttled client still needs a correct clock.
app.UseMiddleware<ServerTimeMiddleware>();

// Also early: an error response and a 429 need these as much as a 200 does,
// and both are produced before any endpoint runs.
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseExceptionHandler();
app.UseStatusCodePages();

/*
 * <b>Served in Development only.</b> The document names every route and every
 * shape this API accepts, which is a map for anybody probing it — and there is
 * nothing a production client needs it for, because the client is generated
 * from the committed copy at build time rather than fetched at runtime.
 */
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// AFTER authentication, so limits partition on the authenticated subject
// rather than on a shared carrier-NAT address. Before it, every learner on one
// mobile network would share a bucket.
app.UseRateLimiter();

// After authorization: the idempotency key is scoped per caller, so it needs
// the identity established first.
app.UseMiddleware<IdempotencyMiddleware>();

app.MapAuthEndpoints();
app.MapSsoEndpoints();
app.MapAccountEndpoints();
app.MapExamEndpoints();
app.MapDictationEndpoints();
app.MapAdminEndpoints();

/*
 * <b>Two endpoints, because they answer two different questions.</b> What stood
 * here returned `ok` unconditionally — the same answer whether the database was
 * reachable or not, which makes it worse than having none: a load balancer
 * routes traffic to it and a deployment goes green on it while the system
 * serves 500s. → `HealthEndpoints`
 */
app.MapHealthEndpoints();

await app.Services.InitialiseInfrastructureAsync();

app.Run();

// `--healthcheck` above returns 1 on a failed probe, so this file has to have a
// return value on every path. `app.Run()` blocks until shutdown; reaching here
// means the process was asked to stop, which is a success.
return 0;

/// <summary>Exposed so the integration tests can spin the real app up.</summary>
public partial class Program;
