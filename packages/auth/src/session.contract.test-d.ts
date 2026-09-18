import type { Me } from './session.js';
import type { Schemas } from '@vni/api-client';

/**
 * Type-level assertions that the session types ARE the contract, not a copy.
 *
 * <b>`tsc --noEmit` is the gate here, not `vitest`.</b> Nothing in this file
 * runs. A drift between a hand-written interface and the generated schema is
 * invisible to a runtime test — the mocked JSON in a test fixture is whatever
 * the fixture says it is, so a test passes against a shape the server never
 * sends. The only thing that catches it is the compiler, so the check has to
 * be a compile error, and the file is named `.test-d.ts` so `vitest` leaves it
 * alone while `tsc` still reads it (`include: ["src"]`).
 *
 * <b>The drift this was written for.</b> `contracts/openapi` declares
 * `MeResponse.mustChangePassword` as a required `boolean`; the hand-written
 * `Me` declared it `boolean | undefined`, because at the time `/me` published
 * no schema at all and an older deployment could plausibly omit the field.
 * That reason expired when `W7` declared the contract (commit `becebde`), and
 * nothing would have noticed.
 *
 * <b>`DeviceSession` and the `/me` response shapes in `apps/web` are bound the
 * same way</b>, at their own call sites. `Session` is not, and cannot be:
 * `/api/v1/auth/login` still declares no response body. → `session.ts`
 */

type IsExactly<Actual, Expected> = [Actual] extends [Expected]
  ? [Expected] extends [Actual]
    ? true
    : false
  : false;

/**
 * Required, not optional. An optional `boolean` is three states where the
 * server has two, and the third one silently means "carry on" — which is the
 * permissive answer to "must this learner change their password?".
 */
export const mustChangePasswordIsRequiredBoolean: IsExactly<Me['mustChangePassword'], boolean> =
  true;

/** Required and nullable, not optional: absent and null are not the same claim. */
export const phoneIsRequiredNullable: IsExactly<Me['phone'], string | null> = true;

/** The whole shape, so a field added to the contract cannot be quietly dropped here. */
export const meIsTheContract: IsExactly<Me, Schemas['MeResponse']> = true;
