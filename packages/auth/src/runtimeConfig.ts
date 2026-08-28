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
  };
}
