/**
 * W3C Trace Context, generated in the browser.
 *
 * <b>F4.2 — so a trace starts where the learner actually is.</b> Without a
 * `traceparent` on the way in, the earliest thing any trace knows about is
 * the API receiving a request; "the app felt slow" and "the server was fast"
 * are then two unrelated observations. With one, the request the learner made
 * and everything it caused — including a marking job picked up by the worker
 * minutes later — are one trace.
 *
 * <b>Not an OpenTelemetry browser SDK.</b> The whole requirement is a header
 * in a documented format, and the SDK would add a bundle, a second exporter
 * and a second place to configure an endpoint — for a page whose own timings
 * nobody has asked to collect yet. If browser-side telemetry is ever wanted,
 * this format is exactly what that SDK emits, so nothing here has to change.
 *
 * Format — https://www.w3.org/TR/trace-context/#traceparent-header
 *   version "00" · trace-id 32 hex · parent-id 16 hex · flags "01" (sampled)
 */

/** Hex string of `bytes` random bytes, using the platform CSPRNG. */
function randomHex(bytes: number): string {
  const buffer = new Uint8Array(bytes);

  // `crypto` is present in every browser this app supports and in jsdom, but
  // guarding keeps the transport importable in a bare Node context (an SSR
  // probe, a unit test) rather than throwing at module load.
  if (typeof globalThis.crypto?.getRandomValues === 'function') {
    globalThis.crypto.getRandomValues(buffer);
  } else {
    for (let i = 0; i < bytes; i++) buffer[i] = Math.floor(Math.random() * 256);
  }

  return Array.from(buffer, (b) => b.toString(16).padStart(2, '0')).join('');
}

/**
 * A fresh `traceparent` for one request.
 *
 * <b>One per request, deliberately — not one per session.</b> A trace id
 * reused across every call a learner makes would collapse an hour of
 * unrelated work into a single unreadable trace, and it would become a
 * durable identifier for that learner across requests, which is exactly the
 * kind of accidental tracking F4.2 exists to avoid.
 */
export function newTraceParent(): string {
  return `00-${randomHex(16)}-${randomHex(8)}-01`;
}

/** Matches a well-formed traceparent, for tests and for validation. */
export const TRACEPARENT_PATTERN = /^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$/;
