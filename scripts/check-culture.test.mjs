// Unit tests for scripts/check-culture.mjs.
//
// The scanner is a small C# lexer, and a lexer that is subtly wrong is worse
// than no lexer: it reports "no findings" over a file it failed to read. Most
// of what is pinned below is therefore about *not* being fooled — braces that
// are not holes, colons that are not format specifiers, quotes inside comments.

import { test } from 'node:test';
import assert from 'node:assert/strict';

import {
  CULTURE_FREE,
  findings,
  formatSpecifier,
  isCultureFree,
  readStringLiteral,
  staleWaivers,
  stripComments,
  WAIVED,
} from './check-culture.mjs';

const lines = (source) => findings(source).map((f) => `${f.line}:${f.snippet}`);

// ── The bug this gate exists for ───────────────────────────────────────────

test('the reported defect is a finding', () => {
  const source =
    'var t = m.TaskNumber is { } n ? $"Task {n} {m.Band.Value:0.0}" : $"{m.Band.Value:0.0}";';
  assert.deepEqual(lines(source), ['1:{m.Band.Value:0.0}', '1:{m.Band.Value:0.0}']);
});

test('the three accepted fixes are not findings', () => {
  assert.deepEqual(
    findings('var t = string.Create(CultureInfo.InvariantCulture, $"Task {n} {b:0.0}");'),
    [],
  );
  assert.deepEqual(findings('var t = FormattableString.Invariant($"{b:0.0}");'), []);
  assert.deepEqual(findings('var t = $"{b.ToString("0.0", CultureInfo.InvariantCulture)}";'), []);
  // A value object that formats itself has no specifier to pin.
  assert.deepEqual(findings('var t = $"Task {n} {m.Band}";'), []);
});

// ── Which specifiers are culture-bound ─────────────────────────────────────

test('numeric and date specifiers are culture-bound', () => {
  for (const spec of ['0.0', 'N0', 'F2', 'C', 'P1', 'yyyy-MM-dd', '+0.0;-0.0;0.0', 'd']) {
    assert.equal(isCultureFree(spec), false, spec);
    assert.deepEqual(lines(`var t = $"{x:${spec}}";`), [`1:{x:${spec}}`], spec);
  }
});

test('hex and round-trip specifiers are culture-free', () => {
  for (const spec of ['X', 'x', 'X8', 'x2', 'o', 'O', 'r', 'R']) {
    assert.equal(isCultureFree(spec), true, spec);
    assert.deepEqual(findings(`var t = $"{x:${spec}}";`), [], spec);
  }
});

test('yyyy-MM-dd is flagged although it names no separator', () => {
  // th-TH writes 2569 for 2026 and ar-SA cannot read it back. The calendar is
  // cultural even when the pattern is not.
  assert.deepEqual(lines('var t = $"{day:yyyy-MM-dd}";'), ['1:{day:yyyy-MM-dd}']);
});

test('the culture-free list is anchored and cannot match a longer specifier', () => {
  // A sloppy /[Xx]\d*/ would clear "X8 and then yyyy" — anchors matter.
  assert.equal(
    CULTURE_FREE.every((p) => p.source.startsWith('^') && p.source.endsWith('$')),
    true,
  );
  assert.equal(isCultureFree('Xyyyy'), false);
  assert.equal(isCultureFree('ooo'), false);
});

// ── Not being fooled ───────────────────────────────────────────────────────

test('a hole with no specifier is not a finding', () => {
  assert.deepEqual(findings('var t = $"{module}={band}";'), []);
});

test('an escaped brace is not a hole', () => {
  assert.deepEqual(findings('var t = $"{{not:a hole}} {x}";'), []);
});

test('a non-interpolated string is never scanned', () => {
  assert.deepEqual(findings('var t = "{x:0.0}";'), []);
  assert.deepEqual(findings('var t = @"{x:0.0}";'), []);
});

test('a colon inside a nested call or index is not a specifier', () => {
  assert.deepEqual(findings('var t = $"{dict[a:b]}";'), []);
  assert.deepEqual(findings('var t = $"{Foo((a ? b : c))}";'), []);
});

test('an alias qualifier is not a specifier', () => {
  assert.deepEqual(findings('var t = $"{global::System.Int32.MaxValue}";'), []);
});

test('a nested interpolated string is scanned in its own right', () => {
  assert.deepEqual(lines('var t = $"{(b ? $"{x:0.0}" : "-")}";'), ['1:{x:0.0}']);
});

test('a nested interpolated string inherits an outer provider', () => {
  assert.deepEqual(
    findings('var t = string.Create(CultureInfo.InvariantCulture, $"{(b ? $"{x:0.0}" : "-")}");'),
    [],
  );
});

test('a $$ raw literal opens its holes with two braces', () => {
  const source = 'var t = $$"""{"title":"x","n":{{count}},"b":{{band:0.0}}}""";';
  // `{"title"` is literal JSON, `{{band:0.0}}` is the only hole with a format.
  assert.deepEqual(lines(source), ['1:{band:0.0}']);
});

test('a plain raw literal still opens its holes with one brace', () => {
  assert.deepEqual(lines('var t = $"""{x:0.0}""";'), ['1:{x:0.0}']);
});

// ── Comment and literal handling ───────────────────────────────────────────

test('comments are blanked and line numbers survive it', () => {
  const source = [
    '// $"{x:0.0}" in a line comment',
    '/* $"{y:N0}" in a block */',
    'var t = $"{z:0.0}";',
  ].join('\n');
  assert.deepEqual(lines(source), ['3:{z:0.0}']);
});

test('a quote inside a comment does not open a string', () => {
  const source = ['// it\'s fine, "really', 'var t = $"{z:0.0}";'].join('\n');
  assert.deepEqual(lines(source), ['2:{z:0.0}']);
});

test('a // inside a string literal is not a comment', () => {
  const source = 'var url = "https://x/y"; var t = $"{z:0.0}";';
  assert.deepEqual(lines(source), ['1:{z:0.0}']);
});

test('stripComments keeps the character count so offsets stay true', () => {
  const source = 'a /* bb */ c';
  assert.equal(stripComments(source).length, source.length);
  assert.equal(stripComments(source), 'a          c');
});

test('an escaped quote does not end a literal', () => {
  const literal = readStringLiteral('"a\\"b"', 0);
  assert.equal(literal.end, 6);
});

test('a verbatim literal ends on a single quote, not a doubled one', () => {
  const literal = readStringLiteral('@"a""b"', 0);
  assert.equal(literal.end, 7);
});

// ── formatSpecifier in isolation ───────────────────────────────────────────

test('formatSpecifier takes the last top-level colon', () => {
  assert.equal(formatSpecifier('x'), null);
  assert.equal(formatSpecifier('x:0.0'), '0.0');
  assert.equal(formatSpecifier('x,10:0.0'), '0.0');
  assert.equal(formatSpecifier('f(a:b)'), null);
});

// ── The waiver list ────────────────────────────────────────────────────────

test('every waiver explains itself', () => {
  for (const waiver of WAIVED) {
    assert.ok(waiver.file.startsWith('backend/src'), waiver.file);
    assert.ok(waiver.text.length > 0, waiver.file);
    assert.ok(waiver.why.length > 40, `${waiver.file}: a waiver needs a reason, not a word`);
  }
});

test('a waiver that matched nothing is reported stale', () => {
  const live = { file: 'a', text: 'b', why: 'c' };
  const dead = { file: 'd', text: 'e', why: 'f' };
  assert.deepEqual(staleWaivers([live, dead], new Set([live])), [dead]);
});
