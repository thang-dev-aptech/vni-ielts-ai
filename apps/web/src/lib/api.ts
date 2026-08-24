/**
 * The HTTP transport.
 *
 * <b>Moved to `@vni/auth` and re-exported from here.</b> The CMS talks to the
 * same API and needs the same two guarantees — a typed error a client can
 * branch on, and the server-clock reconciliation the exam timer depends on.
 * A second copy of either would drift, and the drift would surface as a
 * deadline that is right on one surface and wrong on the other.
 *
 * This file stays as the import path so nothing in this app had to change.
 */
export {
  ApiError,
  apiBase,
  clockOffsetMs,
  request,
  serverNow,
  type ApiProblem,
  type RequestOptions,
} from '@vni/auth';
