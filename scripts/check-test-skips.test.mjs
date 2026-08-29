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

// ── FS0.6 · Gate integrity ──────────────────────────────────────────────────
//
// Two ways this gate reported success without having tested anything, both
// found by the FS0.5 baseline and both fixed here. The tests below are the
// negative proofs: each one goes red against the behaviour that shipped.
//
// Defect 1. `verify.mjs` captured Playwright's `--reporter=json` by scraping
// the child's stdout. pnpm writes `WARN Unsupported engine` to stdout on a host
// whose Node is out of `engines` range, so line 1 of the produced
// `playwright.json` was not JSON. `parseFile()` caught the parse error and
// returned `null` with no warning, so the entire Playwright result set was
// dropped in silence: the gate saw 6 files / 585 tests where the run had
// produced 7 / 592. A gate that exists so a `test.fixme` cannot pass silently
// did exactly that, and it failed OPEN.
//
// Defect 2. `pnpm test:api` ran `dotnet test backend` without
// `VNI_REQUIRE_MONGO` / `VNI_REQUIRE_MINIO`, the two variables that turn "no
// database" from a skipped suite into a failed run. With no Mongo up, all 164
// `Skip.IfNot` sites skipped and `pnpm check` reported success over a backend
// suite that tested nothing.

import { mkdtempSync, writeFileSync, rmSync, readFileSync as read } from 'node:fs';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import nodePath from 'node:path';

const REPO = nodePath.join(import.meta.dirname, '..');
const GATE = nodePath.join(REPO, 'scripts', 'check-test-skips.mjs');

function runGate(argv) {
  const result = spawnSync(process.execPath, [GATE, ...argv], { cwd: REPO, encoding: 'utf8' });
  return { code: result.status, output: `${result.stdout ?? ''}${result.stderr ?? ''}` };
}

const scratch = () => mkdtempSync(nodePath.join(tmpdir(), 'vni-skips-'));

// One passing .NET result, so the directory is never empty. Without it,
// `--require-results` would fail the run for a different reason and the proofs
// below would prove nothing.
const PASSING_TRX = `<?xml version="1.0" encoding="UTF-8"?>
<TestRun><Results>
  <UnitTestResult testName="Suite.Passes" outcome="Passed" />
</Results><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary></TestRun>`;

const PLAYWRIGHT_REPORT = {
  config: { rootDir: '/repo/e2e' },
  suites: [
    {
      title: 'smoke.spec.ts',
      specs: [
        { title: 'a learner can sign in', tests: [{ results: [{ status: 'passed' }] }] },
        {
          title: 'the timer is server-authoritative',
          tests: [{ results: [{ status: 'passed' }] }],
        },
      ],
    },
  ],
  errors: [],
  stats: { expected: 2, skipped: 0, unexpected: 0, flaky: 0 },
};

// Verbatim shape of the corruption, from the FS0.5 baseline run on this host.
const PNPM_ENGINE_WARNING =
  '.                                        |  WARN  Unsupported engine: wanted: {"node":">=24.0.0"} (current: {"node":"v22.22.2","pnpm":"10.15.0"})';

