#!/usr/bin/env node
//
// F5.1 — the one command that runs the Foundation pipeline in the order the
// checklist specifies: generate → docs/format → frontend checks → backend
// tests → integration → E2E → image build/smoke → security gates.
//
// `pnpm check` already existed and is not this. It runs six of these stages,
// in a different order, with no test-result artifacts, no skip detection and
// no drift detection — so it can go green over a suite that skipped every
// concurrency test and a generated client compiled from a contract that has
// since moved. Those two holes are the reason F5.1 exists, and they are
// closed here by two real checks (scripts/check-test-skips.mjs and
// scripts/check-generated-drift.mjs), not by a comment.
//
// Three rules this runner enforces on itself, because a verification command
// that overstates its own result is worse than no verification command:
//
//   1. A stage that did not run is never a stage that passed. Skipping,
//      platform-inapplicability and a missing dependency each produce a
//      distinct status, and any of them makes the whole run PARTIAL.
//   2. PARTIAL is not PASS. Only a run in which every declared stage
//      executed and exited 0 prints PASS, and only that run may be quoted as
//      pipeline evidence.
//   3. A stage whose evidence is owned by another phase (F3 backup, F4
//      security) is BLOCKED when that evidence is absent — never silently
//      dropped, and never assumed green.
//
// Usage:
//   node scripts/verify.mjs                     every applicable stage
//   node scripts/verify.mjs --list              what would run, and why not
//   node scripts/verify.mjs --only=docs,format
//   node scripts/verify.mjs --skip=e2e,smoke
//   node scripts/verify.mjs --from=backend-build
//   node scripts/verify.mjs --allow-blocked     pre-gate preparation runs
//   node scripts/verify.mjs --dry-run
//
// Exit codes: 0 PASS · 1 a stage failed · 2 PARTIAL (nothing failed, but the
// run does not certify anything).

