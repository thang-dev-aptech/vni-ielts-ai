/**
 * The HTTP transport, shared by the learner app and the CMS.
 *
 * <b>Extracted rather than copied.</b> Two apps talk to one API, and the two
 * things this file owns are the two that must never disagree between them:
 * how a server error becomes a typed failure a client can branch on, and how
 * the client's clock is corrected against the server's. A second copy of
 * either would drift, and the drift would only show up as an exam deadline
 * being wrong on one surface.
 *
 * <b>Not `packages/api-client`.</b> That one is generated from
 * `contracts/openapi` and a hand edit there is a build failure, not a patch.
 * This is the layer underneath it, and it survives the generator landing.
 */

import { getRuntimeConfig } from './runtimeConfig.js';
import { newTraceParent } from './trace.js';

const BASE = getRuntimeConfig().apiBaseUrl;

/** The shape every error response takes. Clients branch on `code`, never on prose. */
export interface ApiProblem {
  title: string;
  status: number;
  detail: string;
  code: string;
  traceId?: string;
  errors?: Array<{ path: string; code: string; message: string }>;
  /** Present on a 429. Seconds to wait before retrying. */
  retryAfterSeconds?: number;
}

/**
 * The code carried by an `ApiError` the server never sent.
 *
 * Exported so a caller can tell "the API said no" from "the API was not
 * reached", which need opposite advice: fix your input versus try again.
 */
export const TRANSPORT_ERROR = 'TRANSPORT_ERROR';

export class ApiError extends Error {
  constructor(readonly problem: ApiProblem) {
    super(problem.detail);
    this.name = 'ApiError';
  }
}

/**
 * Did this failure mean "the API refused" or "the API was not reached"?
 *
 * The two need opposite advice — correct your input versus wait and retry —
 * and every screen that shows an error has to make the distinction. Getting it
 * wrong is not cosmetic: it tells someone a valid link is dead, or that a
 * correct password is wrong, because a proxy blinked.
 *
 * Three shapes mean unreachable, and each arrives differently:
 *   • `fetch` itself rejects — DNS, offline, CORS preflight — a `TypeError`
 *   • the body was not JSON — a proxy error page or interstitial
 *   • a 5xx — the request arrived, and the server could not answer it
 *
 * 4xx is deliberately excluded. Those are answers, and the answer is no.
 */
export function isUnreachable(error: unknown): boolean {
  if (error instanceof TypeError) return true;
  if (!(error instanceof ApiError)) return false;
  return error.problem.code === TRANSPORT_ERROR || error.problem.status >= 500;
}

/**
 * Offset between this device's clock and the server's, in milliseconds.
 *
 * The exam timer is server-authoritative (ADR-0007). The client renders
 * remaining time from its own clock, which drifts, so every response carries
 * `X-Server-Time` and every response corrects the offset. This is why the
 * header is on *every* response rather than a dedicated time endpoint — the
 * correction is free, on requests the app was making anyway.
 */
let serverOffsetMs = 0;

/** The server's current time as best this client can tell. */
export function serverNow(): Date {
  return new Date(Date.now() + serverOffsetMs);
}

export function clockOffsetMs(): number {
  return serverOffsetMs;
}

function reconcileClock(response: Response): void {
  const header = response.headers.get('X-Server-Time');
  if (!header) return;
  const server = Date.parse(header);
  if (!Number.isNaN(server)) {
    // Correct towards the server, never the reverse.
    serverOffsetMs = server - Date.now();
  }
}

export interface RequestOptions {
  method?: string;
  body?: unknown;
  accessToken?: string | undefined;
  signal?: AbortSignal | undefined;
  /**
   * Makes a retry safe.
   *
   * The server stores its response against this key for 24 hours, so a second
   * attempt with the same key returns the first result rather than performing
   * the operation again. Generate it ONCE per logical operation — a key
   * regenerated on every press defeats the entire mechanism.
   */
  idempotencyKey?: string | undefined;
}

/**
 * How the transport gets a fresh bearer token after a 401.
 *
 * <b>A hook rather than a direct call, because this package must not know what
 * a session is.</b> `AuthProvider` owns the live session, the storage and the
 * sign-out rule; it registers a function here at start-up. Nothing else may.
 *
 * Returning `null` means "cannot be renewed" — the caller then sees the
 * original 401, which is correct: a request that failed for want of a
 * credential nobody can supply has failed.
 */
export type TokenRenewer = () => Promise<string | null>;

let renewToken: TokenRenewer | null = null;

/** Registered once by the app's auth provider. */
export function setTokenRenewer(renewer: TokenRenewer | null): void {
  renewToken = renewer;
}

/*
 * <b>One renewal at a time, shared by everyone waiting — and the guard now
 * lives one level up.</b>
 *
 * A page in the middle of an exam has several requests in flight — an autosave,
 * an audio fetch, a poll. If the token expired they all get a 401 at once, and
 * each one renewing independently would present the same single-use refresh
 * token several times. The server reads the second presentation as a replay and
 * revokes the whole family, so the naive fix does not merely waste calls: it
 * ends the session it was written to save.
 *
 * <b>This guard was per transport, and that is one tab and one entry point too
 * few.</b> The provider's proactive timer and its restore-on-mount did not pass
 * through here at all, so they had a separate boolean; and two tabs are two
 * heaps with two guards over one stored token. Both holes are closed in
 * `coordinator.ts`, which owns a single promise per tab <i>and</i> a lock
 * across tabs, and which re-reads storage inside that lock so a tab that lost
 * the race adopts rather than rotates.
 *
 * What stays here is the join: the renewer registered by the app is the
 * coordinator, and this keeps one in-flight call per transport so a burst of
 * 401s becomes one await rather than a burst of calls into it.
 */
