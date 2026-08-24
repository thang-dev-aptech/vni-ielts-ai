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

const BASE = import.meta.env['VITE_API_BASE'] ?? 'http://localhost:5099';

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

export class ApiError extends Error {
  constructor(readonly problem: ApiProblem) {
    super(problem.detail);
    this.name = 'ApiError';
  }
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

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, accessToken, signal, idempotencyKey } = options;

  const headers: Record<string, string> = { Accept: 'application/json' };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (accessToken) headers['Authorization'] = `Bearer ${accessToken}`;
  if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey;

  const response = await fetch(`${BASE}${path}`, {
    method,
    headers,
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
    ...(signal ? { signal } : {}),
  });

  reconcileClock(response);

  if (response.status === 204) return undefined as T;

  const text = await response.text();
  const payload: unknown = text ? JSON.parse(text) : null;

  if (!response.ok) {
    const problem = (payload ?? {}) as Partial<ApiProblem>;

    // Retry-After is exposed through CORS specifically so a throttled client
    // can wait the right amount instead of guessing or hammering.
    const retryAfter = response.headers.get('Retry-After');
    throw new ApiError({
      title: problem.title ?? 'Request failed',
      status: response.status,
      detail: problem.detail ?? `HTTP ${response.status}`,
      code: problem.code ?? 'UNKNOWN',
      ...(problem.traceId !== undefined ? { traceId: problem.traceId } : {}),
      ...(problem.errors !== undefined ? { errors: problem.errors } : {}),
      ...(retryAfter !== null ? { retryAfterSeconds: Number(retryAfter) } : {}),
    });
  }

  return payload as T;
}

/** The API's origin, for the few calls that cannot go through `request` — uploads and media. */
export function apiBase(): string {
  return BASE;
}
