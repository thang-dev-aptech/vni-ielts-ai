#!/usr/bin/env node
//
// A number or a date formatted into a response must not depend on the locale
// of the machine that formatted it.
//
// ── The bug this was written for ───────────────────────────────────────────
//
// `Application/Learning/Handlers.cs` built the Writing detail on
// `GET /api/v1/me/coaching` like this:
//
//     $"Task {n} {m.Band.Value:0.0}"
//
// A format specifier inside a plain interpolated string is applied in
// `CultureInfo.CurrentCulture` — the ambient culture of the *process*, which
// comes from the host. So the same build answered `"Task 1 6,0"` on a
// Vietnamese machine and `"Task 1 6.0"` everywhere else, and the string went
// straight out to the client either way. Nobody saw it for two reasons: every
// developer here runs a `vi-VN` or a US machine, and the one test that touched
// the string reproduced the handler's own formatting in its expectation, so
// the test and the defect agreed.
//
// ── Why this file, and not the .NET analyzer ───────────────────────────────
//
// The obvious answer is CA1305, "specify IFormatProvider". It was measured on
// this solution on 2026-09-18, with `dotnet build -p:AnalysisMode=All` — every
// rule Roslyn ships, not just the default set — and it is the wrong tool here
// for two independent reasons:
//
// 1. **It does not see this bug.** CA1305 flags `ToString`, `Parse` and the
//    `StringBuilder` interpolated-string handler. It does not flag
//    `DefaultInterpolatedStringHandler`, which is what a plain `$"…"`
//    producing a `string` compiles to. Zero diagnostics of any kind — not
//    CA1305, not anything — fired on `Handlers.cs:175` or on the second site
//    this scan found. A gate that is silent on the defect that motivated it is
//    not a gate.
//
// 2. **It is loud about things that are not bugs.** 42 CA1305 hits in
//    `backend/src`, mostly `int.Parse` on digit-only regex captures — safe in
//    every real culture — and `StringBuilder.AppendLine($"## {criterion}")`
//    where every hole is a string. `backend/Directory.Build.props` sets
//    `TreatWarningsAsErrors`, so enabling CA1305 means changing 42 call sites,
//    of which about four matter, before anything compiles again. The signal
//    would arrive buried in its own noise and be suppressed within a week.
//
// So the rule enforced here is narrower than CA1305 and catches what CA1305
// cannot: **a format specifier inside an interpolated string, where C# gives
// you no way to pass a provider.** That is a syntactic fact, checkable without
// type information, and it has exactly one correct fix.
//
// ── The rule ───────────────────────────────────────────────────────────────
//
// A `{expr:format}` hole inside an interpolated string in `backend/src` is a
// finding, unless one of these is true:
//
//   · the format is hexadecimal (`X`, `x`, optionally with a digit count).
//     Hex has no separators, no calendar and no digit substitution.
//   · the format is a round-trip or RFC-1123 form (`o`, `O`, `r`, `R`). Both
//     are defined to ignore the current culture.
//   · the interpolated string is an argument to something that supplies the
//     provider — `string.Create(CultureInfo.InvariantCulture, $"…")` or
//     `FormattableString.Invariant($"…")`. Those are the two fixes, so they
//     must not be findings.
//
// Everything else — `:0.0`, `:N0`, `:F2`, `:yyyy-MM-dd`, `:C` — is flagged.
//
// **`yyyy-MM-dd` is flagged too, and that is deliberate.** The pattern pins the
// separator but not the *calendar*, which is also cultural. Measured on
// .NET 10: under `th-TH` that format writes `2569-09-18` for 18 September 2026,
// and under `ar-SA` reading `"2026-09-18"` back throws. A date is not safe for
// having no comma in it.
//
// ── What this rule deliberately does NOT decide ────────────────────────────
//
// A bare `{someDecimal}` with no specifier is *also* culture-sensitive, and is
// not flagged. Telling `{x}` where `x` is a `decimal` from `{x}` where `x` is a
// `string` needs the type, and this scan has no compiler. Flagging every hole
// would flag several hundred string interpolations and the rule would be turned
// off. CA1305 is the right tool for the typed half of the problem if this
// project ever wants it; this file covers the half CA1305 structurally cannot
// see. Both halves being covered is better than neither, and one is not an
// argument against the other.
//
// ── Known findings left unfixed, and why ───────────────────────────────────
//
// See `WAIVED` below. A waiver names a file and the exact offending text, never
// a line number, and a waiver that no longer matches anything **fails this
// check** rather than lingering — an expired excuse is a lie about the code.
//
// Usage: node scripts/check-culture.mjs