import { spawnSync } from 'node:child_process';
import { runPortable } from './lib/spawn-portable.mjs';
import { existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const ARTIFACTS = join(ROOT, '_artifacts', 'verify');
const TEST_RESULTS = join(ARTIFACTS, 'test-results');
const EVIDENCE_MANIFEST = join(ROOT, 'ci', 'foundation-evidence.json');

const posix = (p) => p.split(sep).join('/');
const rel = (p) => posix(p.startsWith(ROOT) ? p.slice(ROOT.length + 1) : p);

// ── Stage definitions ──────────────────────────────────────────────────────
//
// `platforms` omitted means every platform. `needsDocker` and `needsMongo`
// are declared rather than discovered, so `--list` can explain a skip before
// anything runs. `dependsOn` names the phase that owns the evidence a stage
// consumes; a stage whose `requires` paths are missing is BLOCKED and names
// that phase in the report.

const dotnetTest = (project, extra = []) => [
  'dotnet',
  'test',
  `backend/tests/${project}`,
  '--configuration',
  'Release',
  '--nologo',
  '--logger',
  'trx',
  '--results-directory',
  TEST_RESULTS,
  ...extra,
];

const STAGES = [
  {
    id: 'toolchain-selftest',
    title: "The toolchain checker's own regression fixtures",
    argv: ['node', '--test', 'scripts/check-toolchain-versions.test.mjs'],
  },
  {
    id: 'toolchain',
    title:
      'Toolchain versions agree across .nvmrc, package.json, every workflow — and the running Node',
    argv: ['node', 'scripts/check-toolchain-versions.mjs'],
  },
  {
    id: 'install',
    title: 'Frozen-lockfile install',
    argv: ['pnpm', 'install', '--frozen-lockfile'],
    optIn: true, // a clean-checkout concern; skipped unless --install
  },
  {
    id: 'generate',
    title: 'Generate the API client from contracts/openapi',
    argv: ['pnpm', '--filter', '@vni/api-client', 'run', 'generate'],
  },
  {
    id: 'drift-generated',
    title: 'Generated artifacts are reproducible and untracked',
    argv: [
      'node',
      'scripts/check-generated-drift.mjs',
      '--mode=all',
      `--json=${join(ARTIFACTS, 'drift-generated.json')}`,
    ],
  },
  {
    id: 'openapi-snapshot',
    title: 'Record the OpenAPI contract hash before anything can rewrite it',
    argv: [
      'node',
      'scripts/check-generated-drift.mjs',
      '--mode=snapshot',
      '--paths=contracts/openapi/v1.json',
      `--manifest=${join(ARTIFACTS, 'openapi-manifest.json')}`,
    ],
  },
  {
    id: 'docs-selftest',
    title: "The documentation checker's own regression fixtures",
    argv: ['node', '--test', 'scripts/check-docs.test.mjs'],
  },
  {
    id: 'retention-selftest',
    title: 'Backup retention selector unit tests (F3)',
    argv: ['node', '--test', 'scripts/pbm-retention.test.mjs'],
    requires: ['scripts/pbm-retention.test.mjs'],
    dependsOn: 'F3',
  },
  {
    id: 'docs',
    title: 'Documentation integrity, link, source and credential-shape checks',
    argv: ['node', 'scripts/check-docs.mjs'],
  },
  { id: 'format', title: 'Prettier format check', argv: ['pnpm', 'format:check'] },
  {
    id: 'line-endings',
    title: 'No whitespace or line-ending damage in the working tree',
    argv: ['git', 'diff', '--check'],
  },
  {
    id: 'typecheck',
    title: 'TypeScript typecheck across the workspace',
    argv: ['pnpm', '-r', '--if-present', 'typecheck'],
  },
  {
    id: 'frontend-test',
    title: 'Frontend unit suites',
    argv: ['pnpm', '-r', '--workspace-concurrency=1', '--if-present', 'test'],
  },
  {
    id: 'frontend-build',
    title: 'Frontend and package builds',
    argv: ['pnpm', '-r', '--if-present', 'build'],
  },
  {
    id: 'backend-build',
    title: 'Backend build (warnings are errors)',
    argv: ['dotnet', 'build', 'backend', '--configuration', 'Release', '--nologo'],
  },
  {
    id: 'backend-architecture',
    title: 'Persistence boundary architecture tests',
    argv: dotnetTest('Vni.Ielts.Architecture.Tests'),
    producesResults: true,
  },
  {
    id: 'backend-domain',
    title: 'Domain tests',
    argv: dotnetTest('Vni.Ielts.Domain.Tests'),
    producesResults: true,
  },
  {
    id: 'backend-application',
    title: 'Application tests',
    argv: dotnetTest('Vni.Ielts.Application.Tests'),
    producesResults: true,
  },
  {
    id: 'backend-infrastructure',
    title: 'Infrastructure tests (real MongoDB replica set)',
    argv: dotnetTest('Vni.Ielts.Infrastructure.Tests'),
    env: { VNI_REQUIRE_MONGO: '1', VNI_REQUIRE_MINIO: '1' },
    needsMongo: true,
    producesResults: true,
  },
  {
    id: 'integration',
    title: 'Integration tests (real MongoDB replica set, OpenAPI contract, health faults)',
    argv: dotnetTest('Vni.Ielts.Integration.Tests'),
    env: { VNI_REQUIRE_MONGO: '1', VNI_REQUIRE_MINIO: '1' },
    needsMongo: true,
    producesResults: true,
  },
  {
    id: 'worker',
    title: 'Worker health and graceful-shutdown tests',
    argv: dotnetTest('Vni.Ielts.Worker.Tests'),
    env: { VNI_REQUIRE_MONGO: '1' },
    needsMongo: true,
    producesResults: true,
  },
  {
    id: 'openapi-drift',
    title: 'The OpenAPI contract was not rewritten by the contract test',
    argv: [
      'node',
      'scripts/check-generated-drift.mjs',
      '--mode=compare',
      `--manifest=${join(ARTIFACTS, 'openapi-manifest.json')}`,
    ],
  },
  {
    id: 'e2e',
    title: 'Real browser against a real API',
    argv: [
      'pnpm',
      '--filter',
      '@vni/e2e',
      'exec',
      'playwright',
      'test',
      '--project=desktop',
      '--reporter=list,json',
    ],
    // <b>Playwright writes its own report; nothing scrapes stdout.</b>
    //
    // This stage used to capture `--reporter=json` off the child's stdout into
    // playwright.json. On 2026-08-29 pnpm put `WARN Unsupported engine` on line
    // 1 of that file — the host's Node was 22 against an `engines` range of
    // >=24 — `JSON.parse` threw inside the skip gate, and the gate dropped all
    // 7 browser results without a word. It counted 585 tests over a run that
    // produced 592, and said "no unauthorized test skips" about a suite it had
    // never read.
    //
    // The gate now fails loudly on an unreadable report (FS0.6), but the
    // capture is the mechanism at fault: any tool may print to stdout, and a
    // report assembled from a shared stream is one warning away from being
    // junk. `PLAYWRIGHT_JSON_OUTPUT_FILE` is the JSON reporter's own documented
    // output path, so the report never travels through a stream anything else
    // can write to — and `list` keeps the console readable while it runs.
    env: { PLAYWRIGHT_JSON_OUTPUT_FILE: join(TEST_RESULTS, 'playwright.json') },
    resultFile: join(TEST_RESULTS, 'playwright.json'),
    needsMongo: true,
    producesResults: true,
  },
  {
    id: 'skips-selftest',
    // The skip gate had no stage of its own until FS0.6: nothing in verify.mjs,
    // in `pnpm check` or in any workflow ran check-test-skips.test.mjs, so the
    // regression tests that keep this gate honest were never executed by the
    // pipeline they exist to defend.
    title: "The skip gate's own regression fixtures",
    argv: ['node', '--test', 'scripts/check-test-skips.test.mjs'],
  },
  {
    id: 'skips',
    title: 'No test was skipped without a dated, owned exemption',
    argv: [
      'node',
      'scripts/check-test-skips.mjs',
      `--results=${TEST_RESULTS}`,
      '--require-results',
      `--json=${join(ARTIFACTS, 'skips.json')}`,
    ],
  },
  {
    id: 'images',
    title: 'Every OCI image builds, runs non-root and answers',
    argv: ['bash', 'scripts/verify-images.sh'],
    env: { VNI_REQUIRE_DOCKER: '1' },
    needsDocker: true,
    needsBash: true,
  },
  {
    id: 'smoke',
    title: 'Production-mode smoke: API + worker boot and answer a real request',
    argv: ['bash', 'scripts/production-smoke.sh'],
    needsDocker: true,
    needsBash: true,
  },
  {
    id: 'security',
    title: 'Security gates: dependency audit, secret scan, SAST, image scan, SBOM (F4)',
    // Resolved from ci/foundation-evidence.json at run time. The stage exists
    // before the evidence does on purpose: an absent security gate has to be
    // visible as BLOCKED in the pipeline, not absent from it.
    fromEvidence: 'F4.security-gate-command',
    dependsOn: 'F4',
  },
  {
    id: 'restore-drill',
    title: 'Backup restore drill (F3)',
    argv: ['bash', 'scripts/restore-drill.sh'],
    requires: ['scripts/restore-drill.sh'],
    dependsOn: 'F3',
    needsDocker: true,
    needsBash: true,
  },
];

// ── Environment probes ─────────────────────────────────────────────────────

function probe(command, args) {
  return runPortable([command, ...args], { encoding: 'utf8', stdio: 'pipe' }).status === 0;
}

const ENVIRONMENT = {
  platform: process.platform,
  node: process.version,
  get docker() {
    if (this._docker === undefined)
      this._docker = probe('docker', ['version', '--format', '{{.Server.Os}}']);
    return this._docker;
  },
  get bash() {
    if (this._bash === undefined) this._bash = probe('bash', ['-c', 'true']);
    return this._bash;
  },
  get dotnet() {
    if (this._dotnet === undefined) this._dotnet = probe('dotnet', ['--version']);
    return this._dotnet;
  },
};

// ── Evidence manifest ──────────────────────────────────────────────────────

function loadEvidence() {
  if (!existsSync(EVIDENCE_MANIFEST)) return null;
  return JSON.parse(readFileSync(EVIDENCE_MANIFEST, 'utf8'));
}

function resolveFromEvidence(stage, evidence) {
  if (!stage.fromEvidence) return { argv: stage.argv, missing: [] };
  const [phase, id] = stage.fromEvidence.split('.');
  const entry = evidence?.phases?.[phase]?.evidence?.find((e) => e.id === id);
  if (!entry)
    return { argv: null, missing: [`ci/foundation-evidence.json → ${stage.fromEvidence}`] };
  const candidates = entry.anyOf ?? (entry.path ? [entry.path] : []);
  const present = candidates.filter((p) => existsSync(join(ROOT, p)));
  if (present.length === 0) return { argv: null, missing: candidates };
  if (!entry.command) return { argv: null, missing: [`${stage.fromEvidence}.command`] };
  return { argv: entry.command.split(' '), missing: [] };
}

// ── Runner ─────────────────────────────────────────────────────────────────

function parseArgs(argv) {
  const args = {
    only: null,
    skip: [],
    from: null,
    list: false,
    dryRun: false,
    install: false,
    allowBlocked: false,
    json: join(ARTIFACTS, 'summary.json'),
  };
  for (const raw of argv) {
    const [key, ...rest] = raw.split('=');
    const value = rest.join('=');
    switch (key) {
      case '--only':
        args.only = value.split(',').filter(Boolean);
        break;
      case '--skip':
        args.skip.push(...value.split(',').filter(Boolean));
        break;
      case '--from':
        args.from = value;
        break;
      case '--list':
        args.list = true;
        break;
      case '--dry-run':
        args.dryRun = true;
        break;
      case '--install':
        args.install = true;
        break;
      case '--allow-blocked':
        args.allowBlocked = true;
        break;
      case '--json':
        args.json = value;
        break;
      default:
        throw new Error(`Unknown argument: ${raw}`);
    }
  }
  return args;
}

function plan(args, evidence) {
  const fromIndex = args.from ? STAGES.findIndex((s) => s.id === args.from) : 0;
  if (args.from && fromIndex < 0) throw new Error(`--from=${args.from} is not a stage id.`);

  return STAGES.map((stage, index) => {
    const resolved = resolveFromEvidence(stage, evidence);
    const entry = {
      stage,
      argv: resolved.argv,
      missing: resolved.missing,
      status: 'pending',
      reason: null,
    };

    if (index < fromIndex) {
      entry.status = 'skipped';
      entry.reason = `before --from=${args.from}`;
    } else if (args.only && !args.only.includes(stage.id)) {
      entry.status = 'skipped';
      entry.reason = '--only';
    } else if (args.skip.includes(stage.id)) {
      entry.status = 'skipped';
      entry.reason = '--skip';
    } else if (stage.optIn && !(stage.id === 'install' && args.install)) {
      entry.status = 'skipped';
      entry.reason = 'opt-in stage; pass --install to include it';
    } else if (stage.platforms && !stage.platforms.includes(process.platform)) {
      entry.status = 'not-applicable';
      entry.reason = `declared for ${stage.platforms.join(', ')}; this host is ${process.platform}`;
    } else if (stage.requires?.some((p) => !existsSync(join(ROOT, p)))) {
      entry.status = 'blocked';
      entry.missing = stage.requires.filter((p) => !existsSync(join(ROOT, p)));
      entry.reason = `missing evidence owned by ${stage.dependsOn ?? 'another phase'}: ${entry.missing.join(', ')}`;
    } else if (stage.fromEvidence && !entry.argv) {
      entry.status = 'blocked';
      entry.reason = `${stage.dependsOn ?? 'a dependency'} has not produced: ${entry.missing.join(' | ')}`;
    } else if (stage.needsDocker && !ENVIRONMENT.docker) {
      entry.status = 'unavailable';
      entry.reason = 'no reachable Docker daemon on this host';
    } else if (stage.needsBash && !ENVIRONMENT.bash) {
      entry.status = 'unavailable';
      entry.reason = 'no bash on this host';
    } else {
      entry.status = 'ready';
    }
    return entry;
  });
}

function execute(entry, args) {
  const { stage, argv } = entry;
  const startedAt = new Date();
  const start = process.hrtime.bigint();

  // A report left behind by an earlier run is not evidence about this one.
  // Deleting it first means a stage that dies before writing its report leaves
  // no file at all, and the skip gate's --expect-report then says so instead of
  // counting yesterday's results as today's.
  if (stage.resultFile) {
    mkdirSync(dirname(stage.resultFile), { recursive: true });
    rmSync(stage.resultFile, { force: true });
  }

  const result = args.dryRun
    ? { status: 0 }
    : runPortable(argv, {
        cwd: ROOT,
        env: { ...process.env, ...(stage.env ?? {}) },
        stdio: 'inherit',
      });

  const durationMs = Number(process.hrtime.bigint() - start) / 1e6;
  const exitCode = result.status ?? 1;
  return {
    exitCode,
    startedAt: startedAt.toISOString(),
    endedAt: new Date().toISOString(),
    durationMs: Math.round(durationMs),
    status: args.dryRun ? 'dry-run' : exitCode === 0 ? 'passed' : 'failed',
  };
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const evidence = loadEvidence();
  const entries = plan(args, evidence);

  // <b>Results from a previous run are not evidence about this one.</b>
  //
  // Nothing cleared this directory, so `.trx` files accumulated across runs and
  // the skip gate counted them all: a run of this pipeline on 2026-08-29
  // reported 13 result files and 1278 tests when it had itself produced 6 and
  // ~693 — the rest were 45 minutes old, from a different state of the tree.
  // Inflated counts are the friendly failure mode; the dangerous one is a suite
  // that stopped producing results at all while its last green `.trx` stayed on
  // disk and kept being counted.
  //
  // A selective run is the exception: `--only` and `--from` exist so a stage can
  // be re-run against results earlier stages already wrote, and clearing those
  // would break the very workflow the flags are for. `--skip` does not select,
  // so it still clears.
  const selective = Boolean(args.only || args.from);
  if (!selective && !args.list && !args.dryRun) {
    rmSync(TEST_RESULTS, { recursive: true, force: true });
    console.log(`Cleared ${rel(TEST_RESULTS)} — this run's results only.`);
  }
  mkdirSync(TEST_RESULTS, { recursive: true });

  if (args.list) {
    console.log(`Foundation verification plan — ${process.platform}, node ${process.version}\n`);
    for (const entry of entries) {
      const label = entry.status.toUpperCase().padEnd(14);
      console.log(`${label} ${entry.stage.id.padEnd(24)} ${entry.stage.title}`);
      if (entry.reason) console.log(`${' '.repeat(15)}↳ ${entry.reason}`);
    }
    return 0;
  }

  const started = new Date();
  const records = [];

  // Reports this run actually produced, handed to the skip gate as
  // --expect-report so that a report which never appeared is a failure rather
  // than one fewer file to count. Only stages that ran and passed are listed:
  // a suite skipped with --skip=e2e, or unavailable for want of Docker, owes
  // no report, and demanding one would turn a declared skip into a false red.
  const producedReports = [];

  for (const entry of entries) {
    if (entry.status !== 'ready') {
      records.push({
        id: entry.stage.id,
        title: entry.stage.title,
        status: entry.status,
        reason: entry.reason,
        dependsOn: entry.stage.dependsOn ?? null,
        command: entry.argv ? entry.argv.join(' ') : null,
      });
      console.log(
        `\n── ${entry.stage.id} — ${entry.status.toUpperCase()}${entry.reason ? ` (${entry.reason})` : ''}`,
      );
      continue;
    }

    if (entry.stage.id === 'skips') {
      entry.argv = [...entry.argv, ...producedReports.map((f) => `--expect-report=${f}`)];
    }

    console.log(`\n── ${entry.stage.id} — ${entry.stage.title}`);
    console.log(`   $ ${entry.argv.join(' ')}`);
    const outcome = execute(entry, args);

    if (outcome.status === 'passed' && entry.stage.resultFile) {
      producedReports.push(entry.stage.resultFile);
    }
    records.push({
      id: entry.stage.id,
      title: entry.stage.title,
      command: entry.argv.join(' '),
      dependsOn: entry.stage.dependsOn ?? null,
      ...outcome,
    });
    console.log(`   → exit ${outcome.exitCode} in ${(outcome.durationMs / 1000).toFixed(1)}s`);

    if (outcome.status === 'failed') {
      console.error(
        `\n${entry.stage.id} failed. Stopping — later stages read what this one produces.`,
      );
      break;
    }
  }

  const ran = records.filter((r) => ['passed', 'failed', 'dry-run'].includes(r.status));
  const failed = records.filter((r) => r.status === 'failed');
  const notRun = records.filter((r) => !['passed', 'failed', 'dry-run'].includes(r.status));
  const blocked = notRun.filter((r) => r.status === 'blocked');
  const complete = notRun.length === 0 && failed.length === 0 && !args.dryRun;

  const verdict = failed.length > 0 ? 'FAIL' : complete ? 'PASS' : 'PARTIAL';

  const summary = {
    verdict,
    // The single field a report should quote. A PARTIAL run is evidence of
    // what it ran and of nothing else.
    certifies:
      verdict === 'PASS'
        ? 'every declared stage executed and exited 0 on this host'
        : 'nothing — stages were skipped, blocked, unavailable or failed',
    startedAt: started.toISOString(),
    finishedAt: new Date().toISOString(),
    durationMs: Date.now() - started.getTime(),
    host: {
      platform: process.platform,
      node: process.version,
      docker: ENVIRONMENT.docker,
      bash: ENVIRONMENT.bash,
      dotnet: ENVIRONMENT.dotnet,
    },
    commit:
      spawnSync('git', ['rev-parse', 'HEAD'], { cwd: ROOT, encoding: 'utf8' }).stdout?.trim() ??
      null,
    stagesTotal: STAGES.length,
    stagesRun: ran.length,
    stagesPassed: records.filter((r) => r.status === 'passed').length,
    stagesFailed: failed.length,
    stagesNotRun: notRun.length,
    blockedOn: [...new Set(blocked.map((r) => r.dependsOn).filter(Boolean))],
    artifacts: { testResults: rel(TEST_RESULTS), summary: rel(args.json) },
    stages: records,
  };

  mkdirSync(dirname(args.json), { recursive: true });
  writeFileSync(args.json, `${JSON.stringify(summary, null, 2)}\n`);

  console.log(`\n${'='.repeat(72)}`);
  console.log(
    `VERDICT: ${verdict}   (${summary.stagesPassed} passed · ${failed.length} failed · ${notRun.length} not run)`,
  );
  console.log(`Certifies: ${summary.certifies}`);
  if (blocked.length > 0) {
    console.log('\nBlocked on evidence owned by another phase:');
    for (const record of blocked) console.log(`  ${record.id.padEnd(22)} ${record.reason}`);
  }
  console.log(`\nSummary: ${rel(args.json)}`);
  console.log('='.repeat(72));

  if (failed.length > 0) return 1;
  if (verdict === 'PARTIAL') {
    if (args.allowBlocked) {
      console.log(
        '\n--allow-blocked: exiting 0. This run is NOT a certification and must not be quoted as one.',
      );
      return 0;
    }
    return 2;
  }
  return 0;
}

try {
  process.exit(main());
} catch (error) {
  console.error(`error: ${error.message}`);
  process.exit(1);
}
