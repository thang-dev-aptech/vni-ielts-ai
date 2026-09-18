#!/usr/bin/env node
//
// W6 — a transition in the learner stylesheets must name what it animates,
// and must not animate the box that other content has to move for.
//
// ── Why this file exists, rather than a list of five ───────────────────────
//
// A design review on 2026-09-18 reported "five design-hook findings" in
// `apps/web/src/styles/{practice,dashboard,exam}.css` — side-tab borders and
// layout-property transitions. Nobody wrote the five down line by line, and a
// reconstructed list of five looks exactly like a real list of five while
// being an invention. So the number is not what is preserved here. The rule
// is, because a rule can be re-derived from this file forever and a
// remembered count cannot.
//
// ── The rule ───────────────────────────────────────────────────────────────
//
// 1. `transition: all` (and `transition-property: all`) is forbidden outright.
//    `all` cannot be read. It animates whatever happens to differ between two
//    states, so the day somebody adds `border: 2px` to a `.is-active` rule, or
//    a wider padding to `:hover`, that difference silently becomes a layout
//    animation that nobody wrote, nobody reviewed, and nobody can find by
//    grepping for it. The animation set of an `all` element is not in the
//    stylesheet at all — it is the diff between two rules, computed at
//    runtime. This is the mechanism behind both groups the review reported.
//
// 2. A transition must not name a *flow* property — one whose change moves the
//    element's siblings and ancestors: margin, padding, border widths, the
//    inset properties, gaps, `font-size`, the flex sizing properties and the
//    grid templates. Animating one of those runs layout on every frame and
//    drags the rest of the page along with it. In an exam this is not a
//    performance footnote: the thing that moves under the pointer is a
//    question a learner is trying to answer.
//
// ── What this rule deliberately does NOT decide, and why ───────────────────
//
// `width` and `height` are *not* in the flow list, and that is a decision
// rather than an oversight. `exam.css` animates the height of `.rec-meter-bar`
// and the width of `.rec-progress > i`: an input-level meter and an upload
// progress bar, both children of a parent with a fixed size, so nothing
// outside them moves, and both already opt out under
// `prefers-reduced-motion`. That is the standard idiom for a meter. On a card
// the identical declaration would be a layout animation. **CSS alone cannot
// tell the two apart** — the difference is in the parent and in what the
// component is — and a check that guessed would be either a false alarm on
// every progress bar or a licence for every card.
//
// What keeps the harmful case out is clause 1: a width animation now has to be
// written down, by name, on a line a reviewer can see, instead of arriving for
// free with `all`.
//
// ── Clause 3, added 2026-09-18: an endless animation must have an off switch
//
// An `animation` that runs `infinite` never stops on its own, so under
// `prefers-reduced-motion: reduce` it is not a shortened animation — it is the
// whole of what that setting exists to prevent, running forever, usually
// directly beside the text somebody is reading. Every selector that starts one
// must be named by an *identical* selector inside a
// `@media (prefers-reduced-motion: reduce)` block that sets `animation` (or
// `animation-name`) to `none`.
//
// Two halves of that sentence are load-bearing.
//
// *Identical*, because deciding that some other selector "covers" a rule needs
// the cascade and a DOM, and a stylesheet alone has neither. Exact matching is
// blunt, but it is never wrong about what it reports and its fix instruction is
// exact: name this selector, spelled this way.
//
// *Later in the file*, because identical selectors tie on specificity and
// source order breaks the tie — a media query adds none. An opt-out written
// above the rule it means to cancel is not an opt-out, it is a comment with
// braces. `practice.css` had exactly that: its reduced-motion block listed
// `.work-filter-caret`, and a `@media (max-width: 1080px)` block below it
// re-declared the same transition and won on every screen narrow enough to show
// that control. Clause 3 does not catch that particular one — a transition is
// not an animation — which is why the block now sits at the end of the file,
// where the ordering cannot go wrong again.
//
// What clause 3 does NOT ask: that *every* transition or *every* finite
// animation be cancelled under reduced motion. That is a larger policy than
// this file has a mandate for, and it would flag stylesheets whose owners have
// not agreed to it. Endless motion is the case where the answer is not
// arguable.
//
// ── Scope: it grows with a fix, never alone ────────────────────────────────
//
// Scope is the files below and only those. `auth.css` joined the list on
// 2026-09-18 in the same commit that fixed its two `transition: all`, and that
// pairing is the rule rather than a coincidence. Widening the scan alone fails
// `pnpm check` for work that belongs to somebody else. Fixing alone is worse:
// it looks finished, and nothing stops the identical line landing in the same
// file next week — which is how those two got there in the first place.
//
// The rest of `apps/web/src/styles` is still outside the scan. After the
// 2026-09-18 commit no `transition: all` remains anywhere in that directory,
// but only the four files below are *held* to it.
//
// Usage: node scripts/check-css-transitions.mjs

