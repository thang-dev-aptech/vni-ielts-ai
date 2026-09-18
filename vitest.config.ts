/*
 * ── There is no test suite at the repository root ────────────────────────────
 *
 * <b>This file exists to refuse, and that is its whole job.</b>
 *
 * Until 18/09/2026 there was no vitest configuration here at all, and vitest
 * says nothing when it fails to find one — it silently falls back to its own
 * defaults. So `npx vitest run apps/web/src/__tests__/progress-history.test.tsx`
 * from the root ran that file with `environment: 'node'`, no `setupFiles` and no
 * React plugin, and reported:
 *
 *   ReferenceError: localStorage is not defined   ← 3 failed, at 6ms / 1ms / 0ms
 *
 * A bare `npx vitest list` here collected <b>657 tests</b> across `apps/web`,
 * `apps/admin`, `packages/*`, `plugins/*` and `scripts/*.test.mjs` — every one of
 * them about to fail for the same reason, none of it meaning anything.
 *
 * <b>CI never went this way</b>, so nothing was ever broken by it. It is a trap
 * for a person or an agent who reaches for the obvious command, and it costs
 * them a diagnosis of a suite that is fine. That is the same damage a flaky
 * suite does — a red that carries no information — arriving by a different
 * route, which is why it is closed alongside the flake rather than filed
 * separately.
 *
 * Each package owns its own configuration, because each needs a different one:
 * `apps/web` and `apps/admin` need jsdom, a React plugin and a setup file;
 * `packages/*` and `plugins/*` do not. A root config that tried to serve them
 * all would be wrong for most of them, so this one serves none and says so.
 */

const WHERE_TESTS_LIVE = [
  'pnpm test                      # every package, the way CI runs them',
  'pnpm --filter @vni/web test    # one package, from anywhere',
  'cd apps/web && npx vitest run  # one package, watching or filtered',
].join('\n  ');

throw new Error(
  `\n\nThere is no vitest project at the repository root.\n\n` +
    `Run it from the package that owns the tests, or through the workspace:\n\n  ` +
    `${WHERE_TESTS_LIVE}\n\n` +
    `Without this file, vitest would have found no configuration, said nothing ` +
    `about that, and run every test it could find under Node's environment — ` +
    `657 of them, failing on \`localStorage is not defined\` and similar, none ` +
    `of which would have told you anything about the code.\n`,
);
