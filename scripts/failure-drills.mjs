#!/usr/bin/env node
//
// F5.4 — the drills. Six named failures, each of which must produce the
// failure it is supposed to produce.
//
// The distinction this harness exists to hold: **a drill that did not run is
// not a drill that passed.** Every one of the six depends on something —
// Docker, a replica set, a MinIO endpoint, a script another phase owns — and
// the tempting shortcut when one of those is absent is to skip it quietly and
// let the summary stay green. That is precisely how a fault-injection suite
// stops being evidence. So a missing dependency produces BLOCKED, BLOCKED is
// not a pass, and the process exits non-zero unless the caller explicitly
// says it is a preparation run.
//
// The second distinction, and the reason `expect` exists: three of these
// drills assert that something SUCCEEDS at failing. A production
// configuration with an `http://` external URL must make the API refuse to
// boot. If that command ever exits 0, the drill has failed even though the
// command "worked" — an intentional fault fixture that stops failing is a
// gate that has silently opened. `expect: 'nonzero'` inverts the check and
// says so in the output.
//
// Usage:
//   node scripts/failure-drills.mjs --list
//   node scripts/failure-drills.mjs                       every applicable drill
//   node scripts/failure-drills.mjs --drill=mongo-connection-loss
//   node scripts/failure-drills.mjs --allow-blocked       pre-gate preparation
//
// Exit codes: 0 every drill produced its required failure · 1 a drill did not
// (including a fault fixture that stopped failing) · 2 nothing failed but
// drills were blocked or unavailable, so the run certifies nothing.

import { spawnSync } from 'node:child_process';
import { runPortable } from './lib/spawn-portable.mjs';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const OUT = join(ROOT, '_artifacts', 'drills');
const posix = (p) => p.split(sep).join('/');

const TRX = (name) => [
  '--logger',
  `trx;LogFileName=${name}.trx`,
  '--results-directory',
  join(OUT, 'test-results'),
];

