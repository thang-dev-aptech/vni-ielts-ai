#!/usr/bin/env node
//
// F5.3 — burn-in, which is the opposite of a retry.
//
// A workflow-level retry and a burn-in loop both run a flaky test more than
// once, and they mean opposite things. A retry runs it again *after* it
// failed and reports the last result, so a race that fires one time in eight
// becomes a green build with a footnote. A burn-in runs it N times *before*
// anyone believes it, reports the first failure, and keeps the seed and the
// iteration number that produced it. This repository has two suites whose
// entire purpose is two writers racing — the idempotency claim and the
// answer-sheet CAS — so the difference is not academic: a retry on those
// suites would hide exactly the defect they exist to catch.
//
// The checklist requires the idempotency integration suite to run at least
// ten consecutive iterations. That number is the floor, not the ceiling: an
// interleaving that appears once in twenty is still a defect, and `--iterations`
// exists so the number can be raised when a suite has a history.
//
// Usage:
//   node scripts/burn-in.mjs --suite=idempotency
//   node scripts/burn-in.mjs --suite=idempotency --iterations=20
//   node scripts/burn-in.mjs --suite=all
//   node scripts/burn-in.mjs --list
//
// Exit codes: 0 every iteration passed · 1 an iteration failed, or a suite
// was requested that this host cannot run.

import { spawnSync } from 'node:child_process';
import { runPortable } from './lib/spawn-portable.mjs';
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const OUT = join(ROOT, '_artifacts', 'burn-in');
const posix = (p) => p.split(sep).join('/');

// Each suite records WHY it is here. "It felt flaky once" is not a reason
// that survives a year; the race it is watching for is.
const SUITES = {
  idempotency: {
    title: 'Idempotency claim under concurrent replay',
    race: 'Two requests carrying the same Idempotency-Key must produce one side effect. The claim is a compare-and-swap; the losing writer must observe the winner rather than proceed.',
    iterations: 10, // the checklist floor
    project: 'backend/tests/Vni.Ielts.Integration.Tests',
    filter: 'IdempotencyContractTests',
    env: { VNI_REQUIRE_MONGO: '1' },
    needsMongo: true,
  },
  'exam-run': {
    title: 'Answer-sheet revision CAS and section transition',
    race: 'Per-question ordering, the answer-sheet revision compare-and-swap and the Full Test section transition — every one of them is two mobile clients retrying the same write.',
    iterations: 10,
    project: 'backend/tests/Vni.Ielts.Integration.Tests',
    filter: 'ExamRunContractTests|ExamSessionTransitionTests|AnswerSheetStoreTests',
    env: { VNI_REQUIRE_MONGO: '1' },
    needsMongo: true,
  },
  health: {
    title: 'Readiness under dependency fault',
    race: 'F0.1 — readiness that reports 200 while its dependency is gone. The probe races container teardown, which is exactly the window a single run can miss.',
    iterations: 10,
    project: 'backend/tests/Vni.Ielts.Integration.Tests',
    filter: 'HealthFaultTests|ObjectStorageHealthTests',
    env: { VNI_REQUIRE_MONGO: '1', VNI_REQUIRE_MINIO: '1' },
    needsMongo: true,
  },
  worker: {
    title: 'Worker liveness and graceful shutdown',
    race: 'A job in flight when the stop signal arrives must complete or return its lease — never both, never neither.',
    iterations: 10,
    project: 'backend/tests/Vni.Ielts.Worker.Tests',
    filter: 'GracefulShutdownTests|WorkerHealthTests',
    env: { VNI_REQUIRE_MONGO: '1' },
    needsMongo: true,
  },
};

function parseArgs(argv) {
  const args = { suite: null, iterations: null, list: false, json: null, buildOnce: true };
  for (const raw of argv) {
    const [key, ...rest] = raw.split('=');
    const value = rest.join('=');
    switch (key) {
      case '--suite':
        args.suite = value;
        break;
      case '--iterations':
        args.iterations = Number(value);
        if (!Number.isInteger(args.iterations) || args.iterations < 1) {
          throw new Error(`--iterations must be a positive integer, got "${value}".`);
        }
        break;
      case '--list':
        args.list = true;
        break;
      case '--json':
        args.json = value;
        break;
      case '--no-build':
        args.buildOnce = false;
        break;
      default:
        throw new Error(`Unknown argument: ${raw}`);
    }
  }
  return args;
}

function countFromTrx(dir, since) {
  // The .trx the run just wrote is the only honest source of a test count.
  // Reading the console summary would mean parsing localized text.
  let latest = null;
  for (const name of readdirSync(dir)) {
    if (!name.endsWith('.trx')) continue;
    const full = join(dir, name);
    const text = readFileSync(full, 'utf8');
    const counters = text.match(/<Counters\b[^>]*\/>/);
    if (!counters) continue;
    const attr = (n) => {
      const m = counters[0].match(new RegExp(`${n}="(\\d+)"`));
      return m ? Number(m[1]) : 0;
    };
    const stat = { file: full, mtime: Date.now() };
    if (since && !name.includes(since)) continue;
    latest = {
      ...stat,
      total: attr('total'),
      passed: attr('passed'),
      failed: attr('failed'),
      notExecuted: attr('notExecuted'),
    };
  }
  return latest;
}

