// W6 — run: node --test scripts/check-css-transitions.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  SCANNED_FILES,
  FLOW_PROPERTIES,
  stripComments,
  splitTopLevel,
  animatedProperty,
  transitionDeclarations,
  violations,
} from './check-css-transitions.mjs';

const propertiesOf = (css) => violations(transitionDeclarations(css)).map((v) => v.property);
const linesOf = (css) => violations(transitionDeclarations(css)).map((v) => v.line);

// ── Clause 1: `all` is never acceptable ────────────────────────────────────

test('transition: all is caught — this is the whole point', () => {
  const css = '.btn {\n  color: red;\n  transition: all 0.25s ease;\n}';
  assert.deepEqual(propertiesOf(css), ['all']);
  assert.deepEqual(linesOf(css), [3]);
});

test('transition-property: all is caught too', () => {
  assert.deepEqual(propertiesOf('.btn { transition-property: all; }'), ['all']);
});

test('all is caught wherever it sits inside the shorthand', () => {
  // The order of the shorthand is free, so a positional parser would miss this.
  assert.deepEqual(propertiesOf('.btn { transition: 0.2s ease-in-out all; }'), ['all']);
});

test('all is caught inside a multi-value transition', () => {
  assert.deepEqual(propertiesOf('.btn { transition: opacity 0.2s ease, all 0.3s linear; }'), [
    'all',
  ]);
});

test('the message says what to do instead, not just that it is wrong', () => {
  const [found] = violations(transitionDeclarations('.b { transition: all 1s ease; }'));
  assert.match(found.reason, /name the properties/);
});

// ── Clause 2: named flow properties ────────────────────────────────────────

test('a transition on padding is caught', () => {
  assert.deepEqual(propertiesOf('.tab { transition: padding 0.2s ease; }'), ['padding']);
});

test('a transition on a border shorthand is caught — it carries a width', () => {
  // The side-tab failure mode: a border that only the selected state sets,
  // animated, so the tab grows and shoves its neighbours while it animates.
  assert.deepEqual(propertiesOf('.tab { transition: border 0.2s ease; }'), ['border']);
  assert.deepEqual(propertiesOf('.tab { transition: border-left 0.2s ease; }'), ['border-left']);
  assert.deepEqual(propertiesOf('.tab { transition: border-width 0.2s ease; }'), ['border-width']);
});

test('border-color is NOT caught — colour animates without layout', () => {
  assert.deepEqual(propertiesOf('.tab { transition: border-color 0.2s ease; }'), []);
  assert.ok(!FLOW_PROPERTIES.has('border-color'));
  assert.ok(!FLOW_PROPERTIES.has('border-radius'));
});

test('the compositor-friendly properties pass', () => {
  const css =
    '.card {\n  transition:\n    transform 0.2s ease,\n    opacity 0.2s ease,\n    box-shadow 0.2s ease,\n    background-color 0.2s ease,\n    color 0.2s ease;\n}';
  assert.deepEqual(propertiesOf(css), []);
});

test('width and height are deliberately NOT flow properties', () => {
  // Not an oversight. `.rec-progress > i` (an upload bar) and `.rec-meter-bar`
  // (an input-level meter) animate exactly these, inside a fixed-size parent,
  // so nothing outside them moves. CSS cannot distinguish that from a card
  // that grows on hover, and a check that guessed would be a false alarm on
  // every progress bar or a licence for every card. Clause 1 is what stops
  // the harmful case: it now has to be written down by name.
  assert.deepEqual(propertiesOf('.bar { transition: width 120ms linear; }'), []);
  assert.deepEqual(propertiesOf('.bar { transition: height 60ms linear; }'), []);
  assert.ok(!FLOW_PROPERTIES.has('width'));
  assert.ok(!FLOW_PROPERTIES.has('height'));
});

// ── Parsing, so a violation cannot hide behind formatting ──────────────────

test('a comment mentioning transition is not a declaration', () => {
  // exam.css carries "No transition, no highlight on change" in a comment.
  const css = '/* No transition: all here, just prose. */\n.x { color: red; }';
  assert.deepEqual(transitionDeclarations(css), []);
});

test('blanking a comment keeps the line numbers true', () => {
  const css = '/* one\n   two */\n.btn { transition: all 1s ease; }';
  assert.deepEqual(linesOf(css), [3]);
  assert.equal(stripComments(css).split('\n').length, css.split('\n').length);
});

test('a multi-line transition is read as one declaration', () => {
  const css = '.card {\n  transition:\n    transform 0.2s ease,\n    padding 0.2s ease;\n}';
  const [declaration] = transitionDeclarations(css);
  assert.deepEqual(declaration.properties, ['transform', 'padding']);
  assert.deepEqual(propertiesOf(css), ['padding']);
});

test('a cubic-bezier is not mistaken for four values', () => {
  const css = '.card { transition: all 0.3s cubic-bezier(0.16, 1, 0.3, 1); }';
  assert.deepEqual(propertiesOf(css), ['all']);
  assert.deepEqual(transitionDeclarations(css)[0].properties, ['all']);
});

test('a var() duration is not mistaken for the property', () => {
  const css = '.x { transition: background var(--dur, 180ms) ease; }';
  assert.deepEqual(transitionDeclarations(css)[0].properties, ['background']);
  assert.deepEqual(propertiesOf(css), []);
});

test('transition: none animates nothing and is not a violation', () => {
  assert.deepEqual(transitionDeclarations('.x { transition: none; }')[0].properties, []);
  assert.deepEqual(propertiesOf('.x { transition: none !important; }'), []);
});

test('splitTopLevel respects brackets', () => {
  assert.deepEqual(splitTopLevel('a 1s cubic-bezier(0, 1, 2, 3), b 2s ease', ','), [
    'a 1s cubic-bezier(0, 1, 2, 3)',
    'b 2s ease',
  ]);
});

test('animatedProperty ignores durations, delays and easings', () => {
  assert.equal(animatedProperty('transform 0.2s 0.1s ease-in-out'), 'transform');
  assert.equal(animatedProperty('0.2s steps(4, end) opacity'), 'opacity');
  assert.equal(animatedProperty('none'), null);
});

test('transition-delay and transition-duration are not read as property lists', () => {
  // Only `transition` and `transition-property` name properties.
  assert.deepEqual(transitionDeclarations('.x { transition-duration: 0.2s; }'), []);
  assert.deepEqual(transitionDeclarations('.x { transition-delay: 0.2s; }'), []);
});

// ── The check that actually protects the repository ────────────────────────

test('every scanned stylesheet obeys the rule', async () => {
  const fs = (await import('node:fs')).default;
  const path = (await import('node:path')).default;

  const root = path.join(import.meta.dirname, '..');
  const offenders = [];
  let declarationCount = 0;

  for (const file of SCANNED_FILES) {
    const declarations = transitionDeclarations(fs.readFileSync(path.join(root, file), 'utf8'));
    declarationCount += declarations.length;
    for (const violation of violations(declarations)) {
      offenders.push(`${file}:${violation.line} — ${violation.property}`);
    }
  }

  assert.ok(declarationCount > 0, 'no transitions found at all — the parser or the list is wrong');
  assert.deepEqual(offenders, []);
});