let renewal: Promise<string | null> | null = null;

function renewOnce(): Promise<string | null> {
  if (renewToken === null) return Promise.resolve(null);

  renewal ??= renewToken().finally(() => {
    renewal = null;
  });

  return renewal;
}

/**
 * An authenticated `fetch` for the responses this module cannot parse.
 *
 * <b>`request()` is not enough on its own, and assuming it was is how the
 * renewal shipped with a hole in it.</b> Four call sites need a bearer token
 * and cannot use `request()`: Listening audio, exam images, dictation audio —
 * all of which want a `Blob` — and the Speaking upload, which sends multipart
 * that `request()` would `JSON.stringify`. Every one of them was calling
 * `fetch` directly with a token captured from context, so every one of them
 * kept dying fifteen minutes into a paper after the JSON path stopped.
 *
 * Three of the four are the exam itself: the audio a Listening section is
 * made of, the chart a Writing task refers to, and the recording that *is* the
 * Speaking answer.
 *
 * Same renewal as `request()`, deliberately — <b>the same one, not another
 * copy</b>. A second single-flight guard would be a second lock around the same
 * single-use refresh token, and two locks are none: an autosave and an audio
 * fetch failing together would present it twice and the server would revoke the
 * family.
 *
 * Returns the `Response` untouched. Callers here care about blobs, status codes
 * and their own error vocabulary, and a helper that parsed for them would be
 * `request()` with a worse name.
 */
export async function authedFetch(
  url: string,
  accessToken: string,
  init: RequestInit = {},
): Promise<Response> {
  const send = (token: string) =>
    fetch(url, {
      ...init,
      headers: { ...(init.headers ?? {}), Authorization: `Bearer ${token}` },
    });

  const response = await send(accessToken);

  reconcileClock(response);

  if (response.status !== 401) return response;

  const renewed = await renewOnce();
  if (renewed === null || renewed === accessToken) return response;

  const retried = await send(renewed);
  reconcileClock(retried);
  return retried;
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  try {
    return await send<T>(path, options);
  } catch (error) {
    /*
     * <b>A 401 gets exactly one more chance, and only with a new token.</b>
     *
     * An access token lasts fifteen minutes; a Reading section lasts sixty.
     * `AuthProvider` refreshes ahead of expiry so this path should be rare —
     * but a laptop that slept through the deadline, or a clock that drifted,
     * arrives here, and the server validates with zero skew. Without this the
     * failure surfaced as an autosave that stopped working halfway through a
     * paper, with a red chip and nothing to press.
     *
     * One retry, not a loop: if the renewed token is also refused then the
     * problem is not staleness and asking again would spin. And no retry
     * without a *different* token, or this would replay the same rejected
     * request against the same rejecting server.
     */
    if (!(error instanceof ApiError) || error.problem.status !== 401) throw error;
    if (!options.accessToken) throw error;

    const renewed = await renewOnce();
    if (renewed === null || renewed === options.accessToken) throw error;

    return await send<T>(path, { ...options, accessToken: renewed });
  }
}

async function send<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, accessToken, signal, idempotencyKey } = options;

  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (accessToken) headers['Authorization'] = `Bearer ${accessToken}`;
  if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;

  // F4.2 — starts the trace at the learner rather than at the API. The server
  // continues it, and the marking job it enqueues carries it further still.
  headers['traceparent'] = newTraceParent();

  const response = await fetch(`${BASE}${path}`, {
    method,
    headers,
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
    ...(signal ? { signal } : {}),
  });

  reconcileClock(response);

  if (response.status === 204) return undefined as T;

  const text = await response.text();

  /*
   * A body that is not JSON is a transport failure, not an application error.
   *
   * Unguarded, `JSON.parse` threw a raw `SyntaxError` — which is not an
   * `ApiError`, so every `catch (e) { if (e instanceof ApiError) … }` in both
   * apps fell through to its generic branch. The visible cost was worse than
   * a bad message: a gateway error page during e-mail verification told the
   * reader their link "is no longer valid" and sent them to request another
   * one. Wrong, actionable, and it destroys trust in a link that was fine.
   *
   * Anything can sit between this client and the API — a proxy 502, an nginx
   * error page, a captive-portal interstitial, a truncated body. All of them
   * arrive here as HTML, and all of them mean the same thing to a caller:
   * the server was not reached, try again.
   */
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text);
    } catch {
      throw new ApiError({
        title: 'Unexpected response',
        status: response.status,
        detail: `Non-JSON response (HTTP ${response.status})`,
        code: TRANSPORT_ERROR,
      });
    }
  }

  if (!response.ok) {
    const problem = (payload ?? {}) as Partial<ApiProblem>;

    // Retry-After is exposed through CORS specifically so a throttled client
    // can wait the right amount instead of guessing or hammering.
    const retryAfter = response.headers.get('Retry-After');
    const retryAfterSeconds =
      retryAfter !== null && Number.isFinite(Number(retryAfter)) ? Number(retryAfter) : null;
    throw new ApiError({
      title: problem.title ?? 'Request failed',
      status: response.status,
      detail: problem.detail ?? `HTTP ${response.status}`,
      code: problem.code ?? 'UNKNOWN',
      ...(problem.traceId !== undefined ? { traceId: problem.traceId } : {}),
      ...(problem.errors !== undefined ? { errors: problem.errors } : {}),
      // `Retry-After` is also allowed to carry an HTTP-date, which `Number`
      // turns into NaN — and a NaN countdown renders as "chờ NaN giây".
      ...(retryAfterSeconds !== null ? { retryAfterSeconds } : {}),
    });
  }

  return payload as T;
}

/** The API's origin, for the few calls that cannot go through `request` — uploads and media. */
export function apiBase(): string {
  return BASE;
}