import fs from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '..');

/** The stylesheets this gate is responsible for. Grow it with a fix, never alone. */
export const SCANNED_FILES = [
  'apps/web/src/styles/practice.css',
  'apps/web/src/styles/dashboard.css',
  'apps/web/src/styles/exam.css',
  'apps/web/src/styles/auth.css',
];

/**
 * Properties whose animation re-runs layout and moves neighbouring content.
 * `width`/`height` are absent on purpose — see the header.
 */
export const FLOW_PROPERTIES = new Set([
  'margin',
  'margin-top',
  'margin-right',
  'margin-bottom',
  'margin-left',
  'margin-inline',
  'margin-inline-start',
  'margin-inline-end',
  'margin-block',
  'margin-block-start',
  'margin-block-end',
  'padding',
  'padding-top',
  'padding-right',
  'padding-bottom',
  'padding-left',
  'padding-inline',
  'padding-inline-start',
  'padding-inline-end',
  'padding-block',
  'padding-block-start',
  'padding-block-end',
  // The shorthands carry a width, so they animate one.
  'border',
  'border-top',
  'border-right',
  'border-bottom',
  'border-left',
  'border-inline',
  'border-block',
  'border-width',
  'border-top-width',
  'border-right-width',
  'border-bottom-width',
  'border-left-width',
  'border-inline-width',
  'border-inline-start-width',
  'border-inline-end-width',
  'border-block-width',
  'border-block-start-width',
  'border-block-end-width',
  'top',
  'right',
  'bottom',
  'left',
  'inset',
  'inset-inline',
  'inset-inline-start',
  'inset-inline-end',
  'inset-block',
  'inset-block-start',
  'inset-block-end',
  'gap',
  'row-gap',
  'column-gap',
  'grid-gap',
  'font-size',
  'flex',
  'flex-basis',
  'flex-grow',
  'flex-shrink',
  'grid-template-columns',
  'grid-template-rows',
]);

/** Keywords that appear in a `transition` shorthand but are never the property. */
const NOT_A_PROPERTY = new Set([
  'ease',
  'ease-in',
  'ease-out',
  'ease-in-out',
  'linear',
  'step-start',
  'step-end',
  'normal',
  'infinite',
  'alternate',
  'allow-discrete',
]);

/** Whole-value keywords that animate nothing at all. */
const ANIMATES_NOTHING = new Set(['none', 'initial', 'inherit', 'unset', 'revert', 'revert-layer']);

const TIME = /^-?(\d+\.?\d*|\.\d+)(ms|s)$/;
const NUMBER = /^-?(\d+\.?\d*|\.\d+)$/;
const IDENT = /^-?[a-zA-Z][a-zA-Z0-9-]*$/;

/**
 * Blank out comment bodies while keeping every newline, so reported line
 * numbers stay true. `exam.css` line 504 is the comment "No transition, no
 * highlight on change" — without this it would be read as a declaration.
 */