// Each drill names the failure it is required to produce. "Runs the health
// tests" is not a drill description; "readiness must go 503 with a code that
// leaks neither credential nor endpoint" is.
const DRILLS = [
  {
    id: 'object-storage-credential',
    title: 'Wrong object-storage credential',
    requires: 'readiness reports 503 with a safe error code — never 200, and never the access key',
    checklistItem: 'F5.4 · F0.1',
    argv: [
      'dotnet',
      'test',
      'backend/tests/Vni.Ielts.Integration.Tests',
      '--configuration',
      'Release',
      '--nologo',
      '--filter',
      'ObjectStorageHealthTests',
      ...TRX('object-storage-credential'),
    ],
    env: { VNI_REQUIRE_MINIO: '1', VNI_REQUIRE_MONGO: '1' },
    expect: 'zero',
    needsMongo: true,
    evidence: ['backend/tests/Vni.Ielts.Integration.Tests/ObjectStorageHealthTests.cs'],
  },
  {
    id: 'mongo-connection-loss',
    title: 'MongoDB becomes unreachable after boot',
    requires:
      'readiness flips to unhealthy inside a finite timeout, and the response body says nothing about the connection string',
    checklistItem: 'F5.4 · F2.1',
    argv: [
      'dotnet',
      'test',
      'backend/tests/Vni.Ielts.Integration.Tests',
      '--configuration',
      'Release',
      '--nologo',
      '--filter',
      'HealthFaultTests',
      ...TRX('mongo-connection-loss'),
    ],
    env: { VNI_REQUIRE_MONGO: '1' },
    expect: 'zero',
    needsMongo: true,
    evidence: ['backend/tests/Vni.Ielts.Integration.Tests/HealthFaultTests.cs'],
  },
  {
    id: 'worker-loop-dead',
    title: 'The worker process lives but its polling loop is dead',
    requires:
      'readiness must go unhealthy while the process is still running — an empty queue stays healthy, a dead loop does not',
    checklistItem: 'F5.4 · F2.2',
    argv: [
      'dotnet',
      'test',
      'backend/tests/Vni.Ielts.Worker.Tests',
      '--configuration',
      'Release',
      '--nologo',
      '--filter',
      'WorkerHealthTests|GracefulShutdownTests',
      ...TRX('worker-loop-dead'),
    ],
    env: { VNI_REQUIRE_MONGO: '1' },
    expect: 'zero',
    needsMongo: true,
    evidence: ['backend/tests/Vni.Ielts.Worker.Tests/WorkerHealthTests.cs'],
  },
  {
    id: 'production-config-bad',
    title: 'Production configuration that must be refused',
    requires:
      'an http:// external URL, a wildcard CORS origin or a default signing key must fail startup, and the message must name the option without printing its value',
    checklistItem: 'F5.4 · F2.5',
    argv: [
      'dotnet',
      'test',
      'backend/tests/Vni.Ielts.Integration.Tests',
      '--configuration',
      'Release',
      '--nologo',
      '--filter',
      'StartupConfigurationTests',
      ...TRX('production-config-bad'),
    ],
    env: { VNI_REQUIRE_MONGO: '1' },
    expect: 'zero',
    needsMongo: true,
    evidence: ['backend/tests/Vni.Ielts.Integration.Tests/StartupConfigurationTests.cs'],
  },
  {
    id: 'production-config-live',
    title: 'The same refusal, in a real container rather than a test host',
    requires:
      'the API container must refuse to start when its external URL is plain http, and must name the option it rejected',
    checklistItem: 'F5.4 · F0.3',
    // Deliberately inverted, and deliberately NOT routed through
    // production-smoke.sh: that script hard-codes the valid https values in
    // compose.production.yaml, so there is no seam in it to inject a bad one.
    // `docker compose run -e` overrides a single variable for one container.
    // `--no-deps` is correct here rather than a shortcut — the startup gate
    // runs before any dependency is contacted, so the refusal must happen
    // with Mongo and MinIO absent. If it does not, the gate is running too
    // late to protect anything.
    argv: [
      'docker',
      'compose',
      '-f',
      'infra/docker/compose.production.yaml',
      'run',
      '--rm',
      '--build',
      '--no-deps',
      '-e',
      'Email__ClientBaseUrl=http://insecure.smoke.invalid',
      'api',
    ],
    expect: 'nonzero',
    // A non-zero exit alone would also be produced by a failed image build,
    // which would make this inverted drill pass for entirely the wrong
    // reason. The output has to show the gate rejecting the option by name.
    expectOutputMatches: 'ClientBaseUrl',
    needsDocker: true,
    optIn: true,
    evidence: ['infra/docker/compose.production.yaml', 'backend/Dockerfile'],
    // <b>The note below said this must be set; nothing set it.</b> Run with
    // `--include-live` the drill died in 313 ms on
    //
    //   error while interpolating services.api.environment.Jwt__SigningKey:
    //   required variable VNI_JWT_SIGNING_KEY is missing a value
    //
    // — compose refusing to interpolate, long before an image was built or a
    // startup gate ran. `expectOutputMatches` is what caught it: the exit code
    // was 1, exactly as the drill expects, and only the missing `ClientBaseUrl`
    // in the output kept it from passing for the wrong reason. That assertion
    // earned its place here.
    //
    // The variable under test is the URL. Supplying a valid key isolates it, so
    // the drill fails for the one reason it exists to prove. A caller's own key
    // wins where there is one — `verify.yml` sets a per-run value.
    env: {
      VNI_JWT_SIGNING_KEY:
        process.env.VNI_JWT_SIGNING_KEY ?? 'failure-drill-only-not-a-secret-0000000000',
    },
    note: 'Opt-in (--include-live). It builds the API image. VNI_JWT_SIGNING_KEY is supplied by this drill, because compose treats it as required — the drill must fail on the http URL, not on a missing secret.',
  },
  {
    id: 'dependency-timeout',
    title: 'A required dependency that hangs rather than refusing',
    requires:
      'readiness must resolve within its configured timeout instead of hanging with it — a probe with no deadline is a liveness bug that only appears under a network partition',
    checklistItem: 'F5.4 · F2.1',
    argv: [
      'dotnet',
      'test',
      'backend/tests/Vni.Ielts.Integration.Tests',
      '--configuration',
      'Release',
      '--nologo',
      '--filter',
      'StartupAndHealthTests|KestrelTransportTests',
      ...TRX('dependency-timeout'),
    ],
    env: { VNI_REQUIRE_MONGO: '1' },
    expect: 'zero',
    needsMongo: true,
    evidence: ['backend/tests/Vni.Ielts.Integration.Tests/StartupAndHealthTests.cs'],
  },
  {
    id: 'restore-drill',
    title: 'Encrypted backup, restored into an isolated database',
    requires: 'document count and checksum must match, and the source database must be untouched',
    checklistItem: 'F5.4 · F3.4',
    argv: ['bash', 'scripts/restore-drill.sh'],
    expect: 'zero',
    needsDocker: true,
    needsBash: true,
    dependsOn: 'F3',
    evidence: ['scripts/restore-drill.sh'],
    // POSIX-only, and the reason is measured rather than assumed. Under Git
    // Bash on Windows `chmod 600` does not take: the file stays 644, so
    // scripts/backup.sh's "a key file the whole machine can read is not a
    // secret" guard trips and the drill exits 2. Reproduced directly:
    //     $ chmod 600 "$T/k"; stat -c '%a' "$T/k"   ->  644
    //     $ find "$T/k" -perm /044                  ->  matches
    // Reporting that as a failed drill would be a lie about the backup path,
    // which runs green on Linux CI today. NOT-APPLICABLE still makes the run
    // PARTIAL, so it can never be mistaken for evidence.
    platforms: ['linux', 'darwin'],
    // <b>The container name is a default, not a constant.</b> It was written
    // as a bare `vni-mongo` — the compose name — and on a runner that had
    // started its own container the drill died with
    //
    //   drill: docker exec -i vni-mongo mongosh cannot reach
    //   mongodb://localhost:27017/?directConnection=true          exit 2
    //
    // reported as "the drill did not produce its required failure", which is
    // the worst available reading: the backup path was fine and the harness
    // said otherwise. `VNI_DRILL_SOURCE` is the same variable `pitr-drill.sh`
    // already uses for this, so the two agree by construction.
    env: {
      VNI_MONGODUMP: 'docker run --rm --network host mongo:7 mongodump',
      VNI_MONGORESTORE: 'docker run --rm -i --network host mongo:7 mongorestore',
      VNI_MONGOSH: `docker exec -i ${process.env.VNI_DRILL_SOURCE ?? 'vni-mongo'} mongosh`,
      VNI_MONGOSH_URI: 'mongodb://localhost:27017/?directConnection=true',
    },
  },
  {
    id: 'pitr-drill',
    title: 'Point-in-time restore to a chosen instant',
    requires:
      'data written after the target instant must be absent from the restore, and RPO/RTO must be measured rather than asserted',
    checklistItem: 'F5.4 · F3.4',
    argv: ['bash', 'scripts/pitr-drill.sh'],
    expect: 'zero',
    needsDocker: true,
    needsBash: true,
    optIn: true,
    dependsOn: 'F3',
    evidence: ['scripts/pitr-drill.sh'],
    // <b>NOT platform-restricted, and the restriction that was here was
    // inherited rather than measured.</b> It read
    // `platforms: ['linux','darwin'] // same MSYS chmod constraint as
    // restore-drill`, but that constraint does not exist in this script:
    // `restore-drill.sh` goes through `backup.sh`, whose gpg key-permission
    // guard is what MSYS `chmod` defeats, while this one drives PBM and
    // Docker and never touches a key file. Verified two ways — `grep -E
    // 'chmod|BACKUP_KEY|gpg|-perm' scripts/pitr-drill.sh` finds nothing, and
    // the script has been run to completion on this win32 host twice (RTO
    // 157s and 168s, F3.4). Excluding it here silently dropped the one drill
    // that measures RPO and RTO.
    note: 'Opt-in (--include-live). It takes minutes and drives the shared PBM agent, which another agent may be holding.',
  },
  {
    id: 'security-fixture',
    title: 'A deliberately planted credential and a known-vulnerable dependency',
    requires:
      'the secret scan, the dependency audit and the image scan must each fail on their own fixture',
    checklistItem: 'F5.4 · F4.4',
    fromEvidence: 'F4.security-gate-command',
    expect: 'zero',
    dependsOn: 'F4',
    note: 'Owned by F4. This harness declares the drill so its absence is visible; it does not invent the gate.',
  },
];

