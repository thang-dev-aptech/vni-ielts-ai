// F5.4 — the security drill must account for every scanner independently.
// Run: node --test scripts/failure-drills.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import {
  DEFAULT_DRILL_TIMEOUT_MS,
  DRILLS,
  evaluateSecurityFixtureOutput,
  executeDrill,
  SECURITY_FIXTURE_PROBES,
} from './failure-drills.mjs';

const FIXTURES = path.join(import.meta.dirname, '..', 'fixtures', 'security', 'failure-drill');
const fixture = (name) => readFileSync(path.join(FIXTURES, name), 'utf8');
const PROBE = path.join(FIXTURES, 'probe-command.mjs');
const DRILL = path.join(import.meta.dirname, 'security-fixture-drill.mjs');

function runFixtureDrill(codeqlMode) {
  const command = (mode) => JSON.stringify([process.execPath, PROBE, mode]);
  return spawnSync(process.execPath, [DRILL], {
    encoding: 'utf8',
    env: {
      ...process.env,
      VNI_SECURITY_FIXTURE_ALLOW_COMMAND_OVERRIDES: '1',
      VNI_SECURITY_FIXTURE_SECRET_COMMAND: command('nonzero'),
      VNI_SECURITY_FIXTURE_DEPENDENCY_COMMAND: command('nonzero'),
      VNI_SECURITY_FIXTURE_IMAGE_COMMAND: command('nonzero'),
      VNI_SECURITY_FIXTURE_CODEQL_COMMAND: command(codeqlMode),
    },
  });
}

test('the security fixture contract names all four independent probes', () => {
  assert.deepEqual(SECURITY_FIXTURE_PROBES, {
    secret: 'nonzero',
    dependency: 'nonzero',
    image: 'nonzero',
    codeql: 'zero',
  });
});

test('all three rejected fixtures and the reproduced CodeQL finding satisfy the drill', () => {
  const result = evaluateSecurityFixtureOutput(fixture('all-four-observed.txt'));

  assert.equal(result.satisfied, true);
  assert.equal(result.reason, null);
  assert.equal(result.probes.codeql.matched, true);
});

test('negative proof: an unexpectedly clean CodeQL result fails the drill', () => {
  const result = evaluateSecurityFixtureOutput(fixture('codeql-clean.txt'));

  assert.equal(result.satisfied, false);
  assert.match(result.reason, /codeql: expected zero, got exit 1/);
});

test('negative proof: omitting CodeQL cannot be reported as three scanners passing', () => {
  const result = evaluateSecurityFixtureOutput(fixture('codeql-missing.txt'));

  assert.equal(result.satisfied, false);
  assert.match(result.reason, /codeql: missing integer exitCode or non-empty command/);
});

test('prose and a forged marker are not accepted as scanner evidence', () => {
  assert.equal(
    evaluateSecurityFixtureOutput('secret dependency image codeql all passed').satisfied,
    false,
  );
  assert.equal(
    evaluateSecurityFixtureOutput('VNI_SECURITY_FIXTURE_RESULTS=not-json').satisfied,
    false,
  );
});

test('duplicate records are rejected so stale output cannot mask the last probe run', () => {
  const record = fixture('all-four-observed.txt')
    .split('\n')
    .find((line) => line.startsWith('VNI_'));
  const result = evaluateSecurityFixtureOutput(`${record}\n${record}\n`);

  assert.equal(result.satisfied, false);
  assert.match(result.reason, /exactly one/);
});

test('the dedicated runner emits a complete record after executing four commands', () => {
  const run = runFixtureDrill('zero');
  const result = evaluateSecurityFixtureOutput(run.stdout);

  assert.equal(run.status, 0, run.stderr);
  assert.equal(result.satisfied, true);
});

test('negative proof: the dedicated runner is non-zero when CodeQL stops finding the fixture', () => {
  const run = runFixtureDrill('nonzero');
  const result = evaluateSecurityFixtureOutput(run.stdout);

  assert.equal(run.status, 1, run.stderr);
  assert.equal(result.satisfied, false);
  assert.match(result.reason, /codeql: expected zero/);
});

test('an unavailable scanner is BLOCKED rather than treated as an observed finding', () => {
  const output =
    'VNI_SECURITY_FIXTURE_RESULTS=' +
    JSON.stringify({
      secret: { command: 'gitleaks fixture', exitCode: 1 },
      dependency: { command: 'pnpm audit fixture', exitCode: 1 },
      image: { command: 'trivy fixture', exitCode: 1 },
      codeql: {
        command: 'codeql test run fixture',
        exitCode: null,
        status: 'blocked',
        reason: 'required command unavailable: codeql',
      },
    });
  const result = evaluateSecurityFixtureOutput(output);

  assert.equal(result.satisfied, false);
  assert.equal(result.status, 'blocked');
  assert.match(result.reason, /required command unavailable: codeql/);
});

test('the hosted fixture invokes the CodeQL binary exposed by the init action', () => {
  const workflow = readFileSync(
    path.join(import.meta.dirname, '..', '.github', 'workflows', 'security.yml'),
    'utf8',
  );

  assert.match(workflow, /id: codeql-init/);
  assert.match(
    workflow,
    /steps\.codeql-init\.outputs\.codeql-path \}\}" test run fixtures\/security\/codeql/,
  );
  assert.doesNotMatch(workflow, /^\s*codeql test run fixtures\/security\/codeql/m);
});

