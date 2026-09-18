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
// The third, added 2026-09-18 by `W0`: **a drill that never finishes is not a
// drill that is still running.** Every drill has a deadline of its own, and
// missing it is a failure attributed to that drill by name. Before this, the
// only deadline in reach was the CI job's, and when `production-config-live`
// hung for forty-eight minutes the job was cancelled — which produces no
// summary, no artifact and no name, only "the pipeline ran out of time".
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
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const OUT = join(ROOT, '_artifacts', 'drills');
const posix = (p) => p.split(sep).join('/');

export const SECURITY_FIXTURE_PROBES = Object.freeze({
  secret: 'nonzero',
  dependency: 'nonzero',
  image: 'nonzero',
  codeql: 'zero',
});

const SECURITY_FIXTURE_RESULT_PREFIX = 'VNI_SECURITY_FIXTURE_RESULTS=';

// The three command-line scanners signal a finding with a non-zero exit. A
// CodeQL query test has the opposite convention: it exits zero only when its
// actual result tuples match the committed `.expected` finding. Keeping that
// distinction explicit prevents either convention from being inverted by
// accident. The gate command emits one JSON record after it has run all four
// probes; prose in the log is deliberately not accepted as evidence.
export function evaluateSecurityFixtureOutput(output) {
  const lines = String(output).split(/\r?\n/);
  const resultLines = lines.filter((line) => line.startsWith(SECURITY_FIXTURE_RESULT_PREFIX));
  if (resultLines.length !== 1) {
    return {
      satisfied: false,
      reason: `expected exactly one ${SECURITY_FIXTURE_RESULT_PREFIX}<json> record; found ${resultLines.length}`,
      probes: {},
    };
  }

  let document;
  try {
    document = JSON.parse(resultLines[0].slice(SECURITY_FIXTURE_RESULT_PREFIX.length));
  } catch (error) {
    return {
      satisfied: false,
      reason: `invalid security-fixture JSON: ${error.message}`,
      probes: {},
    };
  }

  if (!document || Array.isArray(document) || typeof document !== 'object') {
    return {
      satisfied: false,
      reason: 'security-fixture result must be a JSON object',
      probes: {},
    };
  }

  const probes = {};
  const problems = [];
  const blocked = [];
  for (const [id, expectation] of Object.entries(SECURITY_FIXTURE_PROBES)) {
    const probe = document[id];
    const exitCode = probe?.exitCode;
    const command = probe?.command;
    if (
      probe &&
      !Array.isArray(probe) &&
      typeof probe === 'object' &&
      probe.status === 'blocked' &&
      exitCode === null &&
      typeof command === 'string' &&
      command.trim().length > 0 &&
      typeof probe.reason === 'string' &&
      probe.reason.trim().length > 0
    ) {
      probes[id] = { expectation, exitCode: null, command, matched: false, status: 'blocked' };
      blocked.push(`${id}: ${probe.reason}`);
      continue;
    }
    const validShape =
      probe &&
      !Array.isArray(probe) &&
      typeof probe === 'object' &&
      Number.isInteger(exitCode) &&
      typeof command === 'string' &&
      command.trim().length > 0;
    const matched = validShape && (expectation === 'nonzero' ? exitCode !== 0 : exitCode === 0);
    probes[id] = { expectation, exitCode: validShape ? exitCode : null, command, matched };
    if (!validShape) problems.push(`${id}: missing integer exitCode or non-empty command`);
    else if (!matched) problems.push(`${id}: expected ${expectation}, got exit ${exitCode}`);
  }

  const unexpected = Object.keys(document).filter((id) => !(id in SECURITY_FIXTURE_PROBES));
  if (unexpected.length > 0) problems.push(`unexpected probes: ${unexpected.join(', ')}`);

  return {
    satisfied: problems.length === 0 && blocked.length === 0,
    status: problems.length > 0 ? 'failed' : blocked.length > 0 ? 'blocked' : 'passed',
    reason:
      problems.length > 0 ? problems.join('; ') : blocked.length > 0 ? blocked.join('; ') : null,
    probes,
  };
}

// Every drill gets a deadline, and a drill may shorten it with `timeoutMs`.
//
// <b>Measured 2026-09-18: `production-config-live` ran 05:46:07 → 06:34:26.</b>
// Forty-eight minutes, killed by `timeout-minutes: 60` in
// `.github/workflows/verify.yml`. The job hit the wall, not the drill, so the
// run was cancelled: the summary was never written, `_artifacts/drills/` was
// never uploaded, and the only statement in the log was that the pipeline had
// run out of time. It took a session to find out which of nine drills had not
// come back.
//
// Fifteen minutes is chosen against that number rather than a guess at how
// long a drill takes. It has to be long enough for the slowest legitimate
// drill — `production-config-live` builds the API image; `pitr-drill` has
// measured RTOs of 157s and 168s — and short enough that one stuck drill
// leaves the job budget with room to write the summary, run the security
// report and upload the artifacts. A test asserts the second half of that
// against the workflow's own number, so the two cannot drift apart silently.
export const DEFAULT_DRILL_TIMEOUT_MS = 15 * 60_000;

