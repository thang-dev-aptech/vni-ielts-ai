#!/usr/bin/env node
//
// F1.5 — one command from a clean checkout to a running local stack.
//
// Canonical implementation is this file, not bootstrap.sh. `pnpm bootstrap`
// used to call `bash scripts/bootstrap.sh`, which on Windows resolves to
// C:\Windows\System32\bash.exe — the WSL stub. When no distro is installed
// (or Docker's WSL VM is stopped) that stub fails with:
//
//     execvpe(/bin/bash) failed: No such file or directory
//
// Git Bash may exist at Program Files\Git\bin\bash.exe and never be chosen.
// The same Node entrypoint runs on macOS and Windows: `pnpm` is spawned via
// `runPortable` (shell only for Windows .cmd shims; a real binary on macOS),
// and Docker Compose is invoked as argv, never as a bash pipeline.
//
// toolchain → install → generate → start dependencies → readiness, in that
// order, each step depending on the one before it having actually finished.
//
// Usage (macOS and Windows):
//        pnpm bootstrap
//        node scripts/bootstrap.mjs
//        bash scripts/bootstrap.sh   # still works; execs this file
//
import { runPortable } from './lib/spawn-portable.mjs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const COMPOSE = ['docker', 'compose', '-f', path.join('infra', 'docker', 'compose.yaml')];
const MONGO_CONTAINER = 'vni-mongo';
const MINIO_CONTAINER = 'vni-minio';
const READY_TIMEOUT_MS = 90_000;
const POLL_MS = 2_000;

function run(argv) {
  const result = runPortable(argv, { cwd: ROOT, stdio: 'inherit', encoding: 'utf8' });
  if (result.error) {
    console.error(`bootstrap: failed to start ${argv[0]}: ${result.error.message}`);
    process.exit(1);
  }
  if (result.status !== 0) {
    process.exit(result.status ?? 1);
  }
}

function capture(argv) {
  return runPortable(argv, { cwd: ROOT, encoding: 'utf8', stdio: 'pipe' });
}

function inspectHealth(container) {
  const result = capture([
    'docker',
    'inspect',
    '--format',
    '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}',
    container,
  ]);
  if (result.status !== 0) return '';
  return (result.stdout ?? '').trim();
}

function requireDocker() {
  const result = capture(['docker', 'info']);
  if (result.status === 0) return;
  console.error('bootstrap: Docker is not running.');
  console.error('  Start Docker Desktop, wait until it is ready, then re-run pnpm bootstrap.');
  process.exit(1);
}

async function waitUntilHealthy() {
  const deadline = Date.now() + READY_TIMEOUT_MS;
  for (;;) {
    const mongo = inspectHealth(MONGO_CONTAINER);
    const minio = inspectHealth(MINIO_CONTAINER);
    if (mongo === 'healthy' && minio === 'healthy') return;

    if (Date.now() >= deadline) {
      console.error('bootstrap: MongoDB/MinIO did not both report healthy within 90s.');
      console.error(`  ${MONGO_CONTAINER}: ${mongo || '(not found)'}`);
      console.error(`  ${MINIO_CONTAINER}: ${minio || '(not found)'}`);
      runPortable([...COMPOSE, 'logs', '--tail', '30'], {
        cwd: ROOT,
        stdio: 'inherit',
        encoding: 'utf8',
      });
      process.exit(1);
    }

    await new Promise((resolve) => setTimeout(resolve, POLL_MS));
  }
}

export async function main() {
  console.log('bootstrap: 1/5 — toolchain versions...');
  run(['node', 'scripts/check-toolchain-versions.mjs']);

  console.log('bootstrap: 2/5 — installing dependencies (frozen lockfile)...');
  run(['pnpm', 'install', '--frozen-lockfile']);

  console.log('bootstrap: 3/5 — generating the API client from contracts/openapi...');
  run(['pnpm', 'run', 'generate:api-client']);

  console.log('bootstrap: 4/5 — starting MongoDB + MinIO...');
  requireDocker();
  run([...COMPOSE, 'up', '-d']);

  console.log('bootstrap: 5/5 — waiting for both to report healthy...');
  await waitUntilHealthy();

  console.log('');
  console.log('bootstrap: ready.');
  console.log('  pnpm api        — the backend API (http://localhost:5099)');
  console.log('  pnpm dev        — the learner web app');
  console.log('  pnpm dev:admin  — the admin CMS');
  console.log('  pnpm check      — the same checks CI runs');
}

function invokedAsCli() {
  const entry = process.argv[1];
  if (!entry) return false;
  // Resolve first: `node scripts/bootstrap.mjs` on macOS passes a relative
  // argv[1], and pathToFileURL of a relative path is not this file's URL.
  return pathToFileURL(path.resolve(entry)).href === import.meta.url;
}

if (invokedAsCli()) {
  await main();
}
