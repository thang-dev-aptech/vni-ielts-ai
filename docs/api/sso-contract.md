# Social sign-in — client contract

**Status of this document:** the endpoints below are implemented and covered by tests. → [ADR-0014](../decisions/0014-backend-mediated-oidc-handoff-code.md) · [ADR-0013](../decisions/0013-one-email-one-account-silent-linking.md) · [`../architecture/key-flows.md`](../architecture/key-flows.md) §1

**Who this is for:** whoever wires the three provider buttons on the sign-in page. Everything the client needs is here; nothing else about OAuth is the client's problem.

---

## The shape, in one paragraph

The client never touches OAuth. It asks the API for a URL, sends the browser there, and the provider redirects **back to the API**, not to the client. The API does the code exchange, decides which account this is, and then redirects the browser to a client route carrying a **one-time code**. The client posts that code back and receives exactly the same session body `POST /api/v1/auth/login` returns.

Two calls and a redirect. No client id, no client secret, no PKCE, no `state`, no token in any URL.

---

## 1 · Which buttons to enable

```http
GET /api/v1/auth/sso/providers
```

```json
{ "providers": [{ "key": "google", "displayName": "Google" }] }
```

**Enable exactly the buttons this returns.** A provider the deployment has no credentials for is absent from the list — today that is Facebook and Microsoft, so only Google appears. Rendering a button for a provider not in this list produces a control that fails on click, which is worse than one that is visibly unavailable.

No authentication. Safe to call on page load.

---

## 2 · Start

```http
POST /api/v1/auth/sso/google/start
Content-Type: application/json

{ "returnTo": "/dashboard" }
```

```json
{ "authorizationUrl": "https://accounts.google.com/o/oauth2/v2/auth?..." }
```

Then `window.location.assign(authorizationUrl)`. Do not open it in an iframe — providers refuse to frame their consent screen.

`returnTo` is optional and must be a **same-site absolute path** (`/dashboard`, `/profile?tab=token`). Anything with a scheme, a host, a backslash or a leading `//` is silently discarded rather than rejected — the sign-in still works, it just lands on the default page.

> **The learner web app currently sends nothing here.** `[QUYẾT ĐỊNH]` chủ sản phẩm 21/08/2026: signing in always lands on the main page, even when the visitor was bounced out of a protected one. The parameter is kept, carried and tested server-side because the decision is a product one and may well come back — but do not add it to a client without checking that decision first.

**No `Idempotency-Key` header.** This endpoint is exempt; sending one is harmless but pointless.

Errors: `404 SSO_PROVIDER_UNKNOWN` if the provider is not configured, `429 RATE_LIMITED`.

---

## 3 · The redirect back

The provider returns the browser to the API, and the API redirects it to:

```
{ClientBaseUrl}/login/sso?code=<handoff>&returnTo=/dashboard
```

or, when something went wrong:

```
{ClientBaseUrl}/login/sso?error=SSO_STATE_INVALID
```

**The client needs a route at `/login/sso`.** It reads the query, and either exchanges the code (step 4) or shows the error. It renders nothing meaningful itself — it is a transit screen; a spinner is the right content.

`ClientCallbackPath` is server configuration. If the client wants a different route, that is a config change, not a code change — ask.

---

## 4 · Complete

```http
POST /api/v1/auth/sso/complete
Content-Type: application/json

{ "handoffCode": "<the code from the query string>" }
```

Returns **exactly** the `SessionResponse` that `POST /api/v1/auth/login` returns:

```json
{
  "accessToken": "…",
  "accessTokenExpiresAt": "2026-08-21T09:15:00+00:00",
  "refreshToken": "…",
  "refreshTokenExpiresAt": "2026-09-20T09:00:00+00:00",
  "userId": "…",
  "displayName": "Học viên"
}
```

Store it through the same `saveSession` path the password login uses. There is deliberately no second session shape.

**The code is single-use and lives sixty seconds.** Strip it from the URL after reading it (`history.replaceState`) so a back-navigation does not retry a spent code, and do not call this twice — the second call returns `401 SSO_HANDOFF_INVALID`.

**Then navigate to `returnTo`, or to `/` when there is none — never to the dashboard.** `[QUYẾT ĐỊNH]` chủ sản phẩm 21/08/2026: signing in stays on the main page. The password form inherits that from `RequireAnonymous`; this route navigates itself, so it needs its own answer. Getting it wrong gives a sign-in that behaves differently depending on which button was pressed — which is exactly what shipped for a few hours on 21/08 and had to be reported by the owner.

---

## Every error the client can see

Branch on `code`, never on `title` or `detail` — those are human-facing and will be translated.

| `code` | Where it arrives | What it means | Suggested Vietnamese message |
|---|---|---|---|
| `SSO_DENIED` | `?error=` | The visitor pressed cancel at the provider | Nothing, or a quiet "Bạn đã hủy đăng nhập" |
| `SSO_STATE_INVALID` | `?error=` | Expired (over 10 minutes), already used, or a stale tab | "Phiên đăng nhập đã hết hạn. Vui lòng thử lại." |
| `SSO_EXCHANGE_FAILED` | `?error=` | The provider refused, or returned something that failed validation | "Không kết nối được với Google. Vui lòng thử lại." |
| `SSO_EMAIL_MISSING` | `?error=` | The provider shared no email address | "Nhà cung cấp không chia sẻ email. Hãy đăng ký bằng email." |
| `IDENTITY_LINK_REQUIRED` | `?error=` | The address already has an account, from a provider that does not verify addresses | "Email này đã có tài khoản. Hãy đăng nhập bằng mật khẩu trước." |
| `ACCOUNT_SUSPENDED` | `?error=` and `403` | The account is suspended | Same wording the password login already uses |
| `SSO_HANDOFF_INVALID` | `401` | The code was spent, expired, or never existed | "Phiên đăng nhập đã hết hạn. Vui lòng thử lại." |
| `SSO_PROVIDER_UNKNOWN` | `404` | Provider not configured | Should be unreachable if step 1 is respected |
| `RATE_LIMITED` | `429` | Carries `Retry-After` | Reuse the existing rate-limit message |

`IDENTITY_LINK_REQUIRED` is **unreachable with Google** — Google verifies addresses, so a matching address links silently and the visitor simply lands signed in. It becomes reachable if Facebook ships. Build the branch; do not expect to see it in testing.

---

## What to expect when the email already has an account

Nothing visible. `M-1` was decided on 2026-08-21: **one email is one account**. Someone who registered with a password and later presses "Sign in with Google" lands in their existing account with their history intact, with no confirmation screen.

One consequence worth knowing when testing: if that existing account had **never verified its email**, linking silently disables its password. That is deliberate — it evicts anyone who registered an address they did not own. → [ADR-0013](../decisions/0013-one-email-one-account-silent-linking.md)

---

## Testing without any Google credentials

Development runs a **stub provider**. `Sso:EnableStubProvider` is already `true` in `appsettings.Development.json`, and it refuses to start outside Development.

It behaves exactly like the real thing — same endpoints, same redirects, same errors — except that the "provider" immediately redirects back and always returns the same account (`stub.learner@example.com`, verified). The entire client flow can be built and tested against it before anyone registers a Google OAuth client.

Run the API (`dotnet run --project backend/src/Vni.Ielts.Api`, port 5099) and press the Google button.