import fs from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '..');

/** Every C# file under here is held to the rule. */
export const SCAN_ROOT = 'backend/src';

/**
 * Findings that are real, reported, and not this commit's to fix.
 *
 * `file` is repo-relative; `text` is the offending source, matched literally.
 * Both must still be true or the check fails: see `staleWaivers`.
 */
export const WAIVED = [
  // Empty, and that is the intended resting state.
  //
  // The one entry this list has ever held — `{limit:N0}` in
  // `AnswerTooLongException` — was waived on 2026-09-18 only because another
  // agent held `ExamHandlers.cs` open that day. It was fixed later the same
  // day by the agent that did hold it, so the waiver is gone rather than
  // inherited: a waiver kept past its reason is how a reported defect becomes
  // a permanent one.
];

/** Format specifiers that mean the same thing in every culture. */
export const CULTURE_FREE = [
  /^[Xx]\d*$/, // hex — no separator, no calendar, no digit substitution
  /^[oO]$/, //   round-trip, defined as invariant
  /^[rR]$/, //   RFC-1123, defined as invariant
];

/** Call shapes that hand the interpolated string a provider of its own. */
const PROVIDER_SUPPLIED =
  /(?:CultureInfo\.InvariantCulture|InvariantCulture)\s*,\s*$|FormattableString\.Invariant\s*\(\s*$/;

/**
 * Blank out comment bodies, keeping every newline so line numbers stay true.
 * Quotes inside comments would otherwise open a string that never closes.
 */
export function stripComments(source) {
  let out = '';
  let i = 0;

  while (i < source.length) {
    const two = source.slice(i, i + 2);

    if (two === '//') {
      while (i < source.length && source[i] !== '\n') {
        out += ' ';
        i += 1;
      }
      continue;
    }

    if (two === '/*') {
      const end = source.indexOf('*/', i + 2);
      const stop = end < 0 ? source.length : end + 2;
      out += source.slice(i, stop).replace(/[^\n]/g, ' ');
      i = stop;
      continue;
    }

    // A string literal may contain "//" — step over it rather than into it.
    const literal = readStringLiteral(source, i);
    if (literal !== null) {
      out += source.slice(i, literal.end);
      i = literal.end;
      continue;
    }

    out += source[i];
    i += 1;
  }

  return out;
}

/**
 * If a string literal starts at `at`, return where its body starts and ends.
 * Handles `"…"`, `@"…"`, `"""…"""` and every `$`-prefixed form of those.
 */
export function readStringLiteral(source, at) {
  const prefix = /^(\$+@?|@\$*)?/.exec(source.slice(at, at + 4))[0] ?? '';
  let i = at + prefix.length;
  if (source[i] !== '"') return null;

  const dollars = (prefix.match(/\$/g) ?? []).length;
  const verbatim = prefix.includes('@');

  let quotes = 0;
  while (source[i + quotes] === '"') quotes += 1;

  // Raw string literal: three or more quotes, closed by as many, no escapes.
  if (quotes >= 3) {
    const fence = '"'.repeat(quotes);
    const bodyStart = i + quotes;
    const close = source.indexOf(fence, bodyStart);
    const bodyEnd = close < 0 ? source.length : close;
    return {
      bodyStart,
      bodyEnd,
      end: close < 0 ? source.length : close + quotes,
      dollars,
      raw: true,
    };
  }

  const bodyStart = i + 1;
  let j = bodyStart;
  while (j < source.length) {
    if (verbatim) {
      if (source[j] === '"') {
        if (source[j + 1] === '"') {
          j += 2;
          continue;
        }
        return { bodyStart, bodyEnd: j, end: j + 1, dollars, raw: false };
      }
      j += 1;
      continue;
    }

    if (source[j] === '\\') {
      j += 2;
      continue;
    }
    if (source[j] === '"') return { bodyStart, bodyEnd: j, end: j + 1, dollars, raw: false };
    if (source[j] === '\n') break; // unterminated — give up rather than run on
    j += 1;
  }

  return { bodyStart, bodyEnd: j, end: j, dollars, raw: false };
}

/**
 * The format specifier of one interpolation hole, or null when it has none.
 *
 * The specifier is whatever follows the last `:` at brace depth zero, outside
 * any nested string. C# requires a conditional inside a hole to be
 * parenthesised precisely so that this `:` is never ambiguous.
 */
export function formatSpecifier(hole) {
  let depth = 0;
  let colon = -1;
  let i = 0;

  while (i < hole.length) {
    const ch = hole[i];

    const literal = readStringLiteral(hole, i);
    if (literal !== null && (ch === '"' || ch === '$' || ch === '@')) {
      i = literal.end;
      continue;
    }

    if (ch === '(' || ch === '[' || ch === '{') depth += 1;
    else if (ch === ')' || ch === ']' || ch === '}') depth -= 1;
    else if (ch === ':' && depth === 0) {
      // `::` is a namespace alias qualifier, never a format specifier.
      if (hole[i + 1] === ':' || hole[i - 1] === ':') {
        i += 2;
        continue;
      }
      colon = i;
    }

    i += 1;
  }

  if (colon < 0) return null;
  return hole.slice(colon + 1);
}

/** Does this specifier mean the same thing under every culture? */
export function isCultureFree(specifier) {
  return CULTURE_FREE.some((pattern) => pattern.test(specifier.trim()));
}

/**
 * Every culture-bound format specifier in one C# source file.
 *
 * Interpolated strings nest, so this walks the whole file once rather than
 * matching a pattern: a `$"…"` inside a hole of another `$"…"` is scanned in
 * its own right, and its provider is decided by the text in front of it.
 */
export function findings(source) {
  const text = stripComments(source);
  const found = [];

  const lineOf = (offset) => text.slice(0, offset).split('\n').length;

  const scan = (from, to, providerSupplied) => {
    let i = from;

    while (i < to) {
      const literal = readStringLiteral(text, i);

      if (literal === null || !'"$@'.includes(text[i])) {
        i += 1;
        continue;
      }

      if (literal.dollars === 0) {
        i = literal.end;
        continue;
      }

      const before = text.slice(Math.max(0, i - 160), i);
      const supplied = providerSupplied || PROVIDER_SUPPLIED.test(before.trimEnd());

      /*
       * How many braces open a hole is the `$` count. A raw literal written
       * `$$"""…"""` — the idiom for embedding JSON, which is why three of them
       * live in this tree — opens its holes with `{{` and treats a single `{`
       * as a literal brace. Reading those as `$"…"` would see `{"title"` as an
       * interpolation and the file as garbage.
       */
      const open = '{'.repeat(literal.dollars);
      const close = '}'.repeat(literal.dollars);

      // Walk the body, collecting holes and recursing into nested literals.
      let j = literal.bodyStart;
      while (j < literal.bodyEnd) {
        if (text.startsWith(open + '{', j)) {
          // One brace past the opener: an escaped literal brace, not a hole.
          j += open.length + 1;
          continue;
        }
        if (text.startsWith(close + '}', j)) {
          j += close.length + 1;
          continue;
        }

        if (!text.startsWith(open, j)) {
          j += 1;
          continue;
        }

        let depth = 1;
        let k = j + open.length;
        while (k < literal.bodyEnd && depth > 0) {
          const nested = readStringLiteral(text, k);
          if (nested !== null && '"$@'.includes(text[k])) {
            k = nested.end;
            continue;
          }
          if (text.startsWith(open, k)) {
            depth += 1;
            k += open.length;
            continue;
          }
          if (text.startsWith(close, k)) {
            depth -= 1;
            k += close.length;
            continue;
          }
          k += 1;
        }

        const hole = text.slice(j + open.length, k - close.length);
        const specifier = formatSpecifier(hole);

        if (specifier !== null && !isCultureFree(specifier) && !supplied) {
          found.push({
            line: lineOf(j),
            specifier: specifier.trim(),
            snippet: `{${hole.trim()}}`,
            reason:
              `the "${specifier.trim()}" specifier is applied in the ambient culture of the ` +
              'process — an interpolated string has no way to be handed a provider',
          });
        }

        scan(j + open.length, k - close.length, supplied);
        j = k;
      }

      i = literal.end;
    }
  };

  scan(0, text.length, false);
  return found;
}

/** Every `.cs` file under `dir`, excluding build output. */
export function sourceFiles(dir) {
  const out = [];

  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'obj' || entry.name === 'bin') continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out.push(...sourceFiles(full));
    else if (entry.name.endsWith('.cs')) out.push(full);
  }

  return out.sort();
}