const TRX = (name) => [
  '--logger',
  `trx;LogFileName=${name}.trx`,
  '--results-directory',
  join(OUT, 'test-results'),
];

// Each drill names the failure it is required to produce. "Runs the health
// tests" is not a drill description; "readiness must go 503 with a code that
// leaks neither credential nor endpoint" is.
export const DRILLS = [
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
      'the API container must refuse to start when an external origin it is handed is plain http, and must name the option it rejected',
    checklistItem: 'F5.4 · F0.3',
    // Deliberately inverted, and deliberately NOT routed through
    // production-smoke.sh: that script hard-codes the valid https values in
    // compose.production.yaml, so there is no seam in it to inject a bad one.
    // `docker compose run -e` overrides a single variable for one container.
    // `--no-deps` is correct here rather than a shortcut — the startup gate
    // runs before any dependency is contacted (`Program.cs` calls
    // `ValidateOrThrow` at line 388, `builder.Build()` at 433), so the refusal
    // must happen with Mongo and MinIO absent. If it does not, the gate is
    // running too late to protect anything.
    //
    // ── W0, 2026-09-18: what this used to inject, and why it hung ─────────
    //
    // <b>It injected `Email__ClientBaseUrl`, and all mail infrastructure was
    // removed on 2026-09-08.</b> Nothing reads an `Email` option any more, so
    // the gate had nothing to refuse, the API booted, `docker compose run`
    // held the container in the foreground and the drill never returned. The
    // drill was asserting a gate that no longer existed — it could not have
    // gone green, and because nothing bounded it, it took the job with it.
    //
    // <b>Retargeted rather than deleted.</b> The guarantee is still worth
    // holding and nothing else holds it: every other configuration drill runs
    // against a test host, and only this one proves the gate fires inside a
    // real Production container, before any dependency exists, in the image
    // that would actually be deployed.
    //
    // Why `Cors:Origins` and not the two candidates the handover suggested:
    //
    //   `Sso:ClientBaseUrl` — its plain-HTTP refusal is gated on Google being
    //   configured (a non-empty ClientId AND ClientSecret), which this compose
    //   file does not configure. One `-e` cannot supply three values, and a
    //   drill that has to plant a fake client secret to fire is testing its
    //   own fixture.
    //
    //   `ObjectStorage:ServiceUrl` — plain HTTP there is a WARNING, not a
    //   refusal. The container would boot and this drill would hang again, in
    //   exactly the shape it just hung in.
    //
    // `Cors:Origins` keeps the drill's original sentence intact: an external
    // URL over plain http must stop a Production boot. Overriding index 0 and
    // leaving index 1 at https matters — the separate "every configured origin
    // is plain HTTP" transport check further down the gate stays silent, so
    // the container refuses for the one named reason this drill is about.
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
      'Cors__Origins__0=http://insecure.smoke.invalid',
      'api',
    ],
    expect: 'nonzero',
    // A non-zero exit alone would also be produced by a failed image build,
    // which would make this inverted drill pass for entirely the wrong
    // reason. The output has to show the gate rejecting the option by name.
    //
    // The whole phrase, not just the option name: `Describe` prints
    // `Cors:Origins = …` on every boot, refusal or not, so matching the name
    // alone would match the healthy configuration dump.
    expectOutputMatches: 'Cors:Origins contains .*over plain HTTP',
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
    title: 'Intentional secret, dependency, image and CodeQL findings',
    requires:
      'the secret, dependency and image scanners must reject their fixtures, and the real CodeQL query test must reproduce its expected finding',
    checklistItem: 'F5.4 · F4.4',
    fromEvidence: 'F4.security-gate-command',
    expect: 'zero',
    validateOutput: evaluateSecurityFixtureOutput,
    dependsOn: 'F4',
    note: 'Owned by F4. The gate must emit one structured VNI_SECURITY_FIXTURE_RESULTS record. CodeQL uses exit 0 because `codeql test run` succeeds only when the real query result matches the committed finding; the other scanners must exit non-zero.',
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

/**
 * Run one planned drill and return its record.
 *
 * Extracted from `main` so the deadline below can be exercised directly: a
 * harness whose only entry point runs nine real drills cannot be tested for
 * what it does with a tenth that never finishes.
 */
