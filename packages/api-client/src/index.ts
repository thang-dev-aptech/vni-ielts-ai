import type { components, paths } from './generated/schema.js';

/**
 * The API's own types, generated from `contracts/openapi/v1.json`.
 *
 * <b>Written 2026-08-28. Before it, both clients hand-copied the contract.</b>
 *
 * `SessionResultsView` existed twice — once as a C# record and once as a
 * TypeScript interface — with nothing making them agree. That is not a
 * theoretical risk here: the most expensive bug this product has had, `A17`,
 * was exactly two sides of one contract disagreeing while both had passing
 * tests. The client spelled a multi-select pick `"A|D"`; the marker accepted
 * `"A,D"`. Six Reading marks and seven Listening marks lost on every sitting,
 * and nobody owned the sentence between them.
 *
 * A generated type makes that class of bug impossible rather than unlikely.
 *
 * ── How this file earns its place over importing the schema directly ──────
 *
 * `components['schemas']['SessionResultsView']` is correct and unreadable, and
 * a screen that writes it out is a screen coupled to the generator's shape
 * rather than to the API's. The aliases below are the vocabulary the product
 * already uses, pointed at the generated definitions — so a rename in the API
 * fails the build here, once, instead of in forty call sites.
 *
 * <b>Nothing here is hand-written data.</b> This file names things; it never
 * describes them. The moment a shape is declared by hand rather than aliased,
 * the guarantee is gone and the second copy is back.
 */

/** Every schema the API defines, by its server-side name. */
export type Schemas = components['schemas'];

/** Every route, with its parameters and responses. */
export type Paths = paths;

// ── The exam engine ───────────────────────────────────────────────────────

/** A sitting: its status, the open section, and the server's deadline. */
export type SessionView = Schemas['SessionView'];

/** The open section — questions, answers, the revision and the ordering tokens. */
export type CurrentSectionView = Schemas['CurrentSectionView'];

export type PartView = Schemas['PartView'];
export type QuestionView = Schemas['QuestionView'];

/** Results: deterministic section scores, markings, and what is still owed. */
export type SessionResultsView = Schemas['SessionResultsView'];

export type SectionResultView = Schemas['SectionResultView'];
export type SectionMarkingView = Schemas['SectionMarkingView'];

/**
 * Why a module has no band yet.
 *
 * The one shape most likely to be got wrong by hand: four states with four
 * different things for the learner to do, where a single boolean would compile
 * fine and be wrong three times out of four. → `I3.6`
 */
export type MarkingStatusView = Schemas['MarkingStatusView'];

/**
 * An autosave.
 *
 * <b>`changes` accepts `null` per question, and that is load-bearing.</b> A
 * null value is how a learner rubs an answer out; an absent key means the
 * question was untouched. A generated type that had made the value
 * non-nullable would have made an erase unsendable. → `I1.5`
 */
export type SaveAnswersRequest = Schemas['SaveAnswersRequest'];

export type StartSessionRequest = Schemas['StartSessionRequest'];

// ── Identity ──────────────────────────────────────────────────────────────

export type LoginRequest = Schemas['LoginRequest'];
export type RegisterRequest = Schemas['RegisterRequest'];
export type RefreshRequest = Schemas['RefreshRequest'];
export type SetPasswordRequest = Schemas['SetPasswordRequest'];
export type SetPhoneRequest = Schemas['SetPhoneRequest'];

/**
 * The six digits from the verification email.
 *
 * `[QUYẾT ĐỊNH]` chủ sản phẩm, 28/08/2026 — a code rather than a link, because
 * the learner is already signed in and already on their profile page.
 */
export type ConfirmEmailCodeRequest = Schemas['ConfirmEmailCodeRequest'];

// ── Errors ────────────────────────────────────────────────────────────────

/**
 * The shape every failure takes.
 *
 * <b>Clients branch on `code`, never on prose.</b> The detail is written for a
 * person and changes when somebody improves the wording; the code is the
 * contract.
 */
export interface ApiProblem {
  title: string;
  status: number;
  detail: string;
  code: string;
  traceId?: string;
  errors?: Array<{ path: string; code: string; message: string }>;
  /** Present on a 429. Seconds to wait before retrying. */
  retryAfterSeconds?: number;
}
