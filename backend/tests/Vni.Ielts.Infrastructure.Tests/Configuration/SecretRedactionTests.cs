using Vni.Ielts.Infrastructure.Configuration;

namespace Vni.Ielts.Infrastructure.Tests.Configuration;

/// <summary>
/// FS0.4 — the redactor itself, held to the rule its callers depend on.
///
/// <b>These are unit tests of a primitive, and the leak proof they support
/// lives in `SecretContractTests`.</b> The distinction matters: proving that
/// `Url` strips a password proves nothing about whether anything calls it. Both
/// halves are needed, and only the second one catches a regression where
/// somebody prints a raw value beside a redacted one.
/// </summary>
public sealed class SecretRedactionTests
{
    private const string Secret = "FAKE-NOT-A-REAL-SECRET-value-for-tests-only";

    [Fact]
    public void A_secret_is_described_by_presence_and_length_and_never_by_content()
    {
        var described = SecretRedaction.Describe(Secret);

        Assert.DoesNotContain(Secret, described, StringComparison.Ordinal);

        // Length is deliberately disclosed: a key with a trailing newline from
        // a copy-paste, or one truncated by shell quoting, is the failure this
        // line is read for, and neither is visible from "set".
        Assert.Contains(Secret.Length.ToString(), described, StringComparison.Ordinal);

        // And no prefix fingerprint — several providers carry account identity
        // in the first characters of a key.
        Assert.DoesNotContain(Secret[..6], described, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_secret_says_so_rather_than_printing_an_empty_value(string? absent)
    {
        Assert.Equal("not set", SecretRedaction.Describe(absent));
    }

    /// <summary>
    /// <b>The exact shape that made this file necessary.</b> An S3-compatible
    /// service URL may carry userinfo, and the startup gate used to interpolate
    /// that setting whole into a warning.
    /// </summary>
    [Fact]
    public void A_url_loses_its_userinfo()
    {
        var redacted = SecretRedaction.Url(
            "https://access-id:FAKE-NOT-A-REAL-SECRET-pw@storage.example.com/bucket");

        Assert.DoesNotContain("FAKE-NOT-A-REAL-SECRET-pw", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("access-id", redacted, StringComparison.Ordinal);

        // The half an operator needs is kept: which host was it talking to.
        Assert.Contains("storage.example.com", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>A pre-signed URL is a bearer token for one object.</b> Object storage
    /// is where learner recordings live, so a signature in a log is an IDOR
    /// that needs no account at all. → threat `T19`
    /// </summary>
    [Fact]
    public void A_url_loses_its_query_string()
    {
        var redacted = SecretRedaction.Url(
            "https://storage.example.com/vni-audio-90d/rec.m4a"
            + "?X-Amz-Credential=FAKE-NOT-A-REAL-CRED&X-Amz-Signature=FAKE-NOT-A-REAL-SIGNATURE");

        Assert.DoesNotContain("FAKE-NOT-A-REAL-SIGNATURE", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE-NOT-A-REAL-CRED", redacted, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The case that matters most, and the one a parser-based redactor gets
    /// wrong.</b> The commonest reason a URL setting will not parse is that
    /// somebody pasted the wrong kind of string into it — and the wrong string
    /// is very often a connection string with a password. Falling back to
    /// "echo it, it isn't a URL" is exactly backwards.
    /// </summary>
    [Fact]
    public void A_value_that_is_not_a_url_is_still_stripped_rather_than_echoed()
    {
        var redacted = SecretRedaction.Url(
            "mongodb://admin:FAKE-NOT-A-REAL-PASSWORD@db.internal:27017,db2.internal:27017/?replicaSet=rs0");

        Assert.DoesNotContain("FAKE-NOT-A-REAL-PASSWORD", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void An_identifier_is_shortened_and_a_short_one_disappears_entirely()
    {
        Assert.Equal("12345678…", SecretRedaction.Identifier("1234567890abcdef"));
        Assert.Equal("…", SecretRedaction.Identifier("short"));
        Assert.Equal("not set", SecretRedaction.Identifier(null));
    }

    /// <summary>
    /// FS9.1 / FS8.6 — a Speaking presigned PUT must not survive into logs or
    /// audit detail. The redactor is what Infrastructure call sites use when
    /// an operator-facing message has to mention the URL at all.
    /// </summary>
    [Fact]
    public void A_speaking_presigned_put_url_loses_its_signature_and_credential()
    {
        const string signature = "FAKE-NOT-A-REAL-SPEAKING-SIGNATURE";
        const string credential = "FAKE-NOT-A-REAL-SPEAKING-CRED/20260829/auto/s3/aws4_request";

        var redacted = SecretRedaction.Url(
            "https://minio.example.com/vni-speaking/recordings/abcdef0123456789abcdef0123456789"
            + $"?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential={credential}"
            + $"&X-Amz-Signature={signature}");

        Assert.DoesNotContain(signature, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain(credential, redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Amz-", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recordings/abcdef0123456789abcdef0123456789", redacted, StringComparison.Ordinal);
        Assert.EndsWith("?«redacted»", redacted, StringComparison.Ordinal);
    }
}