export function stripComments(css) {
  return css.replace(/\/\*[\s\S]*?\*\//g, (match) => match.replace(/[^\n]/g, ' '));
}

/** Split on `sep` at bracket depth 0, so `cubic-bezier(0.16, 1, 0.3, 1)` survives. */
export function splitTopLevel(value, sep) {
  const parts = [];
  let depth = 0;
  let current = '';

  for (const ch of value) {
    if (ch === '(') depth += 1;
    else if (ch === ')') depth -= 1;

    if (ch === sep && depth === 0) {
      parts.push(current);
      current = '';
    } else {
      current += ch;
    }
  }
  parts.push(current);

  return parts.map((p) => p.trim()).filter(Boolean);
}

/**
 * The property one comma-separated slice of a `transition` value animates,
 * or null when it animates nothing. The property is the first token that is
 * not a duration, a delay, a number or an easing — the order inside the
 * shorthand is free, so position alone cannot be trusted.
 */
export function animatedProperty(part) {
  const trimmed = part.trim().toLowerCase();
  if (ANIMATES_NOTHING.has(trimmed)) return null;

  for (const token of splitTopLevel(part, ' ')) {
    const lower = token.toLowerCase();
    if (TIME.test(lower) || NUMBER.test(lower)) continue;
    if (lower.includes('(')) continue; // cubic-bezier(…), steps(…), var(…)
    if (NOT_A_PROPERTY.has(lower)) continue;
    if (!IDENT.test(lower)) continue;
    if (ANIMATES_NOTHING.has(lower)) return null;
    return lower;
  }

  return null;
}

/** Every `transition` / `transition-property` declaration, with its 1-based line. */
export function transitionDeclarations(css) {
  const text = stripComments(css);
  const declarations = [];
  const pattern = /(?:^|[;{}\s])(transition(?:-property)?)\s*:\s*([^;{}]*)/g;

  let match;
  while ((match = pattern.exec(text)) !== null) {
    const line = text.slice(0, match.index + match[0].indexOf(match[1])).split('\n').length;
    const value = match[2].trim();
    const properties = splitTopLevel(value, ',')
      .map(animatedProperty)
      .filter((p) => p !== null);

    declarations.push({ line, declaration: match[1], value, properties });
  }

  return declarations;
}

/** The rule, applied. One entry per offending property, not per declaration. */
export function violations(declarations) {
  const found = [];

  for (const declaration of declarations) {
    for (const property of declaration.properties) {
      if (property === 'all') {
        found.push({
          line: declaration.line,
          declaration: declaration.declaration,
          property,
          reason:
            'transition: all animates whatever happens to differ between two states — ' +
            'name the properties instead',
        });
      } else if (FLOW_PROPERTIES.has(property)) {
        found.push({
          line: declaration.line,
          declaration: declaration.declaration,
          property,
          reason: `${property} is a flow property: animating it lays out the page every frame and moves neighbouring content`,
        });
      }
    }
  }

  return found;
}

// ── Clause 3: endless animation, and its off switch ────────────────────────

/** A media query that asks for less motion. `(prefers-reduced-motion)` bare means the same. */
export function isReducedMotionQuery(prelude) {
  return /\(\s*prefers-reduced-motion\s*(?::\s*reduce\s*)?\)/i.test(prelude);
}

/** Whitespace-collapsed selector text. Class names stay case-sensitive on purpose. */
export function normaliseSelector(selector) {
  return selector.replace(/\s+/g, ' ').trim();
}

/** `prop: value` pairs of one rule body, in source order. */
export function declarationsIn(body) {
  return splitTopLevel(body, ';')
    .map((part) => {
      const colon = part.indexOf(':');
      if (colon < 0) return null;
      return {
        property: part.slice(0, colon).trim().toLowerCase(),
        value: part.slice(colon + 1).trim(),
      };
    })
    .filter((declaration) => declaration !== null && declaration.property !== '');
}

/**
 * Every style rule in the sheet, with its 1-based line, its selectors split
 * apart, and whether a `prefers-reduced-motion` query encloses it.
 *
 * Rules inside `@keyframes` are skipped: `0% { … }` is a stop on a timeline,
 * not a selector, and reading it as one would report `0%` as an offender.
 */
export function parseRules(css) {
  const text = stripComments(css);
  const rules = [];
  const atStack = [];
  let preludeStart = 0;
  let i = 0;

  while (i < text.length) {
    const ch = text[i];

    if (ch === '{') {
      const prelude = text.slice(preludeStart, i);
      const trimmed = prelude.trim();

      if (trimmed.startsWith('@')) {
        atStack.push(trimmed);
        i += 1;
        preludeStart = i;
        continue;
      }

      let depth = 1;
      let j = i + 1;
      while (j < text.length && depth > 0) {
        if (text[j] === '{') depth += 1;
        else if (text[j] === '}') depth -= 1;
        j += 1;
      }

      const insideKeyframes = atStack.some((at) => /^@(?:-[a-z]+-)?keyframes\b/i.test(at));
      if (trimmed !== '' && !insideKeyframes) {
        const offset = preludeStart + (prelude.length - prelude.trimStart().length);
        rules.push({
          line: text.slice(0, offset).split('\n').length,
          selectors: splitTopLevel(trimmed, ',').map(normaliseSelector),
          declarations: declarationsIn(text.slice(i + 1, j - 1)),
          reducedMotion: atStack.some(isReducedMotionQuery),
        });
      }

      i = j;
      preludeStart = i;
      continue;
    }

    if (ch === '}') {
      atStack.pop();
      i += 1;
      preludeStart = i;
      continue;
    }

    if (ch === ';') preludeStart = i + 1;
    i += 1;
  }

  return rules;
}

/** Does this rule start an animation that never ends by itself? */
export function startsEndlessAnimation(rule) {
  return rule.declarations.some(
    ({ property, value }) =>
      (property === 'animation' || property === 'animation-iteration-count') &&
      /(?:^|[\s,])infinite(?:$|[\s,])/i.test(value),
  );
}

/** Does this rule switch animation off outright? `none`, not "fewer iterations". */
export function stopsAnimation(rule) {
  return rule.declarations.some(
    ({ property, value }) =>
      (property === 'animation' || property === 'animation-name') &&
      /(?:^|[\s,])none(?:$|[\s,!])/i.test(value),
  );
}

/**
 * Clause 3, applied. One entry per selector left running, with the two
 * failures told apart — never named, versus named too early to win.
 */
export function endlessAnimationViolations(rules) {
  const offSwitches = rules.filter((rule) => rule.reducedMotion && stopsAnimation(rule));
  const found = [];

  for (const rule of rules) {
    if (rule.reducedMotion || !startsEndlessAnimation(rule)) continue;

    for (const selector of rule.selectors) {
      const named = offSwitches.filter((off) => off.selectors.includes(selector));
      if (named.some((off) => off.line > rule.line)) continue;

      found.push({
        line: rule.line,
        selector,
        reason:
          named.length > 0
            ? `${selector} is switched off under prefers-reduced-motion at line ${named[0].line}, ` +
              'which is ABOVE this rule — identical selectors tie on specificity, so the later ' +
              'one wins and the animation keeps running. Move the opt-out below this rule'
            : `${selector} starts an animation that runs forever and no ` +
              '@media (prefers-reduced-motion: reduce) rule sets animation: none for that exact ' +
              'selector. Endless motion beside text somebody is reading is the case that setting exists for',
      });
    }
  }

  return found;
}

function main() {
  const report = [];
  const motionReport = [];
  let declarationCount = 0;
  let animationCount = 0;
  let endlessCount = 0;

  for (const file of SCANNED_FILES) {
    const absolute = path.join(ROOT, file);
    if (!fs.existsSync(absolute)) {
      console.error(`${file} is in the scan list and does not exist. Fix the list.`);
      process.exit(1);
    }

    const css = fs.readFileSync(absolute, 'utf8');

    const declarations = transitionDeclarations(css);
    declarationCount += declarations.length;
    for (const violation of violations(declarations)) {
      report.push({ file, ...violation });
    }

    const rules = parseRules(css);
    animationCount += rules.filter((rule) =>
      rule.declarations.some(({ property }) => property.startsWith('animation')),
    ).length;
    endlessCount += rules.filter(startsEndlessAnimation).length;
    for (const violation of endlessAnimationViolations(rules)) {
      motionReport.push({ file, ...violation });
    }
  }

  if (declarationCount === 0 || animationCount === 0) {
    // Never pass over an empty set: if the parser or the file list breaks,
    // this check reports success over nothing at all.
    console.error(
      'No transition or no animation declarations were found in any scanned stylesheet.\n' +
        'That is not plausible — fix the parser or the file list rather than trusting this.',
    );
    process.exit(1);
  }

  if (report.length > 0) {
    console.error(`${report.length} transition violation(s):\n`);
    for (const entry of report) {
      console.error(`  · ${entry.file}:${entry.line}  ${entry.declaration ?? 'transition'}`);
      console.error(`      ${entry.property} — ${entry.reason}`);
    }
    console.error(
      '\nFix: list the properties that actually differ between the base rule and its\n' +
        ':hover / :focus / .is-* rules, and leave the flow properties out. The end state\n' +
        'of every rule stays exactly as it is; only the animation set changes.',
    );
  }

  if (motionReport.length > 0) {
    console.error(`${motionReport.length} endless-animation violation(s):\n`);
    for (const entry of motionReport) {
      console.error(`  · ${entry.file}:${entry.line}  ${entry.selector}`);
      console.error(`      ${entry.reason}`);
    }
    console.error(
      '\nFix: add the selector, spelled exactly as it is above, to a\n' +
        '@media (prefers-reduced-motion: reduce) block with `animation: none`, and put that\n' +
        'block BELOW every rule it cancels — identical selectors are decided by source order.',
    );
  }

  if (report.length > 0 || motionReport.length > 0) process.exit(1);

  console.log(
    `  ${declarationCount} transition declaration(s) and ${endlessCount} endless animation(s) ` +
      `across ${SCANNED_FILES.length} stylesheet(s), all enumerated, layout-safe and reducible`,
  );
}

if (process.argv[1] && import.meta.url.endsWith(path.basename(process.argv[1]))) {
  main();
}