/** Waivers that no longer match anything. An expired excuse is a lie. */
export function staleWaivers(waivers, matched) {
  return waivers.filter((waiver) => !matched.has(waiver));
}

function main() {
  const root = path.join(ROOT, SCAN_ROOT);
  if (!fs.existsSync(root)) {
    console.error(`${SCAN_ROOT} does not exist. Fix SCAN_ROOT.`);
    process.exit(1);
  }

  const files = sourceFiles(root);
  const report = [];
  const matchedWaivers = new Set();
  let interpolations = 0;

  for (const absolute of files) {
    const file = path.relative(ROOT, absolute);
    const source = fs.readFileSync(absolute, 'utf8');
    interpolations += (stripComments(source).match(/\$"/g) ?? []).length;

    for (const finding of findings(source)) {
      const waiver = WAIVED.find((w) => w.file === file && finding.snippet.includes(w.text));
      if (waiver) {
        matchedWaivers.add(waiver);
        continue;
      }
      report.push({ file, ...finding });
    }
  }

  if (interpolations === 0) {
    // Never pass over an empty set: a broken parser reports success on nothing.
    console.error(
      `No interpolated string was found in any of the ${files.length} file(s) under ${SCAN_ROOT}.\n` +
        'That is not plausible — fix the scanner or the scan root rather than trusting this.',
    );
    process.exit(1);
  }

  const stale = staleWaivers(WAIVED, matchedWaivers);
  if (stale.length > 0) {
    console.error(`${stale.length} waiver(s) in check-culture.mjs no longer match anything:\n`);
    for (const waiver of stale) console.error(`  · ${waiver.file}  ${waiver.text}`);
    console.error('\nThe finding was fixed or the code moved. Delete the waiver.');
  }

  if (report.length > 0) {
    console.error(`${report.length} culture-dependent format specifier(s):\n`);
    for (const entry of report) {
      console.error(`  · ${entry.file}:${entry.line}`);
      console.error(`      ${entry.snippet}`);
      console.error(`      ${entry.reason}`);
    }
    console.error(
      '\nFix, in order of preference:\n' +
        '  1. let the value object format itself, if it has an invariant ToString() — BandScore does;\n' +
        '  2. string.Create(CultureInfo.InvariantCulture, $"…") to pin a whole interpolation;\n' +
        '  3. value.ToString("0.0", CultureInfo.InvariantCulture) to pin one call.\n' +
        'The output stays exactly what it is on an en-US machine; only the dependence on the host goes.',
    );
  }

  if (report.length > 0 || stale.length > 0) process.exit(1);

  console.log(
    `  ${interpolations} interpolated string(s) across ${files.length} file(s) under ${SCAN_ROOT}: ` +
      `no unpinned format specifier${WAIVED.length > 0 ? `, ${WAIVED.length} waived and still true` : ''}`,
  );
}

if (process.argv[1] && import.meta.url.endsWith(path.basename(process.argv[1]))) {
  main();
}
