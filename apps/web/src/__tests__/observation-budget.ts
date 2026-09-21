import { AUTOSAVE_MS } from '../features/exam/useAnswerSheet.js';

/**
 * How long to watch for something that must <b>not</b> happen.
 *
 * <b>Derived from the product's own debounce, and that is the whole point.</b>
 * A negative assertion — "no second request went out" — is the one shape that
 * cannot wait for its own condition. It watches for a fixed time and then
 * looks, so this number decides what the test measures; a hand-picked literal
 * decides it by accident.
 *
 * It lived in `exam-flow.test.tsx` as `3_000` until 21/09/2026, unconnected to
 * the 1200 ms debounce it exists to outlast. Measured that day: raising
 * `AUTOSAVE_MS` to 9000 left the drain-lock test <b>green</b> — the second
 * debounce could no longer fire inside the window, so `maxInFlight === 1` held
 * for the one reason the test is not allowed to accept. Deleting the drain lock
 * outright would have passed too.
 *
 * Two windows plus a margin: one consumed by the flush the test holds open, one
 * for the second debounce that must fire, and the margin so a loaded machine
 * does not turn a real finding into a timing story.
 *
 * <b>Why this file exists at all.</b> It cannot live in `test-timeouts.ts`,
 * which `vitest.config.ts` imports in plain Node and which therefore may not
 * reach product code — tried on 21/09/2026, and the suite failed to load with
 * `ERR_MODULE_NOT_FOUND` on a transitive `@vni/auth` import. It cannot live in
 * `exam-flow.test.tsx` either, because importing a `.test.tsx` file for one
 * constant re-registers every `it()` in it; the first attempt at the guard
 * below silently ran that 61-test suite a second time.
 */
export const NEGATIVE_OBSERVATION_MS = AUTOSAVE_MS * 2 + 600;