function runIteration(suite, index, resultsDir) {
  const trxName = `${suite.key}-iteration-${String(index).padStart(2, '0')}.trx`;
  const argv = [
    'test',
    suite.project,
    '--configuration',
    'Release',
    '--nologo',
    '--filter',
    suite.filter,
    '--logger',
    `trx;LogFileName=${trxName}`,
    '--results-directory',
    resultsDir,
  ];
  // --no-build after the first iteration: rebuilding nine more times measures
  // MSBuild, not the race.
  if (index > 1) argv.push('--no-build');

  const startedAt = new Date();
  const start = process.hrtime.bigint();
  const result = runPortable(['dotnet', ...argv], {
    cwd: ROOT,
    env: { ...process.env, ...suite.env },
    stdio: 'inherit',
  });
  const durationMs = Math.round(Number(process.hrtime.bigint() - start) / 1e6);
  const exitCode = result.status ?? 1;

  const counts = existsSync(join(resultsDir, trxName))
    ? countFromTrx(resultsDir, trxName.replace('.trx', ''))
    : null;

  return {
    iteration: index,
    exitCode,
    startedAt: startedAt.toISOString(),
    durationMs,
    trx: posix(join('_artifacts', 'burn-in', suite.key, trxName)),
    counts,
  };
}

function main() {
  const args = parseArgs(process.argv.slice(2));

  if (args.list || !args.suite) {
    console.log('Burn-in suites — each entry names the race it is watching for.\n');
    for (const [key, suite] of Object.entries(SUITES)) {
      console.log(`  ${key.padEnd(14)} ${suite.title}  (${suite.iterations} iterations)`);
      console.log(`  ${' '.repeat(14)} ${suite.race}\n`);
    }
    console.log('  all            every suite above, in order\n');
    if (!args.suite) {
      console.log('Pass --suite=<name>. Nothing was run.');
      return args.list ? 0 : 1;
    }
    return 0;
  }

  const keys = args.suite === 'all' ? Object.keys(SUITES) : [args.suite];
  for (const key of keys) {
    if (!SUITES[key]) throw new Error(`Unknown suite "${key}". Run --list.`);
  }

  const runs = [];
  let anyFailed = false;

  for (const key of keys) {
    const suite = { ...SUITES[key], key };
    const iterations = args.iterations ?? suite.iterations;
    const resultsDir = join(OUT, key);
    mkdirSync(resultsDir, { recursive: true });

    console.log(`\n${'='.repeat(72)}`);
    console.log(`Burn-in: ${key} — ${suite.title}`);
    console.log(`Race:    ${suite.race}`);
    console.log(`Filter:  ${suite.filter}   ×${iterations} consecutive iterations`);
    console.log(`${'='.repeat(72)}`);

    const iterationRecords = [];
    for (let i = 1; i <= iterations; i += 1) {
      console.log(`\n── ${key} iteration ${i}/${iterations}`);
      const record = runIteration(suite, i, resultsDir);
      iterationRecords.push(record);
      const counts = record.counts;
      console.log(
        `   → exit ${record.exitCode} in ${(record.durationMs / 1000).toFixed(1)}s` +
          (counts
            ? `   total ${counts.total} · passed ${counts.passed} · failed ${counts.failed} · skipped ${counts.notExecuted}`
            : ''),
      );

      if (record.exitCode !== 0) {
        console.error(
          `\nIteration ${i} of ${iterations} FAILED. This is the point of a burn-in: the race reproduces at 1 in ${i}.`,
        );
        console.error('Do not add a retry. The .trx above is the evidence; find the interleaving.');
        anyFailed = true;
        break;
      }
      if (counts && counts.notExecuted > 0) {
        console.error(
          `\nIteration ${i} skipped ${counts.notExecuted} test(s). A burn-in over skipped tests proves nothing.`,
        );
        anyFailed = true;
        break;
      }
      if (counts && counts.total === 0) {
        console.error(
          `\nIteration ${i} ran 0 tests — the filter "${suite.filter}" matched nothing.`,
        );
        anyFailed = true;
        break;
      }
    }

    const durations = iterationRecords.map((r) => r.durationMs);
    runs.push({
      suite: key,
      title: suite.title,
      race: suite.race,
      filter: suite.filter,
      project: suite.project,
      requested: iterations,
      completed: iterationRecords.length,
      allPassed:
        iterationRecords.length === iterations && iterationRecords.every((r) => r.exitCode === 0),
      testsPerIteration: iterationRecords[0]?.counts?.total ?? null,
      durationMs: {
        min: Math.min(...durations),
        max: Math.max(...durations),
        total: durations.reduce((a, b) => a + b, 0),
      },
      iterations: iterationRecords,
    });

    if (anyFailed) break;
  }

  const summary = {
    ranAt: new Date().toISOString(),
    host: { platform: process.platform, node: process.version },
    commit:
      spawnSync('git', ['rev-parse', 'HEAD'], { cwd: ROOT, encoding: 'utf8' }).stdout?.trim() ??
      null,
    policy: 'burn-in, not retry — the first failing iteration stops the run and is reported',
    ok: !anyFailed,
    runs,
  };

  const jsonPath = args.json ?? join(OUT, 'summary.json');
  mkdirSync(dirname(jsonPath), { recursive: true });
  writeFileSync(jsonPath, `${JSON.stringify(summary, null, 2)}\n`);

  console.log(`\n${'='.repeat(72)}`);
  for (const run of runs) {
    console.log(
      `${run.allPassed ? 'PASS' : 'FAIL'}  ${run.suite.padEnd(14)} ${run.completed}/${run.requested} iterations · ` +
        `${run.testsPerIteration ?? '?'} tests each · ${(run.durationMs.total / 1000).toFixed(0)}s total`,
    );
  }
  console.log(`Summary: ${posix(jsonPath.replace(`${ROOT}${sep}`, ''))}`);
  console.log('='.repeat(72));

  return anyFailed ? 1 : 0;
}

try {
  process.exit(main());
} catch (error) {
  console.error(`error: ${error.message}`);
  process.exit(1);
}
