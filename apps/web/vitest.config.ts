import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test-setup.ts'],

    /*
     * Longer than the 5s default, and the reason is not slow tests.
     *
     * The suite spins up a whole `<App/>` per test under StrictMode, which
     * double-invokes every mount effect, and vitest runs the files in parallel
     * workers. On a machine already running the API, both dev servers and
     * Mongo — which is every machine anyone develops this on — a `waitFor`
     * that normally settles in 40ms can miss a 1s deadline purely from CPU
     * contention.
     *
     * The failures that produced were scattered and non-reproducible: a
     * different two or three tests each run, always ones that await a fetch,
     * always around 4–7 seconds. That reads like a real defect and is not one,
     * which is worse than a red suite — it teaches people to re-run rather
     * than to look.
     *
     * Raising the ceiling does not slow a passing run down: a test that
     * settles still settles immediately. It only changes how long a genuinely
     * stuck test takes to admit it.
     */
    testTimeout: 15_000,
  },
});
