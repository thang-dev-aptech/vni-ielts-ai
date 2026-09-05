import { describe, expect, it } from 'vitest';
import {
  BAND_VALUES,
  NO_SCORE,
  formatBand,
  formatScoreState,
  isBand,
  requiresAdvisoryLabel,
  type ScoreState,
} from './band.js';

describe('the band value set is closed', () => {
  it('accepts every reportable band', () => {
    for (const b of BAND_VALUES) expect(isBand(b)).toBe(true);
  });

  it('rejects values that are not half-steps', () => {
    // 6.3 and 7.77 are the shapes a model returns when a schema declared
    // minimum/maximum instead of enum, and the provider ignored the range.
    for (const v of [6.3, 7.77, 0.25, 8.75]) expect(isBand(v)).toBe(false);
  });

  it('rejects out-of-scale values without clamping them', () => {
    // 9.5 and 47 must be REJECTED, never clamped to 9. Clamping turns a
    // visible fault into a plausible-looking wrong score, and nobody
    // investigates a plausible score. → docs/ai/output-contracts.md check 4
    for (const v of [-0.5, 9.5, 10, 47]) expect(isBand(v)).toBe(false);
  });

  it('rejects non-numbers', () => {
    for (const v of ['7', '7.0', null, undefined, NaN, {}]) expect(isBand(v)).toBe(false);
  });
});

describe('product law L3 — no score shows an em dash, never 0.0', () => {
  it('renders null and undefined as the placeholder', () => {
    expect(formatBand(null)).toBe(NO_SCORE);
    expect(formatBand(undefined)).toBe(NO_SCORE);
  });

  it('still renders a genuine zero as 0.0', () => {
    // The whole point of the em dash is that 0.0 is a REAL band. If absence
    // rendered as 0.0 there would be no way to tell the two apart.
    expect(formatBand(0)).toBe('0.0');
  });

  it('renders pending, evaluating and failed states as the placeholder', () => {
    const states: ScoreState[] = [
      { status: 'pending' },
      { status: 'evaluating' },
      { status: 'failed' },
    ];
    for (const s of states) expect(formatScoreState(s)).toBe(NO_SCORE);
  });
});

describe('band formatting is consistent to one decimal', () => {
  it('pads whole bands', () => {
    expect(formatBand(7)).toBe('7.0');
    expect(formatBand(9)).toBe('9.0');
  });

  it('keeps half bands', () => {
    expect(formatBand(6.5)).toBe('6.5');
  });
});

describe('product law L4 — an AI band is always labelled advisory', () => {
  it('requires the label for an AI-produced band', () => {
    expect(requiresAdvisoryLabel({ status: 'scored', band: 7, provenance: 'ai-advisory' })).toBe(
      true,
    );
  });

  it('does not require it for an answer-key band', () => {
    expect(requiresAdvisoryLabel({ status: 'scored', band: 7, provenance: 'answer-key' })).toBe(
      false,
    );
  });

  it('does not require it when there is no score to label', () => {
    expect(requiresAdvisoryLabel({ status: 'failed' })).toBe(false);
  });
});
