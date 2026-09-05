using System.Net;

namespace Vni.Ielts.Infrastructure.Security;

/// <summary>
/// What a learner actually receives.
///
/// <b>Written properly on 2026-08-28 rather than left as a placeholder</b> —
/// `[QUYẾT ĐỊNH]` chủ sản phẩm: *"nội dung phải chuẩn setup luôn chứ không đợi
/// web chạy ổn rồi mới setup nội dung sau"*. The reasoning holds up: a
/// verification mail is the very first thing this product ever sends anybody,
/// and a placeholder version of it is the first impression it makes.
///
/// ── Email is not the web, and the differences are load-bearing ────────────
///
/// <b>Tables, not flexbox.</b> Outlook on Windows renders through Microsoft
/// Word's engine, which supports neither flexbox nor grid and ignores most of
/// the box model. A layout that looks correct in every browser can collapse
/// into a single unstyled column there — and Outlook is what a school or a
/// company reads mail in.
///
/// <b>Inline styles, no stylesheet.</b> Gmail strips `&lt;style&gt;` blocks in
/// several of its clients, so anything that matters is set on the element.
///
/// <b>No images, and that is deliberate rather than lazy.</b> Most clients
/// block remote images until the reader allows them, so a logo-led design
/// arrives as a broken-image icon above a wall of text. The mark is drawn with
/// text and a coloured rule instead, which always renders.
///
/// <b>No web fonts.</b> They do not load in email, and the fallback has to
/// carry Vietnamese diacritics — two marks stacked over one glyph. The stack
/// below is what is actually installed on Vietnamese users' machines and
/// phones.
///
/// <b>A plain-text part, always.</b> A client that will not render HTML — or a
/// reader who has turned it off, which is common among exactly the
/// security-conscious people who read a password-reset mail carefully — would
/// otherwise get a blank message.
///
/// <b>Preheader text.</b> The line an inbox shows after the subject. Left
/// unset, clients scrape it from the first visible text, which is usually the
/// greeting — so every message previews identically and the code, which is the
/// one thing the reader wants, is invisible until they open it.
/// </summary>
internal static class EmailTemplates
{
    /// <summary>
    /// Blue `#2A6FB1`, measured from the brand source rather than guessed.
    ///
    /// <b>Orange and green are excluded on purpose.</b> Both are in the brand
    /// and neither reaches 4.5:1 against white, so neither may carry text.
    /// → `assets/brand/README.md`
    /// </summary>
    private const string Blue = "#2A6FB1";

    private const string Ink = "#17161a";
    private const string Muted = "#4a4950";
    private const string Rule = "#e4e4e8";

    /// <summary>
    /// A stack that renders Vietnamese, on the machines Vietnamese learners use.
    ///
    /// <b>`Segoe UI` before `Helvetica`.</b> Helvetica has no Vietnamese
    /// coverage; a client that honours it drops back to a system fallback per
    /// glyph, which is how a sentence ends up in two typefaces. The system
    /// stacks at either end handle iOS and Android, where most of this will be
    /// read.
    /// </summary>
    private const string Font =
        "-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Noto Sans',Arial,sans-serif";

    internal sealed record Rendered(string Subject, string Text, string Html);

