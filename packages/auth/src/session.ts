import type { Schemas } from '@vni/api-client';
import { request } from './http.js';

/**
 * The session core: what a token is, how one is obtained, and where it lives.
 *
 * Everything here is used by both apps. Anything used by only one of them —
 * device management, password reset, SSO — stays in that app.
 */

/**
 * A token pair and who it belongs to.
 *
 * <b>Still hand-written, and that is a debt rather than a choice.</b> `Me`
 * below is an alias onto the generated contract; this is not, because
 * `/api/v1/auth/login`, `/refresh` and `/register` still declare no response
 * schema in `contracts/openapi` — `W7` covered `/me` and dictation only. So
 * the shape here is described from memory, exactly as `Me` used to be, and
 * nothing makes it agree with the server. Bind it the day those routes declare
 * what they return.
 */
export interface Session {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  userId: string;
  displayName: string;
}

/**
 * The signed-in account, as `GET /api/v1/me` returns it.
 *
 * <b>Generated since 18/09/2026. It was an interface maintained by hand, and
 * it had already drifted.</b> When `W7` declared the contract (`becebde`) this
 * copy disagreed with it in two places — `mustChangePassword` was optional
 * here and required there, and so was `phone`. Neither drift was visible to a
 * single test, because a mocked `/me` returns whatever the fixture says: the
 * client agreed with itself all the way down. `session.contract.test-d.ts` is
 * what catches that class of bug now, and its gate is `tsc`, not `vitest`.
 *
 * Three things the shape states that are worth stating in prose:
 *
 * <b>`email` is nullable, and that is the normal case.</b> Registration
 * collects a phone number; an address is something an account may later add,
 * so most rows have none. There is deliberately no `emailVerified` beside it
 * any more — the verification flow was removed with the email-first sign-up it
 * belonged to, and a boolean nothing sets is worse than an absent field: a
 * screen would keep rendering "chưa xác minh" against a value the server
 * stopped deciding. → ADR-0018
 *
 * <b>`permissions` is what this account may do.</b> The CMS filters its
 * navigation on these, and the learner app ignores them — a learner's ability
 * to sit an exam is governed by entitlement and session ownership, not by an
 * admin permission.
 *
 * <b>`mustChangePassword` is required, and the reason it used to be optional
 * has expired.</b> The note here said the field was optional *because `/me`
 * declared no schema in `contracts/openapi`*, so a deployment running an older
 * API would send nothing and an absent field had to read as "no, carry on".
 * `/me` declares `MeResponse` as of 18/09/2026 and the field is required in
 * it, so there is no longer a shape for the permissive reading to rescue: an
 * API that omits it is failing its own contract, not describing an account.
 * What the flag means is unchanged — the current password was set by an
 * operator rather than by its owner, and has to be replaced before the account
 * is usable.
 */
export type Me = Schemas['MeResponse'];

/**
 * <b>`identifier`, not `email` — the server decides which one it is.</b>
 * An account is reached by its phone number or by an address, and the client
 * is the wrong place to guess: a string is only ambiguous until it is looked
 * up, and a client that sniffed for an `@` would have to be changed again the
 * next time the rule moves. So the whole typed value goes as one field, and
 * the caller passes what the person actually typed.
 */
export const login = (identifier: string, password: string) =>
  request<Session>('/api/v1/auth/login', { method: 'POST', body: { identifier, password } });

export const refresh = (refreshToken: string) =>
  request<Session>('/api/v1/auth/refresh', { method: 'POST', body: { refreshToken } });

export const me = (accessToken: string) => request<Me>('/api/v1/me', { accessToken });

/**
 * Ends this session on the server, not only in this browser.
 *
 * <b>Signing out was a local act until 2026-08-28, and it should never have
 * been.</b> The client cleared storage and that was all of it: the refresh
 * token family stayed live for its full thirty days. So signing out on a shared
 * machine, a library computer, or a phone being handed on left a working
 * credential behind — recoverable from a browser profile backup, or from
 * anything that had already copied the value.
 *
 * <b>The caller does not wait on it and must not fail on it.</b> A learner
 * pressing "sign out" has to end up signed out whatever the network does; the
 * local clear is what makes that true, and this is what makes it true on the
 * server as well when it can be reached. A failure here leaves a family the
 * device-list screen can still revoke by hand.
 */
export const logout = (accessToken: string) =>
  request<void>('/api/v1/auth/logout', { method: 'POST', accessToken });

/**
 * One key, both apps.
 *
 * <b>Deliberate: one person, one identity, one session.</b> An admin is a user
 * account with permissions on it, not a separate login — so signing in on the
 * learner app and opening the CMS in the same browser should not ask again.
 * Two keys would also mean two refresh cycles rotating the same token family,
 * and the second one to arrive gets treated as a replay.
 */
const STORAGE_KEY = 'vni.session';

/*
 * <b>Reading `localStorage` can throw, before any value comes back.</b>
 *
 * Safari in private browsing, a WebView with site data disabled, and a browser
 * locked down by policy all raise on the *accessor*. `JSON.parse` was already
 * guarded here; the three `localStorage` calls around it were not, and this
 * module runs on the first render of both applications — so on those browsers
 * the app did not fail to restore a session, it failed to start.
 *
 * Unlike the preference helpers in `apps/web/src/lib/storage.ts`, a failure to
 * *write* here does matter: the session will not survive a reload. There is
 * nothing this layer can do about that, so it degrades to a session that lasts
 * as long as the tab rather than taking the page down — and `saveSession`
 * reports whether it landed, so a caller that wants to say so can.
 */
export function loadSession(): Session | null {
  let raw: string | null = null;
  try {
    raw = localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
  if (raw === null) return null;

  try {
    return JSON.parse(raw) as Session;
  } catch {
    // A corrupt entry is not recoverable and blocks every sign-in attempt
    // until it is gone.
    clearSession();
    return null;
  }
}

/** @returns whether the session was actually persisted. */
export function saveSession(session: Session): boolean {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    return true;
  } catch {
    return false;
  }
}

export function clearSession(): void {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Nothing was stored, so nothing needs removing.
  }
}