test('the committed CodeQL tuple matches the hosted 2.26.4 result shape', () => {
  const expected = readFileSync(
    path.join(
      import.meta.dirname,
      '..',
      'fixtures',
      'security',
      'codeql',
      'IntentionalCodeInjection.expected',
    ),
    'utf8',
  );

  assert.equal(
    expected.trim(),
    '| IntentionalCodeInjection.js:3:10:3:29 | eval(userControlled) | Intentional CodeQL failure-drill finding. |',
  );
});

// ── W0 · a drill needs a deadline of its own ──────────────────────────────
//
// Measured 2026-09-18: `production-config-live` ran 05:46:07 → 06:34:26 and was
// killed by the job's `timeout-minutes: 60`. Because the *job* hit the wall and
// not the drill, the run was cancelled — no summary, no uploaded artifact, and
// a log whose only statement was that the pipeline had run out of time.
//
// The trap these tests exist to hold shut is narrower than "it should stop".
// `production-config-live` is INVERTED: it passes on a non-zero exit. A killed
// process has no exit code at all, and the harness used to substitute 1 for a
// missing one — so the moment a deadline started killing the hung container,
// the drill would have reported the required failure it never produced.

const HANG_MS = 45_000;
const neverExits = [process.execPath, '-e', `setTimeout(() => {}, ${HANG_MS})`];
const quiet = { log: () => {} };

const fakeDrill = (overrides) => ({
  id: 'fake-drill',
  title: 'a fixture that exists only for this test',
  requires: 'nothing — it is a fixture',
  checklistItem: 'test-only',
  ...overrides,
});

test("a drill whose command never exits is killed at its own deadline, not the job's", () => {
  const drill = fakeDrill({ argv: neverExits, expect: 'nonzero', timeoutMs: 2_000 });

  const record = executeDrill(drill, drill.argv, quiet);

  assert.equal(record.timedOut, true);
  assert.equal(record.status, 'failed');
  assert.match(record.reason, /did not exit within/);
  // 45s is what the command would have taken on its own. Anything near it means
  // the deadline was not enforced; the DoD asks for well under two minutes.
  assert.ok(record.durationMs < 20_000, `killed after ${record.durationMs}ms`);
});

test('negative proof: a command that really does exit non-zero still satisfies an inverted drill', () => {
  const drill = fakeDrill({
    argv: [process.execPath, '-e', 'process.exit(3)'],
    expect: 'nonzero',
    timeoutMs: 30_000,
  });

  const record = executeDrill(drill, drill.argv, quiet);

  assert.equal(record.status, 'passed');
  assert.equal(record.exitCode, 3);
  assert.equal(record.timedOut, false);
});

test('a command that cannot be spawned at all is never an inverted drill passing', () => {
  const drill = fakeDrill({
    argv: ['vni-no-such-executable-0000', '--version'],
    expect: 'nonzero',
    timeoutMs: 30_000,
  });

  const record = executeDrill(drill, drill.argv, quiet);

  assert.equal(record.status, 'failed');
  assert.equal(record.exitCode, null);
});

test('every declared drill carries a deadline inside the job budget that absorbed the hang', () => {
  const workflow = readFileSync(
    path.join(import.meta.dirname, '..', '.github', 'workflows', 'verify.yml'),
    'utf8',
  );
  const jobBudgetMs = Number(/timeout-minutes:\s*(\d+)/.exec(workflow)[1]) * 60_000;

  assert.ok(jobBudgetMs > 0);
  assert.ok(
    Number.isFinite(DEFAULT_DRILL_TIMEOUT_MS) && DEFAULT_DRILL_TIMEOUT_MS > 0,
    'the default deadline must be a finite number of milliseconds',
  );

  for (const drill of DRILLS) {
    const deadline = drill.timeoutMs ?? DEFAULT_DRILL_TIMEOUT_MS;
    assert.ok(
      Number.isFinite(deadline) && deadline > 0 && deadline < jobBudgetMs,
      `${drill.id}: deadline ${deadline}ms does not fit inside the ${jobBudgetMs}ms job budget`,
    );
  }
});

test('the live drill injects an option StartupConfiguration still refuses', () => {
  const gate = readFileSync(
    path.join(
      import.meta.dirname,
      '..',
      'backend',
      'src',
      'Vni.Ielts.Api',
      'Common',
      'StartupConfiguration.cs',
    ),
    'utf8',
  );
  const drill = DRILLS.find((d) => d.id === 'production-config-live');

  // What the drill actually hands to `docker compose run -e`, rather than a
  // second declaration of it that could drift from the argv.
  const override = drill.argv[drill.argv.indexOf('-e') + 1];
  const [variable] = override.split('=');
  // ASP.NET maps `__` to `:`; a trailing numeric segment is an array index and
  // names no option of its own.
  const key = variable
    .replace(/__\d+$/, '')
    .split('__')
    .join(':');

  assert.ok(
    gate.includes(key),
    `the drill injects ${variable}, but ${key} is read nowhere in the startup gate — ` +
      'the container will boot normally and the drill will hang',
  );

  const at = gate.search(new RegExp(drill.expectOutputMatches));
  assert.ok(at >= 0, `the gate emits nothing matching /${drill.expectOutputMatches}/`);

  // A warning does not stop a container. `ObjectStorage:ServiceUrl is plain
  // HTTP` is a warning, and retargeting the drill at it would reproduce the
  // original hang exactly.
  const before = gate.slice(0, at);
  const lastRefusal = Math.max(before.lastIndexOf('problems.Add('), before.lastIndexOf('Require('));
  const lastWarning = before.lastIndexOf('warnings.Add(');
  assert.ok(
    lastRefusal > lastWarning,
    'the drill waits for a warning, not a refusal — a warning leaves the container running',
  );
});
