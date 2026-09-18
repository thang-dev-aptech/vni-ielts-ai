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
  isReducedMotionQuery,
  normaliseSelector,
  declarationsIn,
  parseRules,
  startsEndlessAnimation,
  stopsAnimation,
  endlessAnimationViolations,
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

// ── Clause 3: an endless animation must have an off switch ─────────────────

const endlessOffenders = (css) =>
  endlessAnimationViolations(parseRules(css)).map((v) => v.selector);

const REDUCE = '@media (prefers-reduced-motion: reduce)';

test('an infinite animation with no reduced-motion rule at all is caught', () => {
  const css = '.hero {\n  animation: float 4s ease-in-out infinite alternate;\n}';
  assert.deepEqual(endlessOffenders(css), ['.hero']);
  assert.equal(endlessAnimationViolations(parseRules(css))[0].line, 1);
});

test('an infinite animation switched off below itself passes', () => {
  const css = `.hero { animation: float 4s infinite; }\n${REDUCE} { .hero { animation: none; } }`;
  assert.deepEqual(endlessOffenders(css), []);
});

test('an off switch ABOVE the rule it cancels does not count', () => {
  // Identical selectors tie on specificity and a media query adds none, so the
  // later declaration wins. This is the `.work-filter-caret` failure, which was
  // in the file for as long as the block was above the responsive rules.
  const css = `${REDUCE} { .hero { animation: none; } }\n.hero { animation: float 4s infinite; }`;
  assert.deepEqual(endlessOffenders(css), ['.hero']);
  assert.match(endlessAnimationViolations(parseRules(css))[0].reason, /ABOVE this rule/);
});

test('the two failures are reported differently, because the fixes differ', () => {
  const never = endlessAnimationViolations(parseRules('.a { animation: x 1s infinite; }'));
  assert.match(never[0].reason, /no\s+@media/);
  assert.doesNotMatch(never[0].reason, /ABOVE/);
});

test('a selector group is checked member by member', () => {
  const css = `.a,\n.b { animation: x 1s infinite; }\n${REDUCE} { .a { animation: none; } }`;
  assert.deepEqual(endlessOffenders(css), ['.b']);
});

test('matching is exact — a near-miss spelling is not an off switch', () => {
  // `.chip-pulse-dot::after` is not cancelled by `.chip-pulse-dot`. Deciding
  // that one "covers" the other needs the cascade and a DOM; this file has
  // neither, so it says so instead of guessing.
  const css = `.dot::after { animation: ripple 2s infinite; }\n${REDUCE} { .dot { animation: none; } }`;
  assert.deepEqual(endlessOffenders(css), ['.dot::after']);
});

test('a finite animation is not clause 3 business', () => {
  assert.deepEqual(endlessOffenders('.scrim { animation: fadeIn 0.2s ease; }'), []);
  assert.deepEqual(endlessOffenders('.x { animation: spin 1s 3; }'), []);
});

test('animation-iteration-count: infinite counts as infinite', () => {
  const css = '.x {\n  animation-name: spin;\n  animation-iteration-count: infinite;\n}';
  assert.deepEqual(endlessOffenders(css), ['.x']);
});

test('animation-name: none is an off switch, and so is the shorthand', () => {
  assert.ok(stopsAnimation({ declarations: declarationsIn('animation: none') }));
  assert.ok(stopsAnimation({ declarations: declarationsIn('animation-name: none') }));
  assert.ok(stopsAnimation({ declarations: declarationsIn('animation: none !important') }));
  // Fewer iterations is not "off".
  assert.ok(!stopsAnimation({ declarations: declarationsIn('animation-iteration-count: 1') }));
});

test('"infinite" has to be a token, not a substring of a keyframe name', () => {
  assert.ok(
    !startsEndlessAnimation({ declarations: declarationsIn('animation: infiniteScroll 2s') }),
  );
  assert.ok(
    startsEndlessAnimation({
      declarations: declarationsIn('animation: infiniteScroll 2s infinite'),
    }),
  );
});

test('a bare (prefers-reduced-motion) query is recognised as well', () => {
  assert.ok(isReducedMotionQuery('@media (prefers-reduced-motion)'));
  assert.ok(isReducedMotionQuery('@media (prefers-reduced-motion: reduce)'));
  assert.ok(isReducedMotionQuery('@media screen and (prefers-reduced-motion:reduce)'));
  assert.ok(!isReducedMotionQuery('@media (max-width: 1080px)'));
});

