using System.Security.Cryptography;
using System.Text;
using Vni.Ielts.Application.Common;
using Vni.Ielts.Domain.Common;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Application.Identity;

public sealed record StartSsoCommand(string ProviderKey, string? ReturnTo);

public sealed record StartSsoResult(Uri AuthorizationUrl);

/// <summary>
/// Opens a social sign-in.
///
/// Generates the three per-request secrets — <c>state</c>, the PKCE verifier
/// and <c>nonce</c> — stores them server-side against a short TTL, and returns
/// the finished authorization URL. The client receives a URL and nothing else:
/// no client id, no verifier, no state. → ADR-0014
/// </summary>
public sealed class StartSsoSignIn(
    IExternalIdentityProviderRegistry registry,
    ISsoStateStore states,
    IClock clock)
{
    /// <summary>
    /// How long the person has to finish at the provider.
    ///
    /// Ten minutes is generous for a consent screen and short enough that an
    /// abandoned attempt cannot be picked up later from a shared machine.
    /// </summary>
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public async Task<Result<StartSsoResult>> HandleAsync(StartSsoCommand command, CancellationToken ct)
    {
        if (!registry.TryResolve(command.ProviderKey, out var provider))
        {
            // Unknown and not-configured are one answer. Which providers a
            // deployment has credentials for is discoverable from the
            // providers endpoint; it does not need a second, subtler channel.
            return Error.NotFound(
                ErrorCodes.SsoProviderUnknown, "That sign-in provider is not available.");
        }

        var state = Secrets.UrlSafe(32);
        var verifier = Secrets.UrlSafe(64);
        var nonce = Secrets.UrlSafe(32);

        await states.StoreAsync(
            new SsoState(
                state,
                provider.Provider,
                verifier,
                nonce,
                SafeReturnTo(command.ReturnTo),
                clock.UtcNow.Add(StateLifetime)),
            ct);

        return new StartSsoResult(
            await provider.BuildAuthorizationUrlAsync(
                new AuthorizationRequest(state, Secrets.S256Challenge(verifier), nonce), ct));
    }

    /// <summary>
    /// Where the client asked to be sent after signing in.
    ///
    /// <para>
    /// <b>Only a same-site absolute path survives this.</b> Anything with a
    /// scheme, a host, a backslash, or a leading <c>//</c> is discarded rather
    /// than rejected, because the sign-in should still work. An open redirect
    /// on an authentication callback is not a nuisance bug: it is how a
    /// convincing credential-phishing chain starts, since the link genuinely
    /// begins at our domain.
    /// </para>
    /// </summary>
    internal static string? SafeReturnTo(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();

        if (!value.StartsWith('/'))
            return null;
        if (value.StartsWith("//", StringComparison.Ordinal))
            return null;
        // `/\evil.example` is treated as protocol-relative by several browsers.
        if (value.StartsWith("/\\", StringComparison.Ordinal))
            return null;
        if (value.Contains('\\') || value.Contains("://", StringComparison.Ordinal))
            return null;
        if (value.Length > 512)
            return null;
        // A control character can split a header if this is ever reflected into one.
        if (value.Any(char.IsControl))
            return null;

        return value;
    }
}

public sealed record SsoCallbackCommand(
    string ProviderKey, string? Code, string? State, string? ProviderError);

public sealed record SsoCallbackResult(string HandoffCode, string? ReturnTo);

