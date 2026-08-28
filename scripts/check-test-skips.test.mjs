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
import { parseArgs, parseTrx } from './check-test-skips.mjs';

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

// ── The TRX summary lies, and this gate used to believe it ──────────────────
//
// Real shape, taken from the CI artifact of run 33193503434: `<Counters>` says
// notExecuted="0" while three `<UnitTestResult>` entries in the same document
// say outcome="NotExecuted", and `dotnet test` printed `Skipped: 3` for it.
const LYING_TRX = `<?xml version="1.0" encoding="UTF-8"?>
<TestRun>
  <Results>
    <UnitTestResult testName="Suite.Passes" outcome="Passed" />
    <UnitTestResult testName="Storage.S3ObjectStoreTests.A_missing_key_in_a_real_bucket_returns_null" duration="00:00:00.001" outcome="NotExecuted" />
    <UnitTestResult outcome="NotExecuted" testName="Storage.S3ObjectStoreTests.Wrong_credentials_throw" />
  </Results>
  <ResultSummary outcome="Completed">
    <Counters total="67" executed="64" passed="64" failed="0" notExecuted="0" />
  </ResultSummary>
</TestRun>`;

test('a skip is reported even when Counters claims notExecuted="0"', () => {
  // The exact defect: `if (notExecuted > 0)` guarded the per-result scan, so a
  // zero in the summary meant the three real skips below it were never looked
  // for. Six object-storage tests went unreported on every CI build this way.
  const { skipped, total } = parseTrx('ci.trx', LYING_TRX);

  assert.equal(total, 67);
  assert.deepEqual(
    skipped.map((s) => s.name).sort(),
    [
      'Storage.S3ObjectStoreTests.A_missing_key_in_a_real_bucket_returns_null',
      'Storage.S3ObjectStoreTests.Wrong_credentials_throw',
    ],
    'a skip named in the body must be reported however the summary counts it',
  );
});

test('both attribute orders are recognised, and neither is double-counted', () => {
  // testName-before-outcome and outcome-before-testName are both present in the
  // fixture above; two regexes read them and a Set has to reconcile the two.
  const names = parseTrx('ci.trx', LYING_TRX).skipped.map((s) => s.name);
  assert.equal(new Set(names).size, names.length, 'a skip was counted twice');
});

test('a passing run stays clean — the gate is not simply always red', () => {
  const clean = `<TestRun><Results>
    <UnitTestResult testName="Suite.Passes" outcome="Passed" />
  </Results><ResultSummary><Counters total="1" executed="1" passed="1" notExecuted="0" /></ResultSummary></TestRun>`;

  assert.deepEqual(parseTrx('clean.trx', clean).skipped, []);
});

test('a count with no names is still raised, not dropped', () => {
  // The opposite direction: the summary sees a skip the body does not name.
  // Unactionable, but silence would be worse.
  const countOnly = `<TestRun><Results>
    <UnitTestResult testName="Suite.Passes" outcome="Passed" />
  </Results><ResultSummary><Counters total="3" executed="2" passed="2" notExecuted="1" /></ResultSummary></TestRun>`;

  const { skipped } = parseTrx('count.trx', countOnly);
  assert.equal(skipped.length, 1);
  assert.match(skipped[0].name, /unnamed skip/);
});

test('names win over a smaller count rather than being truncated to it', () => {
  const under = `<TestRun><Results>
    <UnitTestResult testName="A" outcome="NotExecuted" />
    <UnitTestResult testName="B" outcome="NotExecuted" />
  </Results><ResultSummary><Counters total="2" executed="0" notExecuted="1" /></ResultSummary></TestRun>`;

  assert.deepEqual(
    parseTrx('under.trx', under).skipped.map((s) => s.name),
    ['A', 'B'],
  );
});
