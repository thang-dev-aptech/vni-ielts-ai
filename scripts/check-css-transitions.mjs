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
// Scope is the three files below and only those. Other stylesheets hold
// violations too (`auth.css` had two `transition: all` when this was written);
// widening the scan here would fail `pnpm check` for work that belongs to
// whoever owns those screens. Add a file when its violations are fixed in the
// same commit.
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

function main() {
  const report = [];
  let declarationCount = 0;

  for (const file of SCANNED_FILES) {
    const absolute = path.join(ROOT, file);
    if (!fs.existsSync(absolute)) {
      console.error(`${file} is in the scan list and does not exist. Fix the list.`);
      process.exit(1);
    }

    const declarations = transitionDeclarations(fs.readFileSync(absolute, 'utf8'));
    declarationCount += declarations.length;

    for (const violation of violations(declarations)) {
      report.push({ file, ...violation });
    }
  }

  if (declarationCount === 0) {
    // Never pass over an empty set: if the parser or the file list breaks,
    // this check reports success over nothing at all.
    console.error(
      'No transition declarations were found in any scanned stylesheet.\n' +
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
    process.exit(1);
  }

  console.log(
    `  ${declarationCount} transition declaration(s) across ${SCANNED_FILES.length} stylesheet(s), all enumerated and layout-safe`,
  );
}

if (process.argv[1] && import.meta.url.endsWith(path.basename(process.argv[1]))) {
  main();
}