test('NEGATIVE PROOF 1 — a report whose first line is not JSON fails the gate, loudly', () => {
  // This is the file `verify.mjs` actually produced on 2026-08-29: a pnpm
  // warning, then a perfectly good Playwright report. The old gate answered
  // "OK — no unauthorized test skips" over it.
  const dir = scratch();
  try {
    writeFileSync(nodePath.join(dir, 'backend.trx'), PASSING_TRX);
    writeFileSync(
      nodePath.join(dir, 'playwright.json'),
      `${PNPM_ENGINE_WARNING}\n${JSON.stringify(PLAYWRIGHT_REPORT, null, 2)}\n`,
    );

    const { code, output } = runGate([`--results=${dir}`, '--require-results']);

    assert.equal(code, 1, `expected a non-zero exit, got ${code}.\n${output}`);
    assert.match(output, /playwright\.json/, 'the failure must name the file it could not read');
    assert.match(output, /could not be parsed as JSON/i, output);
    assert.doesNotMatch(output, /OK — no unauthorized test skips/, output);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('NEGATIVE PROOF 1b — a report that was expected and is missing fails the gate', () => {
  // The other half of the same hole. If Playwright never wrote its report —
  // a misconfigured reporter, or a stage that died before `onEnd` — the
  // directory simply holds fewer files, and a gate that counts what it finds
  // cannot tell that apart from a clean run.
  const dir = scratch();
  try {
    writeFileSync(nodePath.join(dir, 'backend.trx'), PASSING_TRX);

    const missing = nodePath.join(dir, 'playwright.json');
    const { code, output } = runGate([
      `--results=${dir}`,
      '--require-results',
      `--expect-report=${missing}`,
    ]);

    assert.doesNotMatch(
      output,
      /Unknown argument/,
      `--expect-report must be a real flag.\n${output}`,
    );
    assert.equal(code, 1, `expected a non-zero exit, got ${code}.\n${output}`);
    assert.match(output, /Expected test report is missing/, output);
    assert.match(output, /playwright\.json/, 'the failure must name the file');
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('the gate is not simply always red — a clean report set passes and is counted', () => {
  const dir = scratch();
  try {
    writeFileSync(nodePath.join(dir, 'backend.trx'), PASSING_TRX);
    const report = nodePath.join(dir, 'playwright.json');
    writeFileSync(report, `${JSON.stringify(PLAYWRIGHT_REPORT, null, 2)}\n`);

    const { code, output } = runGate([
      `--results=${dir}`,
      '--require-results',
      `--expect-report=${report}`,
    ]);

    assert.equal(code, 0, output);
    assert.match(output, /Result files: 2/, output);
    assert.match(output, /tests counted: 3/, output);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('a Playwright test.fixme in a file-written report is still caught', () => {
  // The whole reason the Playwright report is read at all. With the report
  // written straight to a file there is no stdout to corrupt it, so this
  // assertion now means something.
  const dir = scratch();
  try {
    writeFileSync(nodePath.join(dir, 'backend.trx'), PASSING_TRX);
    writeFileSync(
      nodePath.join(dir, 'playwright.json'),
      JSON.stringify({
        config: {},
        suites: [
          {
            title: 'offline.spec.ts',
            specs: [
              {
                title: 'a queued answer survives a reconnect',
                tests: [{ annotations: [{ type: 'fixme' }], results: [{ status: 'skipped' }] }],
              },
            ],
          },
        ],
        errors: [],
      }),
    );

    const { code, output } = runGate([`--results=${dir}`, '--require-results']);

    assert.equal(code, 1, output);
    assert.match(output, /Skipped test with no exemption/, output);
    assert.match(output, /a queued answer survives a reconnect/, output);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

test('JSON in the results directory that is no report at all is a failure, not a shrug', () => {
  const dir = scratch();
  try {
    writeFileSync(nodePath.join(dir, 'backend.trx'), PASSING_TRX);
    writeFileSync(nodePath.join(dir, 'notes.json'), JSON.stringify({ hello: 'world' }));

    const { code, output } = runGate([`--results=${dir}`, '--require-results']);

    assert.equal(code, 1, output);
    assert.match(output, /notes\.json/, output);
    assert.match(output, /neither a vitest nor a Playwright report/i, output);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ── Defect 2 ────────────────────────────────────────────────────────────────

test('NEGATIVE PROOF 2 — a dotnet test script without the dependency flags is a problem', async () => {
  const { dotnetTestScriptProblems } = await import('./check-test-skips.mjs');

  const problems = dotnetTestScriptProblems({
    scripts: { 'test:api': 'dotnet test backend --nologo' },
  });

  assert.equal(problems.length, 2, JSON.stringify(problems, null, 2));
  assert.match(problems.join('\n'), /VNI_REQUIRE_MONGO/);
  assert.match(problems.join('\n'), /VNI_REQUIRE_MINIO/);
});

test('NEGATIVE PROOF 2 — this repository owns a package.json that declares them', async () => {
  // The proof that goes red the moment somebody takes the flags back out of
  // `test:api`: without them `pnpm check` runs `dotnet test backend`, every
  // `Skip.IfNot` site skips for want of Mongo, and the suite reports success
  // over 164 tests that did not run.
  const { dotnetTestScriptProblems } = await import('./check-test-skips.mjs');
  const pkg = JSON.parse(read(nodePath.join(REPO, 'package.json'), 'utf8'));

  assert.deepEqual(dotnetTestScriptProblems(pkg), []);
});

test('NEGATIVE PROOF 2 — the running gate enforces it, not only the exported helper', () => {
  const dir = scratch();
  try {
    writeFileSync(nodePath.join(dir, 'backend.trx'), PASSING_TRX);
    const { code, output } = runGate([`--results=${dir}`, '--require-results']);
    assert.equal(code, 0, `the real package.json must satisfy the static check.\n${output}`);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});

// ── The mechanism, not the symptom ──────────────────────────────────────────

test('verify.mjs has Playwright write its report to a file rather than scraping stdout', () => {
  // Defect 1's root cause is the capture mechanism, not the parser: any tool
  // that prints a line to stdout before the JSON corrupts a scraped report.
  // `PLAYWRIGHT_JSON_OUTPUT_FILE` makes the reporter write the file itself, so
  // there is no stdout for anything to corrupt.
  const source = read(nodePath.join(REPO, 'scripts', 'verify.mjs'), 'utf8');

  assert.match(
    source,
    /PLAYWRIGHT_JSON_OUTPUT_FILE/,
    'the e2e stage must let Playwright write its own JSON report',
  );
  assert.doesNotMatch(
    source,
    /stdoutTo/,
    'stdout scraping is how a pnpm warning ended up as line 1 of playwright.json',
  );
});

test('verify.mjs tells the skip gate which reports it must find', () => {
  const source = read(nodePath.join(REPO, 'scripts', 'verify.mjs'), 'utf8');
  assert.match(source, /--expect-report=/, 'a report that never appeared must fail the gate');
});

test('verify.mjs runs this self-test as a stage', () => {
  // These tests defend the pipeline only if the pipeline runs them. Until
  // FS0.6 nothing did: no verify stage, no pnpm script and no workflow
  // invoked check-test-skips.test.mjs at all.
  const source = read(nodePath.join(REPO, 'scripts', 'verify.mjs'), 'utf8');
  assert.match(source, /scripts\/check-test-skips\.test\.mjs/);
});
