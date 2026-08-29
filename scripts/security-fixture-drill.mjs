#!/usr/bin/env node
// F4.4/F5.4 — run four isolated, intentional security findings.
// A missing scanner is BLOCKED, never passed. Override commands are JSON argv
// arrays and exist for the deterministic harness tests only; no shell strings
// are evaluated.

import { copyFileSync, existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { runPortable } from './lib/spawn-portable.mjs';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const rel = (path) => path.split(sep).join('/');
const fixture = (...parts) => join(ROOT, 'fixtures', 'security', ...parts);
const TAG = 'vni-security-fixture:local';
const SECRET_FIXTURE_PLACEHOLDER = '{runtime-secret-fixture}';
const DEPENDENCY_FIXTURE_PLACEHOLDER = '{runtime-dependency-fixture}';

const definitions = [
  {
    id: 'secret',
    expectation: 'nonzero',
    tools: ['gitleaks'],
    fixture: fixture('secret', 'fixture-recipe.json'),
    steps: [
      [
        'gitleaks',
        'detect',
        '--source',
        SECRET_FIXTURE_PLACEHOLDER,
        '--no-git',
        '--redact',
        '--exit-code',
        '1',
      ],
    ],
  },
  {
    id: 'dependency',
    expectation: 'nonzero',
    tools: ['pnpm'],
    fixture: fixture('dependency', 'pnpm-lock.fixture.yaml'),
    steps: [
      [
        'pnpm',
        '--dir',
        DEPENDENCY_FIXTURE_PLACEHOLDER,
        '--ignore-workspace',
        'audit',
        '--audit-level',
        'high',
        '--prod',
      ],
    ],
  },
  {
    id: 'image',
    expectation: 'nonzero',
    tools: ['docker', 'trivy'],
    fixture: fixture('image', 'Dockerfile'),
    steps: [
      ['docker', 'build', '--tag', TAG, fixture('image')],
      [
        'trivy',
        'image',
        '--severity',
        'HIGH,CRITICAL',
        '--exit-code',
        '1',
        '--ignore-unfixed',
        TAG,
      ],
    ],
  },
  {
    id: 'codeql',
    // `codeql test run` exits zero only when the real query result equals the
    // committed `.expected` tuple. It is not a scanner-clean exit.
    expectation: 'zero',
    tools: ['codeql'],
    fixture: fixture('codeql', 'IntentionalCodeInjection.expected'),
    steps: [
      ['codeql', 'pack', 'install', fixture('codeql')],
      ['codeql', 'test', 'run', fixture('codeql')],
    ],
  },
];

function commandExists(command) {
  const result = runPortable([command, '--version'], { encoding: 'utf8', stdio: 'pipe' });
  return result.status === 0;
}

function overrideFor(id) {
  const key = `VNI_SECURITY_FIXTURE_${id.toUpperCase()}_COMMAND`;
  const raw = process.env[key];
  if (!raw) return null;
  if (process.env.VNI_SECURITY_FIXTURE_ALLOW_COMMAND_OVERRIDES !== '1') {
    throw new Error(`${key} is test-only; set VNI_SECURITY_FIXTURE_ALLOW_COMMAND_OVERRIDES=1`);
  }
  let argv;
  try {
    argv = JSON.parse(raw);
  } catch (error) {
    throw new Error(`${key} must be a JSON argv array: ${error.message}`);
  }
  if (!Array.isArray(argv) || argv.length === 0 || argv.some((part) => typeof part !== 'string')) {
    throw new Error(`${key} must be a non-empty JSON array of strings`);
  }
  return [argv];
}

function runProbe(definition) {
  const override = overrideFor(definition.id);
  const configuredSteps = override ?? definition.steps;
  const displayCommand = configuredSteps.map((argv) => argv.join(' ')).join(' && ');
  if (!existsSync(definition.fixture)) {
    return {
      command: displayCommand,
      exitCode: null,
      status: 'blocked',
      reason: `missing fixture ${rel(definition.fixture.replace(`${ROOT}${sep}`, ''))}`,
    };
  }

  if (!override) {
    const missing = definition.tools.filter((tool) => !commandExists(tool));
    if (missing.length > 0) {
      return {
        command: displayCommand,
        exitCode: null,
        status: 'blocked',
        reason: `required command unavailable: ${missing.join(', ')}`,
      };
    }
  }

  let temporaryFixture = null;
  let steps = configuredSteps;
  if (!override && definition.id === 'secret') {
    // Keeping a detector-shaped credential in git would make the production
    // full-history secret scan permanently red. Materialize the synthetic
    // value only for this isolated drill and remove it immediately afterward.
    temporaryFixture = mkdtempSync(join(tmpdir(), 'vni-secret-fixture-'));
    const accessKey = ['AK', 'IA', 'Z6QWERTY7UIOP123'].join('');
    const secretKey = ['abcdEFGH', 'ijklMNOP', 'qrstUVWX', 'yz012345', '6789ABCD'].join('');
    writeFileSync(
      join(temporaryFixture, 'intentional.env'),
      `AWS_ACCESS_KEY_ID=${accessKey}\nAWS_SECRET_ACCESS_KEY=${secretKey}\n`,
    );
    steps = configuredSteps.map((argv) =>
      argv.map((part) => (part === SECRET_FIXTURE_PLACEHOLDER ? temporaryFixture : part)),
    );
  } else if (!override && definition.id === 'dependency') {
    // Likewise, do not let the repository dependency graph mistake a drill
    // input for a shipped dependency. Only conventional filenames are
    // materialized in the temporary scan root.
    temporaryFixture = mkdtempSync(join(tmpdir(), 'vni-dependency-fixture-'));
    copyFileSync(
      fixture('dependency', 'package.fixture.json'),
      join(temporaryFixture, 'package.json'),
    );
    copyFileSync(
      fixture('dependency', 'pnpm-lock.fixture.yaml'),
      join(temporaryFixture, 'pnpm-lock.yaml'),
    );
    steps = configuredSteps.map((argv) =>
      argv.map((part) => (part === DEPENDENCY_FIXTURE_PLACEHOLDER ? temporaryFixture : part)),
    );
  }

  let exitCode = 0;
  try {
    for (const argv of steps) {
      console.log(`   $ ${argv.join(' ')}`);
      const result = runPortable(argv, { cwd: ROOT, env: process.env, stdio: 'inherit' });
      exitCode = result.status ?? 1;
      if (exitCode !== 0) break;
    }
  } finally {
    if (temporaryFixture) rmSync(temporaryFixture, { recursive: true, force: true });
  }

  const matched = definition.expectation === 'nonzero' ? exitCode !== 0 : exitCode === 0;
  return {
    command: displayCommand,
    exitCode,
    status: matched ? 'observed' : 'failed',
  };
}

function main() {
  const records = {};
  for (const definition of definitions) {
    console.log(`\n-- ${definition.id} fixture (expects ${definition.expectation})`);
    records[definition.id] = runProbe(definition);
    const record = records[definition.id];
    console.log(
      record.status === 'blocked'
        ? `   -> BLOCKED: ${record.reason}`
        : `   -> exit ${record.exitCode}: ${record.status}`,
    );
  }

  console.log(`\nVNI_SECURITY_FIXTURE_RESULTS=${JSON.stringify(records)}`);

  if (Object.values(records).some((record) => record.status === 'failed')) return 1;
  if (Object.values(records).some((record) => record.status === 'blocked')) return 2;
  return 0;
}

try {
  process.exit(main());
} catch (error) {
  console.error(`error: ${error.message}`);
  process.exit(1);
}
