// F5 — run: node --test scripts/check-test-skips.test.mjs
//
// The gate this covers failed on CI with `error: Unknown argument:
// _artifacts/verify/test-results`, because the parser understood only
// `--flag=value` while both this script's own usage block and
// `.github/workflows/verify.yml` call it with `--flag value`. That is the
// worst shape of CI bug: the gate did not run, and the reason it did not run
// looked like a gate finding.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { parseArgs } from './check-test-skips.mjs';

test('--flag=value, the form scripts/verify.mjs uses', () => {
  const args = parseArgs(['--results=_artifacts/verify/test-results']);
  assert.deepEqual(args.results, ['_artifacts/verify/test-results']);
});

test('--flag value, the form verify.yml uses — this is the CI failure', () => {
  const args = parseArgs(['--results', '_artifacts/verify/test-results']);
  assert.deepEqual(args.results, ['_artifacts/verify/test-results']);
});

test('the two forms mix in one command line', () => {
  const args = parseArgs([
    '--results',
    'a',
    '--results=b',
    '--allowlist',
    'ci/test-skip-allowlist.json',
    '--json=_artifacts/skips.json',
    '--require-results',
  ]);

  assert.deepEqual(args.results, ['a', 'b']);
  assert.equal(args.allowlist, 'ci/test-skip-allowlist.json');
  assert.equal(args.json, '_artifacts/skips.json');
  assert.equal(args.requireResults, true);
});

test('a value-taking flag with no value is rejected, not silently empty', () => {
  // `--results=` used to push '' — which makes `results.length === 0` false,
  // so the default directory is never substituted, the walk finds nothing and
  // the gate passes over zero files. A gate that reports clean because it read
  // nothing is worse than one that fails.
  assert.throws(() => parseArgs(['--results=']), /--results needs a value/);
  assert.throws(() => parseArgs(['--results']), /--results needs a value/);
});

test('a following flag is not eaten as a value', () => {
  assert.throws(() => parseArgs(['--results', '--require-results']), /--results needs a value/);
});

test('--now parses as a date, in both forms', () => {
  assert.equal(parseArgs(['--now=2026-01-02']).now.toISOString().slice(0, 10), '2026-01-02');
  assert.equal(parseArgs(['--now', '2026-01-02']).now.toISOString().slice(0, 10), '2026-01-02');
});

test('an unknown flag is still an error', () => {
  assert.throws(() => parseArgs(['--nope']), /Unknown argument: --nope/);
});

test('no --results falls back to the pipeline default', () => {
  const args = parseArgs([]);
  assert.equal(args.results.length, 1);
  assert.match(args.results[0].replace(/\\/g, '/'), /_artifacts\/verify\/test-results$/);
});

test('the command line verify.yml actually runs is accepted end to end', async () => {
  // The parser unit tests above would pass even if the workflow had been
  // changed to a form the parser happens to like. This runs the real script
  // with the real argv from .github/workflows/verify.yml and asserts it does
  // not die on argument parsing.
  const { execFileSync } = await import('node:child_process');
  const { readFileSync } = await import('node:fs');
  const path = (await import('node:path')).default;

  const root = path.join(import.meta.dirname, '..');
  const workflow = readFileSync(path.join(root, '.github/workflows/verify.yml'), 'utf8');

  const invocation = workflow.match(/node scripts\/check-test-skips\.mjs([^\n]*)/);
  assert.ok(invocation, 'verify.yml no longer calls check-test-skips.mjs');

  const argv = invocation[1].trim().split(/\s+/).filter(Boolean);

  let output = '';
  try {
    output = execFileSync('node', ['scripts/check-test-skips.mjs', ...argv], {
      cwd: root,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
    });
  } catch (error) {
    // A missing results directory is a legitimate outcome here — this test is
    // about argument handling, not about whether a pipeline has run. An
    // argument error is not.
    output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
    assert.doesNotMatch(output, /Unknown argument|needs a value/, output);
  }
});
