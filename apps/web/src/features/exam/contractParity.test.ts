import { expectTypeOf, it } from 'vitest';
import type {
  CurrentSectionView as ContractCurrentSection,
  MarkingStatusView as ContractMarkingStatus,
  QuestionView as ContractQuestion,
  SaveAnswersRequest as ContractSaveAnswers,
  SessionView as ContractSession,
  SessionResultsView as ContractResults,
} from '@vni/api-client';
import type {
  CurrentSectionView,
  MarkingStatusView,
  QuestionView,
  SessionView,
  SessionResultsView,
} from './examApi.js';

/**
 * The hand-written client types, checked against the API's own contract.
 *
 * <b>Written 2026-08-28. The bug it makes impossible has already happened.</b>
 *
 * `A17` was two sides of one contract disagreeing while both had passing tests:
 * the client spelled a multi-select pick `"A|D"`, the marker accepted `"A,D"`,
 * and nobody owned the sentence between them. Six Reading marks and seven
 * Listening marks lost on every sitting.
 *
 * <b>These are type assertions, so they cost nothing at runtime and fail at
 * build time.</b> A field renamed in a C# record, regenerated into
 * `contracts/openapi`, and regenerated again into `@vni/api-client` makes this
 * file stop compiling — before anybody runs the app.
 *
 * <b>`vitest run` does not check them, and that is worth knowing.</b>
 * `expectTypeOf` is erased at runtime, so these cases pass green in the test
 * runner whatever the types say. <b>`pnpm typecheck` is the gate</b> — verified
 * by breaking the generated type and watching `tsc` fail here while the runner
 * stayed green. A reader who assumes the runner covers this will believe a
 * guarantee that is not being checked.
 *
 * <b>And use `toEqualTypeOf`, never `toMatchTypeOf`.</b> The lenient one agreed
 * with the exact regression this file exists to catch: `null` is assignable to
 * `string` in the direction it checks, so a contract that had lost its
 * nullability passed.
 *
 * ── Why the hand-written types still exist ────────────────────────────────
 *
 * `examApi.ts` carries doc comments explaining <i>why</i> each field is shaped
 * as it is, and those are the most valuable thing in it. Deleting them for a
 * generated alias would trade an explanation for a guarantee when both are
 * available: keep the prose, and let this file be the guarantee.
 *
 * The day that trade stops being worth it, the fix is to alias the generated
 * types directly and delete this file — not to let the two drift.
 */

it('the results view matches the contract', () => {
  /*
   * <b>Assignable in both directions, deliberately.</b> One direction alone
   * would let the hand-written type quietly gain a field the API never sends —
   * which is how a screen ends up rendering something that is always
   * `undefined` and nobody notices for a month.
   */
  expectTypeOf<SessionResultsView['sessionId']>().toEqualTypeOf<ContractResults['sessionId']>();
  expectTypeOf<SessionResultsView['overallBand']>().toEqualTypeOf<ContractResults['overallBand']>();
  expectTypeOf<SessionResultsView['submittedAt']>().toEqualTypeOf<ContractResults['submittedAt']>();
});

it('the marking status matches the contract', () => {
  /*
   * The shape most likely to be got wrong by hand: four states with four
   * different things for the learner to do, where a boolean would compile fine
   * and be wrong three times out of four. → `I3.6`
   */
  expectTypeOf<MarkingStatusView['attempts']>().toEqualTypeOf<ContractMarkingStatus['attempts']>();
  expectTypeOf<MarkingStatusView['reason']>().toEqualTypeOf<ContractMarkingStatus['reason']>();
});

it('response slots match the public question contract', () => {
  expectTypeOf<QuestionView['slots']>().toEqualTypeOf<ContractQuestion['slots']>();
});

it('the runner scope matches the server-owned session projection', () => {
  /*
   * OpenAPI emits `string[]` for module lists; the hand-written client narrows
   * sitting order to `ExamModule[]` the same way it already does for
   * `completedModules`. Exact equality would force the client to drop that
   * narrowing, so the wire shape and the client narrowing are checked apart.
   */
  expectTypeOf<ContractSession['moduleSequence']>().toEqualTypeOf<string[]>();
  expectTypeOf<SessionView['moduleSequence'][number]>().toEqualTypeOf<
    'reading' | 'listening' | 'writing' | 'speaking'
  >();
  expectTypeOf<SessionView['practiceUnitId']>().toEqualTypeOf<ContractSession['practiceUnitId']>();
  expectTypeOf<SessionView['scope']>().toEqualTypeOf<ContractSession['scope']>();
  expectTypeOf<SessionView['completedPartIds']>().toEqualTypeOf<
    ContractSession['completedPartIds']
  >();
  expectTypeOf<CurrentSectionView['partId']>().toEqualTypeOf<ContractCurrentSection['partId']>();
  expectTypeOf<CurrentSectionView['audioPlayback']>().toEqualTypeOf<
    ContractCurrentSection['audioPlayback']
  >();
});

it('an autosave can clear an answer', () => {
  /*
   * <b>The one the generator got wrong, and the reason this file exists.</b>
   *
   * `changes` is `IReadOnlyDictionary<string, string?>` in C# and the null is
   * load-bearing: it is how a learner rubs an answer out, where an absent key
   * means the question was untouched. The .NET OpenAPI generator honours
   * nullable reference types on ordinary properties and does not reach inside a
   * dictionary value, so the emitted schema said `string` — and a client
   * generated from it would have refused to send an erase.
   *
   * Fixed with a schema transformer in `Program.cs`. This is what stops it
   * silently coming back. → `I1.5`
   */
  type Value = NonNullable<ContractSaveAnswers['changes']>[string];

  /*
   * <b>`toEqualTypeOf`, not `toMatchTypeOf`.</b> The lenient one passes when
   * the contract says plain `string` — `null` is assignable to `string` under
   * a structural match in the direction it checks — so it agreed with the
   * regression it was written to catch. Verified: removing the schema
   * transformer leaves this line red and the lenient one green.
   */
  expectTypeOf<Value>().toEqualTypeOf<string | null>();
});