/// <summary>
/// The provider's callback: exchange the code, decide which account this is,
/// and mint the one-time handoff code.
///
/// <para>
/// <b>This class holds the M-1 decision.</b> One email is one account: a
/// social sign-in on a matching address links to the existing account rather
/// than creating a second one, silently, with no confirmation screen — but
/// only when the provider vouches for the address. The full reasoning,
/// including the takeover this would otherwise open in both directions, is in
/// ADR-0013 and threat T1. Do not relax either condition here without
/// superseding that ADR.
/// </para>
/// </summary>
public sealed class SignInWithSso(
    IExternalIdentityProviderRegistry registry,
    ISsoStateStore states,
    IUserRepository users,
    IUserIdentityRepository identities,
    IRoleRepository roles,
    ITokenService tokens,
    IHandoffCodeStore handoffCodes,
    IClock clock)
{
    public async Task<Result<SsoCallbackResult>> HandleAsync(
        SsoCallbackCommand command, CancellationToken ct)
    {
        if (!registry.TryResolve(command.ProviderKey, out var provider))
            return Error.NotFound(ErrorCodes.SsoProviderUnknown, "That sign-in provider is not available.");

        if (string.IsNullOrWhiteSpace(command.State))
            return Error.Validation(ErrorCodes.SsoStateInvalid, "This sign-in link is no longer valid.");

        // Consumed before anything else is looked at, including the provider's
        // own error parameter. An unconsumed state is a replayable state, and
        // a callback that fails early without spending it leaves one usable.
        var state = await states.ConsumeAsync(command.State, ct);

        if (state is null || state.Provider != provider.Provider || state.ExpiresAt <= clock.UtcNow)
            return Error.Validation(ErrorCodes.SsoStateInvalid, "This sign-in link is no longer valid.");

        if (!string.IsNullOrWhiteSpace(command.ProviderError))
        {
            // The provider's own text is attacker-influenceable and is not
            // repeated back. `access_denied` is the ordinary "user pressed
            // cancel" case and is not an error worth alarming anyone about.
            return Error.Validation(ErrorCodes.SsoDenied, "Sign-in was cancelled.");
        }

        if (string.IsNullOrWhiteSpace(command.Code))
            return Error.Validation(ErrorCodes.SsoExchangeFailed, "Sign-in could not be completed.");

        var exchanged = await provider.ExchangeCodeAsync(command.Code, state.CodeVerifier, state.Nonce, ct);
        if (!exchanged.IsSuccess)
            return exchanged.Error;

        var external = exchanged.Value!;

        var resolved = await ResolveUserAsync(provider, external, ct);
        if (!resolved.IsSuccess)
            return resolved.Error;

        return new SsoCallbackResult(
            await handoffCodes.IssueAsync(resolved.Value!.Id, ct), state.ReturnTo);
    }

    private async Task<Result<User>> ResolveUserAsync(
        IExternalIdentityProvider provider, ExternalIdentity external, CancellationToken ct)
    {
        // 1 · Already linked. The ordinary case after the first sign-in, and
        //     the only one that does not care about the email address at all —
        //     which is the point of keying on the provider's subject.
        var known = await identities.FindByProviderAsync(external.Provider, external.Subject, ct);
        if (known is not null)
        {
            var linked = await users.FindByIdAsync(known.UserId, ct);
            if (linked is null)
            {
                // An identity pointing at a user that no longer exists is a
                // data defect, not a sign-in failure to explain to anyone.
                return Error.Unauthorized(
                    ErrorCodes.SsoExchangeFailed, "Sign-in could not be completed.");
            }

            return CanSignIn(linked);
        }

        // 2 · A new identity, so an address is required. A provider that
        //     sends none leaves nothing to link and nothing to create from.
        if (!Email.TryCreate(external.Email, out var email))
        {
            return Error.Validation(
                ErrorCodes.SsoEmailMissing,
                "This provider did not share an email address, which is required to sign in.");
        }

        var existing = await users.FindByEmailAsync(email, ct);

        // 3 · No account with that address: create one.
        if (existing is null)
            return await CreateAsync(external, email, ct);

        // 4 · An account exists. This is the M-1 branch.
        if (!provider.AssertsEmailVerification || !external.EmailVerified)
        {
            // The provider will not vouch for the address, so a match proves
            // nothing. Linking here is exactly threat T1. → ADR-0013
            return Error.Conflict(
                ErrorCodes.IdentityLinkRequired,
                "An account already uses this email address. Sign in with your password first, "
                + "then link this provider from your profile.");
        }

        var allowed = CanSignIn(existing);
        if (!allowed.IsSuccess)
            return allowed;

        await LinkAsync(existing, external, ct);
        return existing;
    }

    private async Task<Result<User>> CreateAsync(
        ExternalIdentity external, Email email, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var user = User.Register(email, DisplayNameFor(external, email), now);

        // A provider-verified address is verified here too. That is what makes
        // social sign-up worth having: the account arrives past the gate that
        // entitlement accrual and referral confirmation wait on (T4, T13).
        // An unverified one does not, and the account carries the same
        // unverified state an email registration would.
        if (external.EmailVerified)
            user.MarkEmailVerified();

        var learner = await roles.FindByNameAsync(SystemRoles.Learner, ct);
        if (learner is not null)
            user.AssignRole(learner.Id);

        try
        {
            await users.AddAsync(user, ct);
        }
        catch (DuplicateEmailException)
        {
            // Two sign-ins for the same new address arrived together and both
            // passed the lookup. The unique index is what enforces the rule.
            // Re-read and fall into the linking branch rather than failing —
            // to the person on the losing request this was a normal sign-in.
            var winner = await users.FindByEmailAsync(email, ct);
            if (winner is null)
                return Error.Unauthorized(ErrorCodes.SsoExchangeFailed, "Sign-in could not be completed.");

            var allowed = CanSignIn(winner);
            if (!allowed.IsSuccess)
                return allowed;

            await LinkAsync(winner, external, ct);
            return winner;
        }

        var attached = await AttachAsync(user, external, now, ct);
        return attached ?? user;
    }

    /// <summary>
    /// Links the provider identity, tolerating another sign-in having linked
    /// it a moment earlier.
    ///
    /// <para>
    /// Returns the account that ended up owning the identity, or null if that
    /// is the one passed in. The race is narrow — two tabs finishing at once —
    /// but the unique index turns it into a driver exception, and a 500 for
    /// someone who is in fact signed in is the worst possible response to it.
    /// </para>
    /// </summary>
    private async Task<User?> AttachAsync(
        User user, ExternalIdentity external, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await identities.AddAsync(
                UserIdentity.ForSocial(user.Id, external.Provider, external.Subject, now), ct);
            return null;
        }
        catch (DuplicateIdentityException)
        {
            var winner = await identities.FindByProviderAsync(
                external.Provider, external.Subject, ct);

            // If it is gone again, this was not the race it looked like and
            // the caller's own account is still the right answer.
            if (winner is null || winner.UserId == user.Id)
                return null;

            return await users.FindByIdAsync(winner.UserId, ct);
        }
    }

    /// <summary>
    /// Attaches the provider to an existing account — and evicts a squatter if
    /// there is one.
    ///
    /// <para>
    /// The eviction is the half that is easy to leave out. Registration creates
    /// a <c>User</c> before the address is proven, so anyone can register an
    /// address they do not own, set a password, and wait for the real owner to
    /// arrive through Google. Marking the address verified without removing
    /// that password would leave the squatter holding a working credential on
    /// the merged account. → ADR-0013, threat T1
    /// </para>
    /// </summary>
    private async Task LinkAsync(User user, ExternalIdentity external, CancellationToken ct)
    {
        await AttachAsync(user, external, clock.UtcNow, ct);

        if (user.EmailVerified)
            return;

        user.MarkEmailVerified();

        // The display name comes from the provider too, and only here.
        //
        // An account that never proved its address was set up by someone who
        // may not be the person now signing in — and they chose the name on
        // it. Leaving it in place means the real owner inherits a name a
        // stranger picked, which at best is confusing and at worst is abuse
        // aimed at whoever eventually claims the address. On a verified
        // account the name is the person's own and is never touched.
        var provided = external.DisplayName?.Trim();
        if (!string.IsNullOrEmpty(provided))
            user.Rename(provided.Length > 100 ? provided[..100] : provided);

        await users.SaveAsync(user, ct);

        foreach (var identity in await identities.ListForUserAsync(user.Id, ct))
        {
            if (identity.Provider != IdentityProvider.Email || identity.PasswordHash is null)
                continue;

            identity.ClearPassword();
            await identities.SaveAsync(identity, ct);
        }

        // Anything already signed in on that password loses its session. The
        // legitimate user is unaffected: they are arriving now, through the
        // provider, and their token pair has not been issued yet.
        await tokens.RevokeAllForUserAsync(user.Id, ct);
    }

    private static Result<User> CanSignIn(User user) =>
        user.CanAuthenticate
            ? user
            : Error.Forbidden(ErrorCodes.AccountSuspended, "This account has been suspended.");

    /// <summary>
    /// A provider display name is untrusted text of unknown length. The local
    /// part of the address is the fallback so the account is never nameless.
    /// </summary>
    private static string DisplayNameFor(ExternalIdentity external, Email email)
    {
        var name = external.DisplayName?.Trim();
        if (string.IsNullOrEmpty(name))
            return email.Value[..email.Value.IndexOf('@')];

        return name.Length > 100 ? name[..100] : name;
    }
}

