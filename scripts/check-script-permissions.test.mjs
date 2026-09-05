// F5.2 — run: node --test scripts/check-script-permissions.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { trackedShellScripts, nonExecutable } from './check-script-permissions.mjs';

const line = (mode, file) => `${mode} 0000000000000000000000000000000000000000 0\t${file}`;

test('an executable script passes', () => {
  const entries = trackedShellScripts(line('100755', 'scripts/a.sh'));
  assert.deepEqual(nonExecutable(entries), []);
});

test('a non-executable script is caught — this is the whole point', () => {
  // 100644 is what Git Bash on Windows commits, because MSYS chmod does not
  // change the effective mode. It works locally and dies on Linux CI.
  const entries = trackedShellScripts(line('100644', 'scripts/otel-smoke.sh'));
  assert.deepEqual(nonExecutable(entries), ['scripts/otel-smoke.sh']);
});

test('only .sh files are considered', () => {
  const out = [line('100644', 'scripts/tool.mjs'), line('100644', 'README.md')].join('\n');
  assert.deepEqual(trackedShellScripts(out), []);
});

test('several scripts are each reported', () => {
  const out = [
    line('100755', 'scripts/ok.sh'),
    line('100644', 'scripts/bad-one.sh'),
    line('100644', 'scripts/bad-two.sh'),
  ].join('\n');

  assert.deepEqual(nonExecutable(trackedShellScripts(out)), [
    'scripts/bad-one.sh',
    'scripts/bad-two.sh',
  ]);
});

test('a symlink mode is not mistaken for executable', () => {
  // 120000 is a symlink. It is not 100755, so it must be reported rather than
  // silently accepted by a loose "not 100644" test.
  assert.deepEqual(nonExecutable(trackedShellScripts(line('120000', 'scripts/link.sh'))), [
    'scripts/link.sh',
  ]);
});

test('every shell script in this repository is executable', async () => {
  // The check that actually protects the repository, rather than the parser.
  const { execFileSync } = await import('node:child_process');
  const path = (await import('node:path')).default;

  const output = execFileSync('git', ['ls-files', '-s', '--', '*.sh'], {
    cwd: path.join(import.meta.dirname, '..'),
    encoding: 'utf8',
  });

  const scripts = trackedShellScripts(output);
  assert.ok(scripts.length > 0, 'no tracked .sh files found — the glob is wrong');
  assert.deepEqual(nonExecutable(scripts), []);
});