export function executeDrill(drill, argv, { log = console.log } = {}) {
  log(`\n-- ${drill.id} — ${drill.title}`);
  log(`   must produce: ${drill.requires}`);
  log(`   $ ${argv.join(' ')}${drill.expect === 'nonzero' ? '   (expected to FAIL)' : ''}`);

  const startedAt = new Date();
  const start = process.hrtime.bigint();
  const capture = Boolean(drill.expectOutputMatches || drill.validateOutput);
  const timeoutMs = drill.timeoutMs ?? DEFAULT_DRILL_TIMEOUT_MS;
  const result = runPortable(argv, {
    cwd: ROOT,
    env: { ...process.env, ...(drill.env ?? {}) },
    stdio: capture ? ['inherit', 'pipe', 'pipe'] : 'inherit',
    encoding: capture ? 'utf8' : undefined,
    timeout: timeoutMs,
    // <b>SIGKILL, and the choice is the whole point of the deadline.</b> A
    // deadline that asks politely is not a deadline: `spawnSync` sends its
    // kill signal once and then goes back to waiting, so a child that ignores
    // SIGTERM would hold the harness exactly as long as a child with no
    // deadline at all. The cost is real and bounded — a SIGKILLed
    // `docker compose run --rm` leaves its container behind, because the
    // client that would have removed it is gone. On a CI runner that is
    // discarded minutes later; on a laptop it is
    // `docker compose -f infra/docker/compose.production.yaml down`.
    killSignal: 'SIGKILL',
  });
  const durationMs = Math.round(Number(process.hrtime.bigint() - start) / 1e6);
  const timedOut = result.error?.code === 'ETIMEDOUT';

  /*
   * <b>`result.status ?? 1` was here, and it is how a hang would have been
   * reported as a pass.</b> A process that was killed at its deadline, or that
   * never started at all, has no exit code — `status` is null. Substituting 1
   * invents a non-zero exit, and `production-config-live` is INVERTED: it is
   * satisfied by a non-zero exit. So the moment a deadline started killing the
   * container that would not stop, the drill whose hang caused all this would
   * have gone green on the strength of the kill.
   *
   * null is carried through instead, and "no exit code" satisfies nothing in
   * either direction.
   */
  const exitCode = result.status ?? null;

  let outputMatched = null;
  let outputValidation = null;
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
    if (drill.expectOutputMatches) {
      outputMatched = new RegExp(drill.expectOutputMatches).test(output);
      if (!outputMatched) {
        log(
          `   the output does not contain /${drill.expectOutputMatches}/ — the command failed, but not for the reason this drill is about.`,
        );
      }
    }
    if (drill.validateOutput) {
      outputValidation = drill.validateOutput(output);
      outputMatched = outputValidation.satisfied;
      if (!outputMatched) {
        log(`   security fixture evidence is incomplete: ${outputValidation.reason}`);
      }
    }
  }

  const outputBlocked = outputValidation?.status === 'blocked';
  const exitSatisfied =
    exitCode !== null && (drill.expect === 'nonzero' ? exitCode !== 0 : exitCode === 0);
  const satisfied = exitSatisfied && (outputMatched === null || outputMatched);
  const recordStatus = outputBlocked ? 'blocked' : satisfied ? 'passed' : 'failed';

  const reason = outputBlocked
    ? outputValidation.reason
    : timedOut
      ? `the command did not exit within its ${(timeoutMs / 1000).toFixed(0)}s deadline and was killed`
      : exitCode === null
        ? `the command produced no exit code: ${result.error?.message ?? 'unknown spawn failure'}`
        : null;

  log(
    `   -> ${exitCode === null ? 'NO EXIT CODE' : `exit ${exitCode}`} in ${(durationMs / 1000).toFixed(1)}s — ${
      outputBlocked
        ? `BLOCKED: ${outputValidation.reason}`
        : timedOut
          ? `THIS DRILL DID NOT EXIT within ${(timeoutMs / 1000).toFixed(0)}s and was killed. ` +
            'A drill with no deadline of its own spends the whole job budget instead.'
          : exitCode === null
            ? `THIS DRILL NEVER RAN: ${result.error?.message ?? 'unknown spawn failure'}`
            : satisfied
              ? 'the required failure was produced'
              : drill.expect === 'nonzero'
                ? 'THE FAULT FIXTURE STOPPED FAILING. A gate has silently opened.'
                : 'the drill did not produce its required failure'
    }`,
  );

  return {
    id: drill.id,
    title: drill.title,
    requires: drill.requires,
    checklistItem: drill.checklistItem,
    status: recordStatus,
    expect: drill.expect,
    expectOutputMatches: drill.expectOutputMatches ?? null,
    outputMatched,
    outputValidation,
    exitCode,
    timedOut,
    timeoutMs,
    startedAt: startedAt.toISOString(),
    durationMs,
    command: argv.join(' '),
    dependsOn: drill.dependsOn ?? null,
    reason,
  };
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

    records.push(executeDrill(drill, argv));
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

const invokedDirectly =
  process.argv[1] && pathToFileURL(resolve(process.argv[1])).href === import.meta.url;
if (invokedDirectly) {
  try {
    process.exit(main());
  } catch (error) {
    console.error(`error: ${error.message}`);
    process.exit(1);
  }
}
