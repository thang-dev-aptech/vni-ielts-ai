#!/usr/bin/env node
//
// F5.2 — runs the F4 security gates and writes a machine-readable report to
// `_artifacts/security/`.
//
// ── Why a report and not just an exit code ─────────────────────────────────
//
// `pnpm security:check` already gates correctly: it fails the build on an
// invalid allowlist, an expired waiver or an unpinned base image. What it does
// not do is leave anything behind, and `verify.yml` uploads
// `_artifacts/security/` on every run — so until this existed that upload
// retained an empty directory behind `if-no-files-found: ignore`, which looks
// exactly like evidence and is not.
//
// A run that fails still writes the report. That is the whole point: the case
// where somebody needs to read it is the case where the build went red.
//
// Usage: node scripts/security-report.mjs

import { execFileSync } from 'node:child_process';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '..');
const OUT = path.join(ROOT, '_artifacts', 'security');

/**
 * Runs one gate and records what happened, rather than throwing.
 *
 * A gate that cannot run (no .NET SDK, no network) is `unavailable`, never
 * `passed` — the distinction the whole Foundation queue rests on.
 */
function gate(id, description, argv, { optional = false } = {}) {
  const started = Date.now();
  try {
    const stdout = execFileSync(argv[0], argv.slice(1), {
      cwd: ROOT,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
      shell: process.platform === 'win32' && /^(pnpm|npm|npx)$/.test(argv[0]),
    });
    return {
      id,
      description,
      status: 'passed',
      exitCode: 0,
      durationMs: Date.now() - started,
      output: stdout.trim().split('\n').slice(-20).join('\n'),
    };
  } catch (e) {
    const output = `${e.stdout ?? ''}${e.stderr ?? ''}`.trim();

    // A missing executable is "could not run", which is not the same as
    // "ran and found nothing wrong".
    const unavailable = e.code === 'ENOENT' || optional;

    return {
      id,
      description,
      status: unavailable ? 'unavailable' : 'failed',
      exitCode: typeof e.status === 'number' ? e.status : null,
      durationMs: Date.now() - started,
      output: output.split('\n').slice(-30).join('\n'),
    };
  }
}

function main() {
  const gates = [
    gate(
      'vulnerability-allowlist',
      'Every High/Critical waiver has a reason, an owner and an unexpired date (F4.4).',
      ['node', 'scripts/check-vulnerability-allowlist.mjs'],
    ),
    gate('allowlist-selftest', 'The allowlist rules themselves, including expiry (F4.4).', [
      'node',
      '--test',
      'scripts/check-vulnerability-allowlist.test.mjs',
    ]),
    gate('base-image-pins', 'Every Dockerfile base image is pinned by digest (F4.5).', [
      'node',
      'scripts/check-base-image-pins.mjs',
    ]),
    gate(
      'pin-selftest',
      'The pin rule itself, including stage aliases and truncated digests (F4.5).',
      ['node', '--test', 'scripts/check-base-image-pins.test.mjs'],
    ),
    // Needs the .NET SDK and a reachable NuGet. `dotnet list package
    // --vulnerable` exits 0 even when it finds something, so the OUTPUT is
    // what decides — the same reasoning as the CI step in security.yml.
    gate(
      'nuget-advisories',
      'Known-vulnerable NuGet packages, including transitive (F4.4).',
      [
        'dotnet',
        'list',
        'backend/Vni.Ielts.sln',
        'package',
        '--vulnerable',
        '--include-transitive',
      ],
      { optional: true },
    ),
  ];

  // The NuGet gate's verdict lives in its text, not its exit code.
  const nuget = gates.find((g) => g.id === 'nuget-advisories');
  if (nuget && nuget.status !== 'unavailable') {
    const found = /\s(High|Critical)\s/i.test(nuget.output);
    nuget.status = found ? 'failed' : 'passed';
    nuget.note = found
      ? 'A High or Critical advisory is present. Fix it, or waive it in security/vulnerability-allowlist.json.'
      : 'No High or Critical advisory in any project.';
  }

  const failed = gates.filter((g) => g.status === 'failed');
  const unavailable = gates.filter((g) => g.status === 'unavailable');

  const report = {
    ranAt: new Date().toISOString(),
    commit: (() => {
      try {
        return execFileSync('git', ['rev-parse', 'HEAD'], { cwd: ROOT, encoding: 'utf8' }).trim();
      } catch {
        return 'unknown';
      }
    })(),
    host: { platform: process.platform, node: process.version },
    // <b>PASS only when every gate actually ran.</b> An unavailable gate makes
    // the verdict PARTIAL, so a report can never be mistaken for coverage it
    // does not have.
    verdict: failed.length > 0 ? 'FAIL' : unavailable.length > 0 ? 'PARTIAL' : 'PASS',
    certifies:
      failed.length > 0
        ? 'nothing — a security gate failed'
        : unavailable.length > 0
          ? 'nothing — a gate could not run, and a gate that did not run did not pass'
          : 'the F4 security gates that run without a network-bound scanner',
    notCovered: [
      'CodeQL — GitHub-hosted analysis; runs in .github/workflows/security.yml only.',
      'gitleaks — runs in .github/workflows/security.yml only.',
      'Trivy image scan — runs in .github/workflows/security.yml; locally via scripts/verify-images.sh.',
      'npm advisories — pnpm audit; needs a reachable registry.',
    ],
    gates,
  };

  mkdirSync(OUT, { recursive: true });
  writeFileSync(path.join(OUT, 'summary.json'), `${JSON.stringify(report, null, 2)}\n`);

  for (const g of gates) {
    const mark = g.status === 'passed' ? 'ok  ' : g.status === 'unavailable' ? '--  ' : 'FAIL';
    console.log(`  ${mark} ${g.id.padEnd(24)} ${g.note ?? g.description}`);
  }

  console.log(`\nVERDICT: ${report.verdict}`);
  console.log(`Certifies: ${report.certifies}`);
  console.log(`Report: _artifacts/security/summary.json`);

  if (failed.length > 0) process.exit(1);
}

if (process.argv[1] && import.meta.url.endsWith(path.basename(process.argv[1]))) {
  main();
}
