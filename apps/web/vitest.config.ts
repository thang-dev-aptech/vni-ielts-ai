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

    /*
     * Three workers, not one per core — as insurance, not as a fix.
     *
     * <b>The suite is not flaky. The machine was, and it took two rounds of
     * measurement to say that honestly.</b>
     *
     * The first round saw two to eight failures a run, always in files
     * awaiting a fetch, never the same ones, every one passing in isolation —
     * and concluded the pool size was the cause, because dropping it made the
     * failures go away. That baseline was contaminated: the same machine was
     * running the local Docker stack, a dotnet API, three unrelated
     * containers and a browser holding nineteen pages from a review session.
     *
     * Re-measured with those shut down, twelve cores, same commit:
     *
     *   quiet machine,  3 workers — 165/165 · 14.1s, 14.5s
     *   quiet machine,  6 workers — 165/165 · 14.4s, 15.7s
     *   quiet machine, 12 workers — 165/165 · 14.6s, 15.3s
     *   loaded machine, 3 workers — occasional single failure, 55–85s
     *
     * So the pool size buys nothing on a quiet machine and does not fully
     * rescue a loaded one. Three is kept because it is free when there is
     * headroom and it narrows the failure window when there is not — a CI box
     * and a developer laptop with a build running are both the loaded case.
     *
     * <b>What this is NOT is a reason to restructure the tests.</b> The
     * earlier note here proposed giving the fetch-heavy files a rendered
     * subtree instead of `<App/>`; on this evidence that would have been a
     * day spent on a phantom.
     *
     * <b>27/08/2026 — and "check the machine first" was too confident.</b>
     * A later round of scattered failures had the same signature and a real
     * cause: no stub in `exam-flow` answered `/api/v1/auth/refresh`, which the
     * provider calls on its own timer, so whichever test happened to be running
     * when it fired was signed out and rendered the sign-in page. Alongside it,
     * `AuthContext` scheduled that refresh with `setTimeout(fn, NaN)` — which
     * fires immediately — for any stored session whose expiry would not parse.
     * Both are fixed, and three consecutive full runs are green at the load
     * that used to produce one or two failures.
     *
     * That is not proof they were the whole story. It is enough to change the
     * order of the checks: <b>first ask whether the stub answers everything the
     * app calls on its own</b> — refresh, `/me`, providers — and only then
     * reach for `uptime` and `docker ps`. A fixture hole and a loaded machine
     * produce the same scattered, non-reproducible red.
     */
    poolOptions: { threads: { maxThreads: 3 } },
  },
});
