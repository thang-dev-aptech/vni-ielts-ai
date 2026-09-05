/**
 * IELTS band scores — the closed value set, and how to display one.
 *
 * ─────────────────────────────────────────────────────────────────────────
 * WHAT THIS FILE DELIBERATELY DOES NOT DO
 *
 * It does not compute an overall band from section bands.
 *
 * That rule is asymmetric — a mean ending in .25 rounds up to the next half
 * band, one ending in .75 rounds up to the next whole band — and a naive
 * round-to-nearest-half gets the .75 case wrong (6.75 would yield 6.5 instead
 * of 7.0). The rule lives in application code on the SERVER, in exactly one
 * place, with its own table-driven test.
 *
 * Reimplementing it here would create a second source of truth that drifts
 * silently, and the drift would surface as a client showing a different
 * overall band from the one on the learner's result record. The client
 * displays what the server computed. It does not recompute it.
 *
 * → docs/domain/band-scoring.md § "Overall band score"
 * ─────────────────────────────────────────────────────────────────────────
 */

/**
 * Bands are reported in whole and half bands only. This is a closed set, not
 * a range: 6.3 and 7.77 are not band scores, which is also why AI output
 * schemas declare the band as a JSON Schema `enum` rather than
 * minimum/maximum. Numeric constraints are commonly ignored by structured
 * output implementations; `enum` is honoured.
 *
 * → docs/ai/output-contracts.md § "Why enum and not minimum/maximum"
 */
export const BAND_VALUES = [
  0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 4.5, 5, 5.5, 6, 6.5, 7, 7.5, 8, 8.5, 9,
] as const;

export type Band = (typeof BAND_VALUES)[number];

const BAND_SET: ReadonlySet<number> = new Set(BAND_VALUES);

/** True only for a value that is an actual reportable band. */
export function isBand(value: unknown): value is Band {
  return typeof value === 'number' && BAND_SET.has(value);
}

/**
 * Where a band came from. Product law L4: an AI-produced band always carries a
 * reference label and must never be presented as equal in standing to one
 * computed from an answer key.
 */
export type BandProvenance = 'answer-key' | 'ai-advisory';

/** The states a section score can be in from the learner's point of view. */
export type ScoreState =
  | { status: 'scored'; band: Band; provenance: BandProvenance }
  | { status: 'pending' }
  | { status: 'evaluating' }
  | { status: 'failed' };

/**
 * The placeholder shown when there is no band yet.
 *
 * Product law L3: a missing score renders as an em dash, NEVER as 0.0.
 * A fabricated-looking zero is indistinguishable from a real zero to the
 * person reading it, and 0.0 is a valid band.
 */
export const NO_SCORE = '—';

/**
 * Format a band for display, in the one way the product is allowed to.
 *
 * Always one decimal place: "7.0", not "7". A band of 7 and a band of 7.0 are
 * the same value, but a column mixing "7" and "6.5" reads as inconsistent
 * precision and invites the reader to wonder which one is rounded.
 */
export function formatBand(band: Band | null | undefined): string {
  return band == null ? NO_SCORE : band.toFixed(1);
}

/**
 * Format a whole score state. Anything that is not a completed score renders
 * as the placeholder — a failed evaluation shows as failed elsewhere in the
 * UI, and must never borrow a number to look complete.
 */
export function formatScoreState(state: ScoreState): string {
  return state.status === 'scored' ? formatBand(state.band) : NO_SCORE;
}

/** Product law L4 — does this score require the advisory label? */
export function requiresAdvisoryLabel(state: ScoreState): boolean {
  return state.status === 'scored' && state.provenance === 'ai-advisory';
}
