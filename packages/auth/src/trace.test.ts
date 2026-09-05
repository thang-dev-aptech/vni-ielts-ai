import { expect, it, vi, afterEach } from 'vitest';
import { newTraceParent, TRACEPARENT_PATTERN } from './trace.js';

/**
 * F4.2 — the browser end of the correlation chain.
 *
 * The value only means anything if it is a well-formed W3C traceparent: the
 * server parses it with a strict parser and silently ignores anything it
 * cannot read, so a malformed header does not fail loudly — it just quietly
 * ends the correlation the header exists to create.
 */

afterEach(() => {
  vi.unstubAllGlobals();
});

it('produces a well-formed W3C traceparent', () => {
  expect(newTraceParent()).toMatch(TRACEPARENT_PATTERN);
});

it('marks the trace as sampled, or the server discards it', () => {
  // The trailing `01` is the sampled flag. `00` means "not sampled", and a
  // backend is free to drop it — which would look exactly like the header
  // never having been sent.
  expect(newTraceParent().endsWith('-01')).toBe(true);
});

it('never repeats a trace id', () => {
  // A reused trace id would collapse an hour of unrelated requests into one
  // unreadable trace, and would become a durable identifier for the learner
  // across those requests.
  const ids = new Set(Array.from({ length: 500 }, () => newTraceParent().split('-')[1]));
  expect(ids.size).toBe(500);
});

it('uses an all-zero trace id for nothing', () => {
  // `00000000000000000000000000000001` is invalid per the spec and is
  // rejected by conformant parsers.
  const traceId = newTraceParent().split('-')[1];
  expect(traceId).not.toBe('0'.repeat(32));
});

it('still produces a valid header when the platform CSPRNG is unavailable', () => {
  // A bare Node context, an SSR probe, an old embedded webview: the fallback
  // must still yield something the server can parse, because a malformed
  // header ends the trace silently.
  vi.stubGlobal('crypto', {});

  expect(newTraceParent()).toMatch(TRACEPARENT_PATTERN);
});
