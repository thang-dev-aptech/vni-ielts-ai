// F5.4 — the security drill must account for every scanner independently.
// Run: node --test scripts/failure-drills.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { evaluateSecurityFixtureOutput, SECURITY_FIXTURE_PROBES } from './failure-drills.mjs';

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
