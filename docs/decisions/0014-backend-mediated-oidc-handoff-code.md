# ADR-0014 — Social sign-in runs through the backend, and hands the client a one-time code

- **Status:** Accepted
- **Date:** 2026-08-21
- **Deciders:** Solution architect · backend engineer
- **Related:** `AU-2`/`AU-3`/`AU-6` in [`../requirements/confirmed.md`](../requirements/confirmed.md) · `T2`, `T3` in [`../security/threat-model.md`](../security/threat-model.md) · [ADR-0013](0013-one-email-one-account-silent-linking.md) · [ADR-0002](0002-client-capacitor-react.md)

## Context

[`../architecture/key-flows.md`](../architecture/key-flows.md) sketched social sign-in as: the client
runs the OIDC authorization-code flow with PKCE, then posts the resulting `code` to
`POST /auth/social` for the backend to exchange. That sketch settled the *protocol* and left the
*division of labour* open. Building it forces the open half.

The constraints:

- **Google's web client is a confidential client.** A web application client type is issued a client
  secret, and Google requires it at the token endpoint. A browser cannot hold one. → [Google OAuth 2.0 for web server applications](https://developers.google.com/identity/protocols/oauth2/web-server)
- **The mobile clients are public clients.** Capacitor apps ship their code to the device; anything embedded in them is readable. PKCE exists for exactly this. → [ADR-0002](0002-client-capacitor-react.md), `T2`
- **The same product needs both**, from one codebase, because Android and iOS are Capacitor targets of `apps/web` rather than a separate client.
- **Tokens must not travel in a URL.** A redirect carrying an access or refresh token puts it in browser history, in the `Referer` header of the next request, and in any proxy or analytics log on the path. → `T3`
- The existing password flow already returns a bearer pair from `POST /api/v1/auth/login`, and the whole client is built on that shape.

## Options considered

| Option | For | Against |
|---|---|---|
| **A** · Client runs the flow, posts `code` to the backend | Matches the original sketch; one round trip | The browser must know the client id and redirect URI, and the backend must trust a `code` it did not originate. Redirect-URI and `state` validation move to the least trustworthy participant → `T2` |
| **B** · Backend originates the request and receives the callback; hands the client a one-time handoff code to exchange for the token pair | Client secret, PKCE verifier, `state` and `nonce` all stay server-side. Identical shape for web and Capacitor. No token in any URL | Two client round trips instead of one. The handoff code is a new short-lived credential that must be stored and expired |
| **C** · Google Identity Services button, client posts the ID token directly | No redirect at all; very little code | Google-specific — the button, the SDK and the response shape do not generalise. Directly contradicts `AU-6`. Nothing equivalent exists for Facebook |
| **D** · Full BFF: backend sets an `HttpOnly` session cookie, no bearer token on web | Strongest against `T3` on web; JavaScript cannot read the session | Splits the client in two — cookies on web, bearer on mobile — and every existing call site assumes bearer. A larger change than the feature warrants today |

## Decision

**Option B.** Four endpoints, all rate-limited under the existing `auth` policy:

| Endpoint | Does |
|---|---|
| `GET /api/v1/auth/sso/providers` | Lists the providers that are actually configured, so the UI enables real buttons rather than hard-coding three |
| `POST /api/v1/auth/sso/{provider}/start` | Generates `state`, PKCE verifier and `nonce`, stores them server-side against a TTL, returns the authorization URL |
| `GET /api/v1/auth/sso/{provider}/callback` | Receives the provider redirect, exchanges the code, validates the ID token, applies [ADR-0013](0013-one-email-one-account-silent-linking.md), redirects to the client with a **handoff code** |
| `POST /api/v1/auth/sso/complete` | Exchanges the handoff code for the same `SessionResponse` the password login returns |

Rules that are not negotiable within this decision:

- **PKCE `S256` is used even though the backend holds the secret.** It costs nothing and it is the difference between the mobile client being a second-class citizen and being the same flow.
- **`state` and the handoff code are single-use, stored hashed, and expire** — ten minutes and sixty seconds respectively. A `state` that can be replayed is not a `state`.
- **`nonce` is bound into the ID token and checked.** Without it, an ID token minted for another application of the same issuer can be substituted → `T2`.
- **Redirect URIs come from configuration, never from the request.** An open redirect here is an account-takeover primitive, not a nuisance.
- **The client never sees a provider `code`, a client id, or a client secret.**
- **No `code`, `state`, ID token, access token or refresh token is ever logged**, at any level, including on failure.

## Consequences

### Positive
- `T2`'s three named attacks — code interception, redirect-URI manipulation, token substitution — are each closed by a mechanism the client cannot weaken, because the client does not participate in them.
- Web and mobile share one server flow; the mobile difference is a custom-scheme redirect URI in configuration.
- The frontend contract is two calls and a redirect, and the successful result is byte-identical to `POST /api/v1/auth/login` — no second session-handling path in the client.
- Adding a provider is an adapter plus a configuration section (`AU-6`).

### Negative
- Two round trips where option A had one, and a redirect the client cannot intercept if it fails midway. Error reporting has to survive a redirect, which is why failures come back as a query parameter carrying an error *code* rather than a message.
- A new short-lived credential type to store, index, expire and reason about.
- The backend now makes outbound calls to Google during a user-facing request. A provider outage becomes a sign-in outage, with a timeout budget that must be tight enough not to hold the connection.

### Risks accepted
- **The token pair still lands in `localStorage` on web** — this decision keeps `T3`'s web exposure exactly where the password flow already put it. Option D fixes it; that is a separate decision covering both flows, not a rider on this one.
- **The reseller/PDPL question does not apply here, but a related one does.** Sign-in sends the learner's email and IP address to Google, which is a cross-border transfer. It is not exam data and it is user-initiated, but it belongs in the `B-2` filing rather than being assumed out of scope. → [`../security/privacy-vietnam-pdpl.md`](../security/privacy-vietnam-pdpl.md)

## Notes

- The handoff code is deliberately *not* a JWT. It carries no claims, it is a random opaque value indexed by its hash, and it is deleted on first use. Making it a signed token would mean a stolen one is usable until expiry with no way to revoke it — reintroducing on a sixty-second scale exactly the problem refresh-token rotation exists to solve.
- What would make this wrong later: if the web client moves to `HttpOnly` cookie sessions (option D), the handoff step collapses — the callback would simply set the cookie and redirect. Keep the callback and the session issuance separable so that change stays small.
- If a provider is ever added that cannot redirect to a backend URL — a native SDK that only yields an ID token on-device — it does not fit this shape and needs its own decision. Do not bend `/complete` into accepting a provider token.
