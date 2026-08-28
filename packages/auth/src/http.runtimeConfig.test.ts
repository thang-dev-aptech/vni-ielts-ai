import { afterEach, expect, it, vi } from 'vitest';

/**
 * F2.6 — `runtimeConfig.test.ts` proves the reader's own precedence in
 * isolation; this proves `http.ts` actually wired `BASE` to it, rather than
 * still reading `import.meta.env['VITE_API_BASE']` directly.
 *
 * `BASE` is computed once at module load (`const BASE = ...`), matching the
 * real page: `env-config.js` runs once, before the bundle. So the global has
 * to be set *before* `http.js` is first imported — `vi.resetModules()` plus
 * a fresh dynamic import is what makes that true inside one test file,
 * instead of reusing whatever `apiBase()` resolved to when some earlier test
 * first imported the module.
 */

afterEach(() => {
  delete window.__VNI_RUNTIME_CONFIG__;
  vi.resetModules();
});

it('apiBase() reflects the config injected before the module first loads', async () => {
  window.__VNI_RUNTIME_CONFIG__ = { apiBaseUrl: 'https://api.learn.example.com' };
  vi.resetModules();

  const { apiBase } = await import('./http.js');

  expect(apiBase()).toBe('https://api.learn.example.com');
});

it('apiBase() falls back to the Vite build-time default with nothing injected', async () => {
  vi.resetModules();

  const { apiBase } = await import('./http.js');

  expect(apiBase()).toBe('http://localhost:5099');
});
