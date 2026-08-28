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
});

it('prefers the injected config over the build-time default entirely', () => {
  window.__VNI_RUNTIME_CONFIG__ = {
    apiBaseUrl: 'https://api.learn.example.com',
    environment: 'staging',
    telemetryEndpoint: 'https://otel.example.com/v1/traces',
  };

  const config = getRuntimeConfig();

  expect(config).toEqual({
    apiBaseUrl: 'https://api.learn.example.com',
    environment: 'staging',
    telemetryEndpoint: 'https://otel.example.com/v1/traces',
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
