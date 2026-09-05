using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using Vni.Ielts.Application.Identity;
using Vni.Ielts.Domain.Identity;

namespace Vni.Ielts.Infrastructure.Security;

/// <summary>
/// How a verification or reset link actually reaches a mailbox.
///
/// <b>SMTP, and that is a decision about coupling rather than about a vendor.</b>
/// Every provider worth using — SES, SendGrid, Postmark, Mailgun, a Vietnamese
/// host — speaks it, so choosing SMTP is choosing <i>not</i> to choose: the
/// provider becomes a host name and a credential in configuration, and swapping
/// one is a deployment change rather than a code change.
///
/// That matters more here than it usually would. `B-2` is unresolved and an
/// email address sent to a foreign provider is a cross-border transfer with the
/// same obligation as everything else; if the answer comes back "keep it in
/// Vietnam", a provider swap must not be a rewrite. → `docs/security/privacy-vietnam-pdpl.md`
///
/// <b>A vendor SDK would have bought one thing this does not have</b> — delivery
/// webhooks, bounce handling, suppression lists — and none of that is needed to
/// send a link to somebody who just typed their own address. When it is needed
/// it is a second adapter behind the same port, not a change to this one.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Email";

    /// <summary>Empty means no sender is configured, which is a supported state in Development.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 587 for STARTTLS, 465 for implicit TLS.
    ///
    /// <b>587 by default, not 25.</b> Port 25 is unauthenticated relay between
    /// servers, is blocked outbound by most hosts, and carries no expectation
    /// of encryption. A default that silently sends a password-reset link in
    /// the clear is the wrong default.
    /// </summary>
    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// From environment configuration only. Never committed — the `.gitignore`
    /// and a PreToolUse hook both block `.env*`, and CI scans for
    /// credential-shaped strings. → CLAUDE.md rule 6
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>The address a learner sees, and the one a reply would go to.</summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "VNI Education";

    /// <summary>
    /// Where the links point — the learner web app, not this API.
    ///
    /// <b>Configuration rather than derived from the request.</b> Building a
    /// link from an incoming `Host` header would let anyone who can reach this
    /// process choose the domain a password-reset link points at, which is a
    /// phishing primitive that starts on our own mail.
    /// </summary>
    public string ClientBaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(FromAddress)
        && !string.IsNullOrWhiteSpace(ClientBaseUrl);
}

internal sealed class SmtpMessageSender(
    SmtpOptions options, ILogger<SmtpMessageSender> logger) : IVerificationMessageSender
{
    /// <summary>
    /// <b>The `token` here is a six-digit code, not a link token.</b>
    ///
    /// `[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026. The learner is already signed
    /// in and already on their profile page when they verify, so a link would
    /// open in whatever browser the mail app chose — usually an in-app webview
    /// with no session. → `IEmailVerificationTokens.IssueCodeAsync`
    /// </summary>
    public Task<MessageDelivery> SendAsync(Email address, string token, CancellationToken ct) =>
        SendOneAsync(address, EmailTemplates.Verification(token, minutes: 10), ct);

    /// <summary>
    /// Still a link, and deliberately.
    ///
    /// The person reading this is signed out — by definition they cannot get
    /// in — and what the token protects is a full account takeover rather than
    /// "this address is real". A 256-bit token has no brute-force surface;
    /// six digits redeemed from an unauthenticated page would.
    /// </summary>
    public Task<MessageDelivery> SendPasswordResetAsync(
        Email address, string token, CancellationToken ct) =>
        SendOneAsync(
            address,
            EmailTemplates.PasswordReset(
                $"{options.ClientBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(token)}",
                minutes: 60),
            ct);

    private async Task<MessageDelivery> SendOneAsync(
        Email address, EmailTemplates.Rendered content, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        message.To.Add(MailboxAddress.Parse(address.Value));
        message.Subject = content.Subject;

        /*
         * <b>Both parts, and the plain-text one is not a courtesy.</b> A mail
         * client that will not render HTML — or a person who has turned it off,
         * which is common among exactly the security-conscious people who read
         * a password-reset mail carefully — would otherwise receive a blank
         * message.
         */
        message.Body = new BodyBuilder
        {
            TextBody = content.Text,
            HtmlBody = content.Html,
        }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();

            /*
             * <b>`StartTlsWhenAvailable` is not used, deliberately.</b> It
             * downgrades to plaintext when a server does not offer TLS, and a
             * server that does not offer TLS is exactly the case where a
             * password-reset link must not be sent. Implicit TLS on 465,
             * required STARTTLS everywhere else.
             */
            await client.ConnectAsync(
                options.Host,
                options.Port,
                options.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
                ct);

            if (!string.IsNullOrWhiteSpace(options.Username))
                await client.AuthenticateAsync(options.Username, options.Password, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            return MessageDelivery.Sent;
        }
        catch (Exception e)
        {
            /*
             * <b>Reported as not sent rather than thrown, and the caller
             * already knows what to do with that.</b>
             *
             * The whole reason this port returns a <see cref="MessageDelivery"/>
             * is so a screen can say "we could not send it" instead of guessing
             * from a successful-looking response. Throwing would fail the
             * registration that produced it — and a learner whose account was
             * created successfully must not be told registration failed because
             * a mail server was slow.
             *
             * The address is not logged. A verification mail is sent to an
             * address somebody just typed, and a log line naming it is a log
             * line that has to be treated as personal data for as long as it is
             * kept.
             */
            logger.LogError(e, "Could not send {Subject} through {Host}.", content.Subject, options.Host);
            return MessageDelivery.NotSent;
        }
    }
}
