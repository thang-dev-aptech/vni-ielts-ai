import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { TEST_TIMEOUT_MS } from './src/test-timeouts.js';

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
     * <b>The number is derived, in `src/test-timeouts.ts`, and that is the
     * point.</b> It has to stay above a multiple of Testing Library's
     * `asyncUtilTimeout`, or a test that spends one full async budget on a wait
     * that settles can never reach the deadline of the next one — the test
     * timeout fires first, and it can only say "timed out", never what the test
     * was waiting for. 16 of 24 observed failures arrived with no message for
     * exactly that reason. Editing one of the two numbers here, in isolation,
     * is what re-opens it, so there is now only one place to edit.
     */
    testTimeout: TEST_TIMEOUT_MS,

    /*
     * ── A worker cap that is actually applied ────────────────────────────────
     *
     * <b>`maxWorkers`, not `poolOptions.threads.maxThreads`.</b> This line read
     * `poolOptions: { threads: { maxThreads: 3 } }` from the day the file was
     * created until 18/09/2026, and it never once did anything: vitest's
     * default pool has been `forks` since vitest 2.0, and `poolOptions.threads`
     * is read only by the `threads` pool. A run on this 12-core box was measured
     * forking <b>eleven</b> workers — `availableParallelism() - 1` — with the
     * cap sitting right there in the file. `maxWorkers` is pool-agnostic, so it
     * cannot become a dead branch again if the pool is ever changed.
     *
     * <b>The suite is its own dominant load, and that is what the cap is for.</b>
     * Under identical external load, `progress-history.test.tsx` run alone took
     * 2.2s / 3.2s / 4.7s and passed; the same three tests inside the full suite
     * took 15.5s / 17.0s / 17.4s and failed. The only difference was the ten
     * sibling vitest processes.
     *
     * <b>The cap is measured by counting processes, not by reading config.</b>
     * Direct children of the vitest process, sampled three times during a full
     * run: eleven `node (vitest N)` workers with the old line, and exactly
     * `maxWorkers` with this one — verified at 3, 4, 6 and 11. That is the only
     * claim here that is not confounded by the state of the machine.
     *
     * <b>The earlier note here is deleted rather than corrected.</b> It carried
     * a table — "quiet machine, 3 / 6 / 12 workers", all three ~14s — and
     * concluded "the pool size buys nothing". If those rows were produced by
     * editing `maxThreads`, all three were the same configuration and the
     * conclusion was an artefact of the knob being inert. It is not evidence and
     * must not be read as any.
     *
     * <b>The value stays 3, the number this file has claimed since it was
     * written.</b> Fixing the mechanism and changing the number in the same
     * commit would leave nobody able to say which one did anything, and the
     * wall-clock measurements taken on 18/09 do not order by pool size — the
     * other agents sharing the box moved more between runs than the pool size
     * did. Three is also free where the exposure is smallest: `ubuntu-latest`
     * gives 2–4 vCPU, so CI was already getting 1–3 workers by accident, and
     * this only makes that deliberate.
     *
     * <b>What this is NOT is a reason to restructure the tests.</b> An earlier
     * note here proposed giving the fetch-heavy files a rendered subtree instead
     * of `<App/>`; nothing measured since has supported that.
     *
     * <b>27/08/2026 — and "check the machine first" was too confident.</b>
     * A round of scattered failures had the same signature and a real cause: no
     * stub in `exam-flow` answered `/api/v1/auth/refresh`, which the provider
     * calls on its own timer, so whichever test happened to be running when it
     * fired was signed out and rendered the sign-in page. Alongside it,
     * `AuthContext` scheduled that refresh with `setTimeout(fn, NaN)` — which
     * fires immediately — for any stored session whose expiry would not parse.
     * Both are fixed. That is not proof they were the whole story; it is enough
     * to change the order of the checks: <b>first ask whether the stub answers
     * everything the app calls on its own</b> — refresh, `/me`, providers — and
     * only then reach for `uptime` and `docker ps`. A fixture hole and a loaded
     * machine produce the same scattered, non-reproducible red.
     */
    maxWorkers: 3,
  },
});
