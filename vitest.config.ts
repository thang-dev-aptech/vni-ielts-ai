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
 *
 * <b>It refuses only the root, and that distinction is load-bearing.</b> Vitest
 * walks up from the working directory until it finds a configuration, so a
 * package that has none of its own — today `packages/types`, whose tests need
 * nothing but Node — reaches this file and would have been refused along with
 * the mistake this file exists to catch. `pnpm check` caught exactly that, and
 * the first version of this guard broke `@vni/types` in the same commit that
 * fixed the flake.
 *
 * So the refusal is conditional on where vitest was invoked from, not on which
 * file it happened to find. Run from the root, it throws. Reached by a package
 * that owns no configuration, it hands back the empty configuration that
 * package used to get when this file did not exist — with `root` pinned to the
 * caller, because otherwise collection would widen to the whole repository and
 * re-create the 657-test sweep from the other direction.
 */

import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const WHERE_TESTS_LIVE = [
  'pnpm test                      # every package, the way CI runs them',
  'pnpm --filter @vni/web test    # one package, from anywhere',
  'cd apps/web && npx vitest run  # one package, watching or filtered',
].join('\n  ');

const REPO_ROOT = dirname(fileURLToPath(import.meta.url));
const INVOKED_FROM = process.cwd();

if (INVOKED_FROM === REPO_ROOT)
  throw new Error(
    `\n\nThere is no vitest project at the repository root.\n\n` +
      `Run it from the package that owns the tests, or through the workspace:\n\n  ` +
      `${WHERE_TESTS_LIVE}\n\n` +
      `Without this file, vitest would have found no configuration, said nothing ` +
      `about that, and run every test it could find under Node's environment — ` +
      `657 of them, failing on \`localStorage is not defined\` and similar, none ` +
      `of which would have told you anything about the code.\n`,
  );

// A plain object, not `defineConfig`: the repository root does not depend on
// vitest, so importing `vitest/config` here fails at the root with
// `UNRESOLVED_IMPORT` — which would replace the message below with a confusing
// one, in exactly the case the message exists for.
export default { test: { root: INVOKED_FROM } };