function probe(command, args) {
  return runPortable([command, ...args], { encoding: 'utf8', stdio: 'pipe' }).status === 0;
}

const ENV = {
  get docker() {
    if (this._d === undefined) this._d = probe('docker', ['version', '--format', '{{.Server.Os}}']);
    return this._d;
  },
  get bash() {
    if (this._b === undefined) this._b = probe('bash', ['-c', 'true']);
    return this._b;
  },
};

function loadEvidenceCommand(ref) {
  const manifest = join(ROOT, 'ci', 'foundation-evidence.json');
  if (!existsSync(manifest)) return { argv: null, missing: ['ci/foundation-evidence.json'] };
  const doc = JSON.parse(readFileSync(manifest, 'utf8'));
  const [phase, id] = ref.split('.');
  const entry = doc.phases?.[phase]?.evidence?.find((e) => e.id === id);
  if (!entry) return { argv: null, missing: [`ci/foundation-evidence.json -> ${ref}`] };
  const candidates = entry.anyOf ?? (entry.path ? [entry.path] : []);
  const present = candidates.filter((p) => existsSync(join(ROOT, p)));
  if (present.length === 0 || !entry.command) return { argv: null, missing: candidates };
  return { argv: entry.command.split(' '), missing: [] };
}

function parseArgs(argv) {
  const args = {
    drill: null,
    list: false,
    allowBlocked: false,
    includeLive: false,
    json: join(OUT, 'summary.json'),
  };
  for (const raw of argv) {
    const [key, ...rest] = raw.split('=');
    const value = rest.join('=');
    switch (key) {
      case '--drill':
        args.drill = value;
        break;
      case '--list':
        args.list = true;
        break;
      case '--allow-blocked':
        args.allowBlocked = true;
        break;
      case '--include-live':
        args.includeLive = true;
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

function planDrill(drill, args) {
  const resolved = drill.fromEvidence
    ? loadEvidenceCommand(drill.fromEvidence)
    : { argv: drill.argv, missing: [] };
  const missingEvidence = (drill.evidence ?? []).filter((p) => !existsSync(join(ROOT, p)));

  if (args.drill && drill.id !== args.drill)
    return { status: 'skipped', reason: '--drill', argv: resolved.argv };
  if (drill.optIn && !args.includeLive && !args.drill) {
    return {
      status: 'skipped',
      reason: 'opt-in; pass --include-live or --drill=<id>',
      argv: resolved.argv,
    };
  }
  if (missingEvidence.length > 0) {
    return {
      status: 'blocked',
      reason: `missing ${drill.dependsOn ?? 'required'} evidence: ${missingEvidence.join(', ')}`,
      argv: resolved.argv,
    };
  }
  if (!resolved.argv) {
    return {
      status: 'blocked',
      reason: `${drill.dependsOn ?? 'a dependency'} has not produced: ${resolved.missing.join(' | ')}`,
      argv: null,
    };
  }
  if (drill.platforms && !drill.platforms.includes(process.platform)) {
    return {
      status: 'not-applicable',
      reason: `declared for ${drill.platforms.join(', ')}; this host is ${process.platform}`,
      argv: resolved.argv,
    };
  }
  if (drill.needsDocker && !ENV.docker)
    return { status: 'unavailable', reason: 'no reachable Docker daemon', argv: resolved.argv };
  if (drill.needsBash && !ENV.bash)
    return { status: 'unavailable', reason: 'no bash on this host', argv: resolved.argv };
  return { status: 'ready', reason: null, argv: resolved.argv };
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  mkdirSync(join(OUT, 'test-results'), { recursive: true });

  const planned = DRILLS.map((drill) => ({ drill, ...planDrill(drill, args) }));

  if (args.list) {
    console.log('Failure drills — each one names the failure it is required to produce.\n');
    for (const { drill, status, reason } of planned) {
      console.log(`${status.toUpperCase().padEnd(12)} ${drill.id.padEnd(26)} ${drill.title}`);
      console.log(`${' '.repeat(13)}must produce: ${drill.requires}`);
      if (drill.expect === 'nonzero')
        console.log(`${' '.repeat(13)}INVERTED: this drill fails if the command succeeds.`);
      if (reason) console.log(`${' '.repeat(13)}-> ${reason}`);
      console.log('');
    }
    return 0;
  }

  const records = [];
  for (const entry of planned) {
    const { drill, status, reason, argv } = entry;
    if (status !== 'ready') {
      console.log(`\n-- ${drill.id} — ${status.toUpperCase()}${reason ? ` (${reason})` : ''}`);
      records.push({
        id: drill.id,
        title: drill.title,
        requires: drill.requires,
        checklistItem: drill.checklistItem,
        status,
        reason,
        dependsOn: drill.dependsOn ?? null,
        command: argv ? argv.join(' ') : null,
      });
      continue;
    }

    console.log(`\n-- ${drill.id} — ${drill.title}`);
    console.log(`   must produce: ${drill.requires}`);
    console.log(
      `   $ ${argv.join(' ')}${drill.expect === 'nonzero' ? '   (expected to FAIL)' : ''}`,
    );

    const startedAt = new Date();
    const start = process.hrtime.bigint();
    const capture = Boolean(drill.expectOutputMatches);
    const result = runPortable(argv, {
      cwd: ROOT,
      env: { ...process.env, ...(drill.env ?? {}) },
      stdio: capture ? ['inherit', 'pipe', 'pipe'] : 'inherit',
      encoding: capture ? 'utf8' : undefined,
    });
    const durationMs = Math.round(Number(process.hrtime.bigint() - start) / 1e6);
    const exitCode = result.status ?? 1;

    let outputMatched = null;
    if (capture) {
      const output = `${result.stdout ?? ''}${result.stderr ?? ''}`;
      const logPath = join(OUT, `${drill.id}.log`);
      writeFileSync(logPath, output);
      process.stdout.write(
        output.length > 4000
          ? `${output.slice(-4000)}
… (truncated; full log at ${posix(logPath.replace(`${ROOT}${sep}`, ''))})
`
          : output,
      );
      outputMatched = new RegExp(drill.expectOutputMatches).test(output);
      if (!outputMatched) {
        console.log(
          `   the output does not contain /${drill.expectOutputMatches}/ — the command failed, but not for the reason this drill is about.`,
        );
      }
    }

    const exitSatisfied = drill.expect === 'nonzero' ? exitCode !== 0 : exitCode === 0;
    const satisfied = exitSatisfied && (outputMatched === null || outputMatched);

    console.log(
      `   -> exit ${exitCode} in ${(durationMs / 1000).toFixed(1)}s — ${
        satisfied
          ? 'the required failure was produced'
          : drill.expect === 'nonzero'
            ? 'THE FAULT FIXTURE STOPPED FAILING. A gate has silently opened.'
            : 'the drill did not produce its required failure'
      }`,
    );

    records.push({
      id: drill.id,
      title: drill.title,
      requires: drill.requires,
      checklistItem: drill.checklistItem,
      status: satisfied ? 'passed' : 'failed',
      expect: drill.expect,
      expectOutputMatches: drill.expectOutputMatches ?? null,
      outputMatched,
      exitCode,
      startedAt: startedAt.toISOString(),
      durationMs,
      command: argv.join(' '),
      dependsOn: drill.dependsOn ?? null,
    });
  }

  const failed = records.filter((r) => r.status === 'failed');
  const blocked = records.filter((r) => r.status === 'blocked');
  const notRun = records.filter((r) => !['passed', 'failed'].includes(r.status));
  const verdict = failed.length > 0 ? 'FAIL' : notRun.length > 0 ? 'PARTIAL' : 'PASS';

  const summary = {
    verdict,
    certifies:
      verdict === 'PASS'
        ? 'every declared drill ran and produced the failure it is required to produce'
        : 'nothing — drills were blocked, unavailable, skipped or failed',
    ranAt: new Date().toISOString(),
    host: { platform: process.platform, docker: ENV.docker, bash: ENV.bash },
    commit:
      spawnSync('git', ['rev-parse', 'HEAD'], { cwd: ROOT, encoding: 'utf8' }).stdout?.trim() ??
      null,
    counts: {
      total: records.length,
      passed: records.filter((r) => r.status === 'passed').length,
      failed: failed.length,
      blocked: blocked.length,
      notRun: notRun.length,
    },
    blockedOn: [...new Set(blocked.map((r) => r.dependsOn).filter(Boolean))],
    artifacts: { testResults: posix(join('_artifacts', 'drills', 'test-results')) },
    drills: records,
  };

  mkdirSync(dirname(args.json), { recursive: true });
  writeFileSync(args.json, `${JSON.stringify(summary, null, 2)}\n`);

  console.log(`\n${'='.repeat(72)}`);
  console.log(
    `VERDICT: ${verdict}  (${summary.counts.passed} produced their failure · ${failed.length} did not · ${notRun.length} not run)`,
  );
  console.log(`Certifies: ${summary.certifies}`);
  for (const record of blocked) console.log(`  BLOCKED  ${record.id.padEnd(26)} ${record.reason}`);
  console.log(`Summary: ${posix(args.json.replace(`${ROOT}${sep}`, ''))}`);
  console.log('='.repeat(72));

  if (failed.length > 0) return 1;
  if (verdict === 'PARTIAL') {
    if (args.allowBlocked) {
      console.log('\n--allow-blocked: exiting 0. This run is NOT drill evidence for the F5 gate.');
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
