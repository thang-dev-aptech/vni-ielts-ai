using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Vni.Ielts.Api.Common;
using Vni.Ielts.Api.Endpoints;
using Vni.Ielts.Infrastructure;
using Vni.Ielts.Infrastructure.Security;

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
builder.Services.AddOpenApi();
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

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).WithTags("Ops");

if (!app.Environment.IsDevelopment())
{
    // There is no production email sender yet. Refusing to boot is the only
    // honest option: starting would mean registering users who can never
    // verify, while the API reports that a message was sent.
    throw new InvalidOperationException(
        "No production IVerificationMessageSender is configured. Registration would report "
        + "that a verification email was sent when none was. Choose and wire a provider "
        + "before deploying outside Development — note the PDPL position (B-2) applies to it.");
}

await app.Services.InitialiseInfrastructureAsync();

app.Run();

/// <summary>Exposed so the integration tests can spin the real app up.</summary>
public partial class Program;
