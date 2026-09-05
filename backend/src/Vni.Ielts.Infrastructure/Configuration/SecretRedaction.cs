using System.Text.RegularExpressions;

namespace Vni.Ielts.Infrastructure.Configuration;

/// <summary>
/// The one place a configured value is turned into something safe to print.
///
/// <b>FS0.4 — written because the startup gate had begun interpolating URLs
/// into its own messages.</b> `StartupConfiguration` already printed
/// `ObjectStorage:ServiceUrl` verbatim into a warning, and an S3-compatible
/// service URL is allowed to carry userinfo (`https://key:secret@host`). So a
/// deployment that expressed its credentials that way — which several S3
/// clients accept — put them in the process's own startup output, in a line
/// written by the code whose whole purpose is to make misconfiguration safe.
/// Nothing was wrong with the *intent* of that line; the problem is that a
/// value's safety was decided independently at every call site.
///
/// <b>So the decision is made once, here, and every printer calls it.</b> The
/// F4.2 rule this serves is unchanged — a secret does not reach a log, a span
/// or a config dump — and this is the smallest thing that makes obeying it
/// cheaper than not.
///
/// <b>What this is not.</b> It is not a scrubber that hunts for secrets in
/// arbitrary text. A scrubber is a losing position: it has to recognise every
/// shape a secret can take, and it fails silently on the first one it does not
/// know. These are narrow, typed helpers for values whose *class* is already
/// known at the call site — this string is a key, this string is a URL — which
/// is a decision the caller can always make correctly.
/// </summary>
public static partial class SecretRedaction
{
    /// <summary>
    /// How a secret is described when its presence matters and its value never does.
    ///
    /// <b>Presence and length, never a prefix.</b> An operator reading a
    /// startup log needs two things: whether the value arrived, and whether it
    /// arrived intact — a key with a trailing newline from a copy-paste, or a
    /// truncated one from a shell quoting mistake, is the failure this answers
    /// without revealing anything. A "first four characters" fingerprint is the
    /// tempting alternative and it is a real disclosure: several providers
    /// carry account identity in the prefix.
    /// </summary>
    public static string Describe(string? secret) =>
        string.IsNullOrWhiteSpace(secret)
            ? "not set"
            : $"set ({secret.Length} characters)";

    /// <summary>
    /// A URL with everything credential-bearing removed, safe to print.
    ///
    /// <b>Two parts come off, and both are places credentials actually live.</b>
    ///
    /// The <i>userinfo</i> (`https://key:secret@host`) is how an S3-compatible
    /// endpoint, a Mongo connection string and an SMTP URL all express a
    /// credential — three of the settings this product has.
    ///
    /// The <i>query string</i> is where a pre-signed URL puts its signature:
    /// `X-Amz-Signature`, `X-Amz-Credential`. A signed URL is a bearer token
    /// for one object, and object storage is where learner recordings live.
    /// → threat `T19`
    ///
    /// <b>An unparseable value is still redacted rather than echoed.</b> The
    /// commonest reason a URL will not parse is that somebody pasted the wrong
    /// kind of string into the setting — and the wrong string is very often a
    /// connection string with a password in it. So the userinfo pattern is
    /// stripped textually before anything is returned.
    /// </summary>
    public static string Url(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "not set";

        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            var authority = string.IsNullOrEmpty(parsed.UserInfo)
                ? parsed.Authority
                : $"«redacted»@{parsed.Authority}";

            var path = parsed.AbsolutePath == "/" ? string.Empty : parsed.AbsolutePath;
            var query = string.IsNullOrEmpty(parsed.Query) ? string.Empty : "?«redacted»";

            return $"{parsed.Scheme}://{authority}{path}{query}";
        }

        return StripUserInfo(url);
    }

    /// <summary>
    /// A non-secret identifier, shortened.
    ///
    /// An OAuth client id or an S3 access key id is not a secret — it is
    /// useless without its partner — but it names an account, and a log that
    /// travels to a collector is read by people who have no need for that.
    /// This is the same rule the SSO wiring already applied to a Google client
    /// id; it lives here now so there is one of it.
    /// </summary>
    public static string Identifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "not set"
        : value.Length <= 8 ? "…"
        : value[..8] + "…";

    /// <summary>
    /// `scheme://user:pass@host` → `scheme://«redacted»@host`, without parsing.
    ///
    /// Deliberately greedy about what counts as userinfo and deliberately
    /// unwilling to look inside it: this runs on strings that already failed to
    /// parse, so the only safe assumption is that anything before an `@` is a
    /// credential.
    /// </summary>
    private static string StripUserInfo(string value) =>
        UserInfoPattern().Replace(value, "://«redacted»@");

    [GeneratedRegex("://[^/@\\s]*@")]
    private static partial Regex UserInfoPattern();
}
