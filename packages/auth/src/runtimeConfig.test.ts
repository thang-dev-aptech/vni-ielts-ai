import { afterEach, expect, it } from 'vitest';
import { getRuntimeConfig } from './runtimeConfig.js';

/**
 * F2.6 — this is the one seam that decides whether a built image can move
 * between environments without a rebuild. `env-config.js` writes
 * `window.__VNI_RUNTIME_CONFIG__` before the app's module bundle runs; these
 * tests drive that same global directly rather than loading the script, since
 * what is under test is the reader's precedence, not the script tag.
 */

afterEach(() => {
  delete window.__VNI_RUNTIME_CONFIG__;
});

it('falls back to the Vite build-time default when nothing was injected', () => {
  const config = getRuntimeConfig();

  expect(config.apiBaseUrl).toBe('http://localhost:5099');
  expect(config.telemetryEndpoint).toBe('');
  expect(config.supportZaloUrl).toBeNull();
});

it('prefers the injected config over the build-time default entirely', () => {
  window.__VNI_RUNTIME_CONFIG__ = {
    apiBaseUrl: 'https://api.learn.example.com',
    environment: 'staging',
    telemetryEndpoint: 'https://otel.example.com/v1/traces',
    supportZaloUrl: 'https://zalo.me/vni-support',
  };

  const config = getRuntimeConfig();

  expect(config).toEqual({
    apiBaseUrl: 'https://api.learn.example.com',
    environment: 'staging',
    telemetryEndpoint: 'https://otel.example.com/v1/traces',
    supportZaloUrl: 'https://zalo.me/vni-support',
  });
});

it('falls back field by field when only part of the config was injected', () => {
  window.__VNI_RUNTIME_CONFIG__ = { telemetryEndpoint: 'https://otel.example.com/v1/traces' };

  const config = getRuntimeConfig();

  expect(config.apiBaseUrl).toBe('http://localhost:5099');
  expect(config.telemetryEndpoint).toBe('https://otel.example.com/v1/traces');
});

it('ignores an empty-string injected value the same as a missing one', () => {
  // A generated env-config.js writes "" for an unset environment variable,
  // not the key's absence — the fallback has to treat both the same way.
  window.__VNI_RUNTIME_CONFIG__ = { apiBaseUrl: '' };

  const config = getRuntimeConfig();

  expect(config.apiBaseUrl).toBe('http://localhost:5099');
});

/**
 * `supportZaloUrl` is the only runtime value rendered to a visitor who is not
 * signed in — the "ask a human" link on the forgot-password page, which exists
 * because registration stopped collecting an email and there is no
 * self-service reset. The container entrypoint refuses a non-`https://` value
 * outright; these cover the paths the entrypoint never sees — the checked-in
 * `public/env-config.js` a dev server hands out, a hand-edited file in a
 * running container, anything else that writes the global.
 *
 * Red-when-removed: drop the `httpsOrNull` guard and return the injected
 * string straight through, and the first two of these fail — a `javascript:`
 * value reaches the page, where an `href` makes it script execution on our own
 * origin.
 */
it('refuses a supportZaloUrl that is not https, whatever else it looks like', () => {
  for (const hostile of [
    'javascript:alert(document.cookie)',
    'data:text/html,<script>alert(1)</script>',
    'http://zalo.me/vni-support',
    'zalo.me/vni-support',
  ]) {
    window.__VNI_RUNTIME_CONFIG__ = { supportZaloUrl: hostile };

    expect(getRuntimeConfig().supportZaloUrl, hostile).toBeNull();
  }
});

it('reads an empty-string supportZaloUrl as no channel configured', () => {
  // A generated env-config.js can write "" where this file writes null; the
  // page has one story for both — "there is no support channel".
  window.__VNI_RUNTIME_CONFIG__ = { supportZaloUrl: '' };

  expect(getRuntimeConfig().supportZaloUrl).toBeNull();
});
