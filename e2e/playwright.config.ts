import { defineConfig, devices } from '@playwright/test';

/**
 * The browser suite — `I7.5`.
 *
 * ── What only a real browser can answer ───────────────────────────────────
 *
 * Everything below this file was written against a defect that no unit test
 * and no integration test can reach, because the defect lives in the browser
 * rather than in either side of the wire: two tabs racing over one refresh
 * token, a laptop lid closing mid-sitting, a request that arrives after the one
 * sent behind it, an upload still in flight when the paper is handed in.
 *
 * `packages/auth/src/coordinator.ts`, `patchJournal.ts` and the retry ladder in
 * `useAnswerSheet.ts` were all built for exactly these situations and have never
 * been run in a browser at all.
 *
 * ── Its own ports, its own database ───────────────────────────────────────
 *
 * <b>5199 and 5273, not 5099 and 5173.</b> A developer running the app has those
 * two occupied, and a suite that killed them to run would be a suite nobody runs
 * twice. `reuseExistingServer` is off for the same reason in reverse: reusing a
 * server somebody else started means testing whatever code *they* last built.
 *
 * <b>`vni_ielts_e2e`, not `vni_ielts_dev`.</b> This suite registers accounts and
 * sits exams; pointing it at the development database would fill somebody's
 * dashboard with the debris of a test run.
 */
const API = 'http://localhost:5199';
const WEB = 'http://localhost:5273';

export default defineConfig({
  testDir: './tests',

  /*
   * <b>Serial, deliberately.</b> Several of these tests move a clock, take a
   * session offline, or race two tabs — all of which are process-wide facts
   * about one browser and one API. Running them in parallel would make each
   * one's world depend on what another was doing at that instant, and the
   * failures would be irreproducible, which is worse than slow.
   */
  workers: 1,
  fullyParallel: false,

  /*
   * <b>No retries, and that is a decision rather than an omission.</b> This
   * suite exists to find race conditions. A retry turns "fails one time in
   * three" — which is exactly what a race looks like — into a green build.
   */
  retries: 0,

  timeout: 60_000,
  expect: { timeout: 15_000 },

  reporter: [['list']],

  use: {
    baseURL: WEB,
    trace: 'retain-on-failure',
    video: 'off',
  },

  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },

    /*
     * The phone matters here beyond viewport width: this is the device that
     * suspends a tab when the screen locks, loses its network in a lift, and
     * is the one most learners will sit an exam on.
     */
    { name: 'mobile', use: { ...devices['Pixel 7'] } },
  ],

  webServer: [
    {
      command: 'dotnet run --project ../backend/src/Vni.Ielts.Api --no-launch-profile',
      url: `${API}/health/ready`,
      reuseExistingServer: false,
      timeout: 180_000,
      stdout: 'pipe',
      stderr: 'pipe',
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: API,
        // Its own database, dropped and reseeded by the fixtures rather than
        // shared with whatever a developer has been doing.
        Mongo__Database: 'vni_ielts_e2e',
        // The four-module synthetic paper. Off by default so it never reaches
        // a learner's catalogue. → DevelopmentExamSeeder
        Seed__IncludeSyntheticExams: 'true',
        // Fixed, so a restart mid-suite does not invalidate every session the
        // suite is holding. Test-only, 48 characters, and never a production
        // key: production supplies its own or refuses to boot.
        Jwt__SigningKey: 'e2e-only-signing-key-not-for-production-000000000',
        // 5273 is not in appsettings, and without this every request the
        // browser makes is refused by CORS — which surfaces as a network error
        // and reads like the API is down.
        Cors__Origins__0: WEB,
      },
    },
    {
      command: 'pnpm --filter @vni/web dev --port 5273 --strictPort',
      url: WEB,
      reuseExistingServer: false,
      timeout: 120_000,
      cwd: '..',
      env: { VITE_API_BASE: API },
    },
  ],
});