// ── Clause 3's parser, so a rule cannot hide behind a nesting level ────────

test('a keyframe stop is not read as a selector', () => {
  // Without this, `0%` and `100%` arrive as selectors that animate nothing and
  // `from`/`to` become rules nobody can cancel.
  const css =
    '@keyframes float {\n  0% { transform: translateY(0); }\n  100% { transform: translateY(-9px); }\n}';
  assert.deepEqual(parseRules(css), []);
});

test('a rule inside a media query is found, and knows which query it is in', () => {
  const css = `@media (max-width: 900px) {\n  .a { animation: x 1s infinite; }\n}\n${REDUCE} {\n  .a { animation: none; }\n}`;
  const rules = parseRules(css);
  assert.deepEqual(
    rules.map((r) => r.selectors),
    [['.a'], ['.a']],
  );
  assert.deepEqual(
    rules.map((r) => r.reducedMotion),
    [false, true],
  );
  assert.deepEqual(endlessOffenders(css), []);
});

test('an infinite animation inside a media query still needs an off switch', () => {
  const css = '@media (min-width: 900px) {\n  .a { animation: x 1s infinite; }\n}';
  assert.deepEqual(endlessOffenders(css), ['.a']);
  assert.equal(endlessAnimationViolations(parseRules(css))[0].line, 2);
});

test('parseRules reports the line the selector starts on', () => {
  const css = '\n\n.a,\n.b {\n  color: red;\n}';
  assert.equal(parseRules(css)[0].line, 3);
});

test('a comment cannot fake a rule, and does not shift the lines', () => {
  const css = '/* .fake { animation: x 1s infinite; }\n   still a comment */\n.a { color: red; }';
  const rules = parseRules(css);
  assert.deepEqual(
    rules.map((r) => r.selectors),
    [['.a']],
  );
  assert.equal(rules[0].line, 3);
});

test('normaliseSelector collapses whitespace and keeps case', () => {
  assert.equal(normaliseSelector('\n  .a   >   .B  '), '.a > .B');
});

test('declarationsIn survives a value carrying colons and commas', () => {
  const declarations = declarationsIn(
    'background: url(data:image/png;base64,AAA); transition: a 1s, b 2s',
  );
  assert.deepEqual(
    declarations.map((d) => d.property),
    ['background', 'transition'],
  );
});

// ── What clause 3 deliberately does NOT do ─────────────────────────────────

test('clause 3 says nothing about transitions — that gap is on purpose', () => {
  // Requiring every transition to be cancelled under reduced motion is a
  // larger policy than this file has a mandate for, and would flag stylesheets
  // whose owners have not agreed to it. The reduced-motion transition lists in
  // `practice.css` are therefore maintained by hand and unguarded. Anyone
  // widening this rule should change this test first, deliberately.
  assert.deepEqual(endlessOffenders('.a { transition: transform 0.2s ease; }'), []);
});

// ── The check that actually protects the repository ────────────────────────

test('every scanned stylesheet obeys the rule', async () => {
  const fs = (await import('node:fs')).default;
  const path = (await import('node:path')).default;

  const root = path.join(import.meta.dirname, '..');
  const offenders = [];
  let declarationCount = 0;

  let endlessCount = 0;

  for (const file of SCANNED_FILES) {
    const css = fs.readFileSync(path.join(root, file), 'utf8');

    const declarations = transitionDeclarations(css);
    declarationCount += declarations.length;
    for (const violation of violations(declarations)) {
      offenders.push(`${file}:${violation.line} — ${violation.property}`);
    }

    const rules = parseRules(css);
    endlessCount += rules.filter(startsEndlessAnimation).length;
    for (const violation of endlessAnimationViolations(rules)) {
      offenders.push(`${file}:${violation.line} — ${violation.selector}`);
    }
  }

  assert.ok(declarationCount > 0, 'no transitions found at all — the parser or the list is wrong');
  assert.ok(
    endlessCount > 0,
    'no infinite animation found in any scanned sheet — practice.css has four, so the parser is wrong',
  );
  assert.deepEqual(offenders, []);
});

test('auth.css is in the scan list, which is the half that is easy to forget', () => {
  // It was fixed and added in one commit on 2026-09-18. A later commit that
  // drops it from the list would leave the fix standing and the guard gone —
  // which is the state the file was already in once.
  assert.ok(SCANNED_FILES.includes('apps/web/src/styles/auth.css'));
});