    /// <summary>
    /// The verification message: a six-digit code, and nothing to click.
    ///
    /// <b>No link at all, which is the point.</b> The learner is already signed
    /// in and already on their profile page — a link would open in whatever
    /// browser the mail app chooses, which on a phone is usually an in-app
    /// webview with no session. → `IEmailVerificationTokens.IssueCodeAsync`
    /// </summary>
    public static Rendered Verification(string code, int minutes) => new(
        // <b>The code is in the subject.</b> On a phone that is often the whole
        // interaction: the notification arrives, the code is in it, and the
        // learner types it without opening anything.
        $"{code} là mã xác minh email VNI IELTS của bạn",
        $"""
        Mã xác minh của bạn là {code}

        Nhập mã này trong trang hồ sơ để xác minh địa chỉ email. Mã có hiệu lực
        trong {minutes} phút và chỉ dùng được một lần.

        Nếu bạn không yêu cầu, bạn có thể bỏ qua email này — tài khoản của bạn
        không có gì thay đổi.

        VNI Education
        """,
        Layout(
            preheader: $"Mã xác minh: {code}. Có hiệu lực trong {minutes} phút.",
            heading: "Xác minh địa chỉ email",
            body: "Nhập mã dưới đây trong trang hồ sơ của bạn để xác minh địa chỉ email.",
            code: code,
            note:
                $"Mã có hiệu lực trong <strong>{minutes} phút</strong> và chỉ dùng được một lần. "
                + "Nếu bạn không yêu cầu, bạn có thể bỏ qua email này — tài khoản của bạn không "
                + "có gì thay đổi.",
            action: null));

    /// <summary>
    /// The password-reset message: a link, and deliberately still a link.
    ///
    /// <b>The person reading this is signed out — by definition they cannot get
    /// in.</b> A code would have to be entered on a page reachable while signed
    /// out, identifying the account from an address the caller typed, which
    /// makes the endpoint unauthenticated and sprayable. And what it protects
    /// is not "this address is real" but a full account takeover.
    ///
    /// A 256-bit token has no brute-force surface at all. Right tool, different
    /// job. → `SmtpMessageSender`
    /// </summary>
    public static Rendered PasswordReset(string link, int minutes) => new(
        "Đặt lại mật khẩu VNI IELTS",
        $"""
        Bạn đã yêu cầu đặt lại mật khẩu cho tài khoản VNI IELTS.

        Mở liên kết dưới đây để đặt mật khẩu mới. Liên kết có hiệu lực trong
        {minutes} phút và chỉ dùng được một lần.

        {link}

        Nếu bạn không yêu cầu, bạn có thể bỏ qua email này. Mật khẩu hiện tại
        của bạn vẫn giữ nguyên.

        VNI Education
        """,
        Layout(
            preheader: $"Liên kết đặt lại mật khẩu, có hiệu lực trong {minutes} phút.",
            heading: "Đặt lại mật khẩu",
            body: "Bấm nút dưới đây để đặt mật khẩu mới cho tài khoản của bạn.",
            code: null,
            note:
                $"Liên kết có hiệu lực trong <strong>{minutes} phút</strong> và chỉ dùng được một "
                + "lần. Nếu bạn không yêu cầu, bạn có thể bỏ qua email này — mật khẩu hiện tại "
                + "của bạn vẫn giữ nguyên.",
            action: (Label: "Đặt mật khẩu mới", Href: link)));

