/**
 * Configuration read at container start, not baked in at build time.
 *
 * <b>F2.6 — the same static bundle has to work in every environment.</b>
 * Before this, `VITE_API_BASE` was a Vite build-time env var: it gets
 * inlined into the JS bundle at `vite build`, so pointing the learner app at
 * a different API meant rebuilding the image for that environment — an OCI
 * image that is not actually portable, and a rebuild that can drift from
 * the one already tested.
 *
 * <b>A plain script tag, not a fetch.</b> `env-config.js` is served as a
 * static file, loaded by a classic (non-module) `<script>` before the app's
 * module bundle in `index.html`. A classic script blocks parsing and runs to
 * completion before any deferred module script starts, so by the time this
 * file's module-level `getRuntimeConfig()` calls run, `window` already
 * carries whatever the container wrote — no race, no fetch to await before
 * the app can render its first frame.
 *
 * <b>Local dev gets a real file too, not a special case.</b> `public/`
 * files are served as-is by both `vite dev` and a production static server,
 * so a checked-in `public/env-config.js` with empty/placeholder values is
 * the same file the container's entrypoint overwrites in the built image —
 * one code path, not "dev reads one thing, production reads another".
 */

export interface RuntimeConfig {
  /** The API's origin. Falls back to the Vite build-time var, then localhost, for `vite dev`. */
  apiBaseUrl: string;
  /** A label for the running environment — never used for a security decision, only display/telemetry tagging. */
  environment: string;
  /** Where traces/metrics go. Empty means nothing is wired yet — a configured seam, not a default vendor. */
  telemetryEndpoint: string;
  /**
   * Where a locked-out learner is told to ask for help — a Zalo chat, in
   * practice, because that is what VNI's support desk actually answers.
   *
   * <b>`null` when unset, not `''`, and the difference is the whole point.</b>
   * Registration stopped collecting an email on 08/09/2026, so there is no
   * self-service password reset: the forgot-password page has nothing to offer
   * but a human. `null` means "no channel is configured", and the page must
   * then say so plainly rather than render a link to nowhere. The other three
   * fields fall back to a working default; this one cannot — inventing a
   * support address is worse than admitting there is none.
   *
   * <b>Whatever is here is rendered to a signed-out visitor.</b> That makes it
   * the one runtime value an operator can turn into a phishing primitive, so
   * the entrypoint refuses anything that is not an `https://` URL before it
   * ever reaches the bundle. → `apps/web/docker-entrypoint.d/40-vni-runtime-config.sh`
   */
  supportZaloUrl: string | null;
}

declare global {
  interface Window {
    __VNI_RUNTIME_CONFIG__?: Partial<RuntimeConfig>;
  }
}

export function getRuntimeConfig(): RuntimeConfig {
  const injected = typeof window !== 'undefined' ? window.__VNI_RUNTIME_CONFIG__ : undefined;

  return {
    apiBaseUrl: injected?.apiBaseUrl || import.meta.env['VITE_API_BASE'] || 'http://localhost:5099',
    environment: injected?.environment || (import.meta.env.DEV ? 'development' : 'production'),
    telemetryEndpoint: injected?.telemetryEndpoint || '',
    supportZaloUrl: httpsOrNull(injected?.supportZaloUrl),
  };
}

/**
 * <b>The second of two checks, deliberately, not a duplicate of the first.</b>
 *
 * The entrypoint validates the operator's environment variable before it
 * writes `env-config.js`, which covers the container. It does not cover the
 * checked-in `public/env-config.js` a dev server hands out, a hand-edited file
 * in a running container, or any future writer of this global — and this
 * function is the one path every consumer goes through. A `javascript:` or
 * `data:` value that reached an `href` on the signed-out forgot-password page
 * would be script execution on our own origin, so the reader refuses it here
 * as well and the page sees `null`: no channel configured, which it already
 * has to render for the unset case.
 */
function httpsOrNull(value: string | null | undefined): string | null {
  if (!value) return null;

  try {
    return new URL(value).protocol === 'https:' ? value : null;
  } catch {
    // Not a URL at all — a bare `zalo.me/vni`, or a typo.
    return null;
  }
}