public sealed record CompleteSsoCommand(string HandoffCode);

/// <summary>
/// Exchanges the handoff code for a token pair.
///
/// Returns <see cref="LoginResult"/> — the same type the password login
/// returns — on purpose. The client has one session-handling path, and a
/// second shape here would be a second place for it to drift. → ADR-0014
/// </summary>
public sealed class CompleteSsoSignIn(
    IHandoffCodeStore handoffCodes,
    IUserRepository users,
    IPermissionResolver permissions,
    ITokenService tokens)
{
    private static readonly Error Invalid = Error.Unauthorized(
        ErrorCodes.SsoHandoffInvalid, "This sign-in has expired. Please try again.");

    public async Task<Result<LoginResult>> HandleAsync(CompleteSsoCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.HandoffCode))
            return Invalid;

        var userId = await handoffCodes.ConsumeAsync(command.HandoffCode, ct);
        if (userId is null)
            return Invalid;

        var user = await users.FindByIdAsync(userId.Value, ct);
        if (user is null)
            return Invalid;

        // Re-checked here, not only at the callback. Suspension can land in the
        // sixty seconds between the two, and this is the call that actually
        // issues credentials.
        if (!user.CanAuthenticate)
            return Error.Forbidden(ErrorCodes.AccountSuspended, "This account has been suspended.");

        var granted = await permissions.ResolveAsync(user, ct);
        var pair = await tokens.IssueAsync(user, granted, familyId: null, ct);

        return new LoginResult(pair, user.Id, user.DisplayName);
    }
}

/// <summary>
/// Random values and the PKCE challenge derivation.
///
/// <para>
/// Base64url without padding, because every one of these travels in a URL and
/// <c>+</c>, <c>/</c> and <c>=</c> all mean something else there. RFC 7636
/// requires exactly this encoding for the challenge; using it for the state
/// and nonce too means no value in this flow ever needs escaping.
/// </para>
/// </summary>
internal static class Secrets
{
    public static string UrlSafe(int bytes) =>
        Base64Url(RandomNumberGenerator.GetBytes(bytes));

    public static string S256Challenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