    /// <summary>
    /// One layout, both messages.
    ///
    /// <b>Nested tables with fixed widths, which is how email has worked for
    /// twenty years and still does.</b> The outer table paints the background
    /// edge to edge; the inner one is capped at 560 px, which is about 75
    /// characters of Vietnamese at 16 px — the width prose is comfortable at,
    /// and narrow enough that a phone shows it without zooming.
    /// </summary>
    private static string Layout(
        string preheader,
        string heading,
        string body,
        string? code,
        string note,
        (string Label, string Href)? action)
    {
        /*
         * <b>The preheader is hidden and still read.</b> An inbox takes its
         * preview from the first text in the body; a client that renders the
         * HTML must not show this line twice. `display:none` alone is ignored
         * by some clients, so it is paired with zero height and an off-screen
         * position — the combination that has held up across all of them.
         */
        var hiddenPreheader =
            "<div style=\"display:none;font-size:1px;color:#ffffff;line-height:1px;"
            + "max-height:0;max-width:0;opacity:0;overflow:hidden\">"
            + Encode(preheader)
            + "</div>";

        var codeBlock = code is null
            ? string.Empty
            : $"""
              <tr><td style="padding:8px 0 24px">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
                  <tr><td align="center"
                      style="background:#f5f8fc;border:1px solid #cfe0f1;border-radius:10px;
                             padding:20px 12px">
                    <div style="font-family:{Font};font-size:34px;font-weight:700;
                                letter-spacing:8px;color:{Blue};line-height:1.1">{Encode(code)}</div>
                  </td></tr>
                </table>
              </td></tr>
              """;

        /*
         * <b>A bulletproof button: a table cell, not a styled anchor.</b>
         * Outlook ignores padding and border-radius on an `<a>`, so a
         * CSS-styled button arrives there as a bare blue link. Painting the
         * cell and letting the anchor fill it is what renders everywhere.
         */
        var actionBlock = action is null
            ? string.Empty
            : $"""
              <tr><td style="padding:8px 0 24px">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0">
                  <tr><td align="center" bgcolor="{Blue}" style="border-radius:8px">
                    <a href="{Encode(action.Value.Href)}"
                       style="display:inline-block;padding:14px 28px;font-family:{Font};
                              font-size:16px;font-weight:600;color:#ffffff;text-decoration:none">
                      {Encode(action.Value.Label)}</a>
                  </td></tr>
                </table>
              </td></tr>
              <tr><td style="padding:0 0 24px;font-family:{Font};font-size:13px;
                             color:{Muted};line-height:1.6;word-break:break-all">
                Nếu nút không hoạt động, hãy sao chép liên kết này vào trình duyệt:<br>
                {Encode(action.Value.Href)}
              </td></tr>
              """;

        return $"""
            <!doctype html>
            <html lang="vi"><head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <meta name="color-scheme" content="light">
              <title>{Encode(heading)}</title>
            </head>
            <body style="margin:0;padding:0;background:#f2f3f5">
              {hiddenPreheader}
              <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%"
                     style="background:#f2f3f5">
                <tr><td align="center" style="padding:32px 16px">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="560"
                         style="width:100%;max-width:560px;background:#ffffff;border-radius:14px">
                    <tr><td style="padding:32px 32px 0">

                      <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%">
                        <tr><td style="padding:0 0 24px;border-bottom:1px solid {Rule}">
                          <span style="font-family:{Font};font-size:15px;font-weight:700;
                                       color:{Blue};letter-spacing:1px">VNI EDUCATION</span>
                        </td></tr>

                        <tr><td style="padding:28px 0 8px;font-family:{Font};font-size:22px;
                                       font-weight:700;color:{Ink};line-height:1.35">
                          {Encode(heading)}
                        </td></tr>

                        <tr><td style="padding:0 0 20px;font-family:{Font};font-size:16px;
                                       color:{Ink};line-height:1.65">
                          {Encode(body)}
                        </td></tr>

                        {codeBlock}
                        {actionBlock}

                        <tr><td style="padding:0 0 28px;font-family:{Font};font-size:14px;
                                       color:{Muted};line-height:1.65">
                          {note}
                        </td></tr>

                        <tr><td style="padding:20px 0 28px;border-top:1px solid {Rule};
                                       font-family:{Font};font-size:13px;color:{Muted};
                                       line-height:1.6">
                          Email này được gửi tự động, vui lòng không trả lời.<br>
                          VNI Education
                        </td></tr>
                      </table>

                    </td></tr>
                  </table>
                </td></tr>
              </table>
            </body></html>
            """;
    }

    /// <summary>
    /// <b>Everything variable is encoded, including the code and the link.</b>
    ///
    /// A six-digit code cannot contain markup and a link is built from
    /// configuration, so neither is attacker-controlled today. Encoding them
    /// anyway costs nothing and removes the question — and the day one of these
    /// carries a display name, an address, or anything a person typed, the
    /// template will already be doing the right thing rather than needing to be
    /// audited for it.
    /// </summary>
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
