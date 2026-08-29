// FS0.6 — run: node --test scripts/check-toolchain-versions.test.mjs
//
// This gate compared `.nvmrc`, `package.json` and every workflow file to each
// other, and never to the Node actually running it. So on 2026-08-29 it passed
// on a host running Node v22.22.2 against a declared 24 — while pnpm printed
// `WARN Unsupported engine` on every invocation, and that warning line landed
// as line 1 of a scraped Playwright report and blinded the skip gate to seven
// browser tests.
//
// Four copies of one fact, all agreeing with each other, none of them checked
// against reality.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';

import { hostNodeProblem } from './check-toolchain-versions.mjs';

const ROOT = path.join(import.meta.dirname, '..');

test('NEGATIVE PROOF — a host Node that differs from .nvmrc is reported', () => {
  const problem = hostNodeProblem('v22.22.2', '24');

  assert.ok(problem, 'Node 22 on a Node 24 project must be a problem, not silence');
  assert.match(problem, /v22\.22\.2/);
  assert.match(problem, /24/);
  assert.match(problem, /Unsupported engine/, 'say what the mismatch actually causes');
});

test('a matching host Node is not a problem — the check is not simply always red', () => {
  assert.equal(hostNodeProblem('v24.4.0', '24'), null);
  assert.equal(hostNodeProblem('24.0.0', '24'), null);
});

test('only the major version is compared', () => {
  assert.equal(hostNodeProblem('v24.11.1', '24'), null);
  assert.ok(hostNodeProblem('v23.11.1', '24'));
});

test('--strict turns the host mismatch into a failure, which is what CI gets', () => {
  // `main()` treats `process.env.CI` exactly as --strict, so this is the CI
  // behaviour with none of the fragility of faking an environment variable.
  const expectedMajor = readFileSync(path.join(ROOT, '.nvmrc'), 'utf8').trim();
  const runningMajor = process.version.replace(/^v/, '').split('.')[0];

  let output = '';
  let code = 0;
  try {
    output = execFileSync('node', ['scripts/check-toolchain-versions.mjs', '--strict'], {
      cwd: ROOT,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
    });
  } catch (error) {
    code = error.status;
    output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
  }

  if (runningMajor === expectedMajor) {
    assert.equal(code, 0, output);
  } else {
    assert.equal(code, 1, `a host on Node ${process.version} must fail --strict.\n${output}`);
    assert.match(output, /running Node does not match/, output);
  }
});

test('importing the gate does not run it', () => {
  // The module used to be entirely top-level, so importing `hostNodeProblem`
  // would run the whole check and — under CI, with a mismatch — call
  // process.exit from inside an import.
  const source = readFileSync(path.join(ROOT, 'scripts', 'check-toolchain-versions.mjs'), 'utf8');
  assert.match(source, /import\.meta\.url === pathToFileURL\(process\.argv\[1\]\)\.href/);
});
