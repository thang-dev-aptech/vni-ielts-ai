#!/usr/bin/env node
//
// F5.1 — generated artifacts drift, and the drift is invisible until a
// client and a server disagree in production.
//
// There are three links in this chain and, before this file, only two of
// them were guarded:
//
//   the running API  ⇄  contracts/openapi/v1.json   — OpenApiContractTests
//   v1.json          ⇄  packages/api-client         — nothing
//   api-client       ⇄  hand-written client types   — pnpm typecheck
//
// The middle link was "the frontend job runs the generator before it
// typechecks", which is not a drift check: it regenerates, so it can never
// notice that what was there before was stale. That mattered on this repo
// specifically because the generated directory is untracked — the CI runner
// always starts from nothing and always agrees with itself, while a
// developer's working tree can hold a schema.ts generated from a v1.json
// that has since moved.
//
// Modes:
//   --mode=tracked     generated output must not be tracked in git (hand
//                      edits are a build failure, not a patch)
//   --mode=client      regenerate the API client and fail if the bytes moved
//   --mode=snapshot    write a hash manifest of the watched paths
//   --mode=compare     re-hash and fail if anything moved since a manifest
//   --mode=all         tracked + client   (the default)
//
// snapshot/compare is how the OpenAPI link is guarded inside a pipeline
// without depending on a clean git worktree: OpenApiContractTests writes the
// new document into the working tree before it fails, so a v1.json whose
// hash changed across the backend stage IS the drift, whatever else is
// uncommitted around it.
//
// Exit codes: 0 no drift · 1 drift, a tracked generated file, or a failed
// generator.

import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// The generated artifacts this repository owns, and the command that
// reproduces each one. Adding a generated artifact means adding a row here;
// leaving one out is how the next stale file gets missed.
const ARTIFACTS = [
  {
    id: 'openapi',
    path: 'contracts/openapi/v1.json',
    producedBy:
      'dotnet test backend/tests/Vni.Ielts.Integration.Tests --filter OpenApiContractTests',
    regenerable: false, // needs a built API; snapshot/compare guards it instead
  },
  {
    id: 'api-client',
    path: 'packages/api-client/src/generated',
    producedBy: 'pnpm --filter @vni/api-client run generate',
    regenerable: true,
    mustBeUntracked: true,
  },
];

const posix = (p) => p.split(sep).join('/');

function parseArgs(argv) {
  const args = {
    mode: 'all',
    manifest: join(ROOT, '_artifacts', 'verify', 'generated-manifest.json'),
    paths: null,
    json: null,
  };
  for (const raw of argv) {
    const [key, ...rest] = raw.split('=');
    const value = rest.join('=');
    switch (key) {
      case '--mode':
        args.mode = value;
        break;
      case '--manifest':
        args.manifest = value;
        break;
      case '--paths':
        // Fixture seam: lets the tracked-file check be pointed at a path that
        // IS tracked, so its red path can be proven without staging anything.
        args.paths = value.split(',').filter(Boolean);
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

function filesUnder(absolute) {
  if (!existsSync(absolute)) return [];
  const st = statSync(absolute);
  if (st.isFile()) return [absolute];
  const out = [];
  for (const entry of readdirSync(absolute)) {
    out.push(...filesUnder(join(absolute, entry)));
  }
  return out.sort();
}

function hashPath(relPath) {
  const absolute = join(ROOT, relPath);
  const files = filesUnder(absolute);
  if (files.length === 0) return { present: false, files: 0, hash: null };
  const digest = createHash('sha256');
  for (const file of files) {
    digest.update(posix(relative(ROOT, file)));
    digest.update('\0');
    // Newline-normalized on purpose. `.gitattributes` forces LF at checkout,
    // but a generator run on Windows can still emit CRLF into an untracked
    // file, and "the line endings differ" is not the drift this gate is for.
    digest.update(readFileSync(file, 'utf8').replace(/\r\n/g, '\n'));
    digest.update('\0');
  }
  return { present: true, files: files.length, hash: digest.digest('hex') };
}

function snapshot(paths) {
  const entries = {};
  for (const p of paths) entries[p] = hashPath(p);
  return { takenAt: new Date().toISOString(), entries };
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: ROOT,
    stdio: 'inherit',
    shell: process.platform === 'win32',
    ...options,
  });
  return result.status ?? 1;
}

// ── Checks ─────────────────────────────────────────────────────────────────

function checkTracked(problems, paths) {
  for (const p of paths) {
    const result = spawnSync('git', ['ls-files', '--error-unmatch', p], {
      cwd: ROOT,
      encoding: 'utf8',
    });
    const tracked = result.status === 0 && result.stdout.trim().length > 0;
    if (tracked) {
      problems.push(
        `${p} is tracked in git. It is generated output — a hand edit there is a build failure, not a patch. Untrack it and let the generator be its only author.`,
      );
    } else {
      console.log(`OK — ${p} is not tracked in git.`);
    }
  }
}

function checkClientRegenerates(problems) {
  const artifact = ARTIFACTS.find((a) => a.id === 'api-client');
  const before = hashPath(artifact.path);
  if (!before.present) {
    console.log(`${artifact.path} is absent; generating it for the first time.`);
  }

  const status = run('pnpm', ['--filter', '@vni/api-client', 'run', 'generate']);
  if (status !== 0) {
    problems.push(
      `The API client generator exited ${status}. A generated artifact that cannot be reproduced is drift by definition.`,
    );
    return;
  }

  const after = hashPath(artifact.path);
  if (!after.present) {
    problems.push(`${artifact.path} does not exist after running the generator.`);
    return;
  }
  if (before.present && before.hash !== after.hash) {
    problems.push(
      `${artifact.path} changed when it was regenerated from contracts/openapi/v1.json.\n` +
        `  before: ${before.hash}\n  after:  ${after.hash}\n` +
        `  The checked-out generated client did not match its source. Re-run: ${artifact.producedBy}`,
    );
    return;
  }
  console.log(
    `OK — ${artifact.path} reproduces byte-identically (${after.files} file(s), ${after.hash.slice(0, 12)}…).`,
  );
}

function compareAgainstManifest(problems, manifestPath) {
  if (!existsSync(manifestPath)) {
    problems.push(
      `No manifest at ${posix(relative(ROOT, manifestPath))}. Run --mode=snapshot before the stage that could rewrite a generated file.`,
    );
    return;
  }
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  for (const [p, before] of Object.entries(manifest.entries)) {
    const after = hashPath(p);
    if (before.hash !== after.hash) {
      problems.push(
        `${p} changed during the run.\n  before: ${before.hash ?? '<absent>'}\n  after:  ${after.hash ?? '<absent>'}\n` +
          `  A generated artifact rewritten mid-pipeline means the committed copy was stale. ` +
          `${ARTIFACTS.find((a) => a.path === p)?.producedBy ?? ''}`,
      );
    } else {
      console.log(
        `OK — ${p} unchanged since the snapshot (${before.hash ? `${before.hash.slice(0, 12)}…` : 'absent, still absent'}).`,
      );
    }
  }
}

// ── Main ───────────────────────────────────────────────────────────────────

function main() {
  const args = parseArgs(process.argv.slice(2));
  const problems = [];
  const watched = args.paths ?? ARTIFACTS.map((a) => a.path);

  switch (args.mode) {
    case 'tracked':
      checkTracked(
        problems,
        args.paths ?? ARTIFACTS.filter((a) => a.mustBeUntracked).map((a) => a.path),
      );
      break;
    case 'client':
      checkClientRegenerates(problems);
      break;
    case 'snapshot': {
      const doc = snapshot(watched);
      mkdirSync(dirname(args.manifest), { recursive: true });
      writeFileSync(args.manifest, `${JSON.stringify(doc, null, 2)}\n`);
      console.log(`Snapshot written to ${posix(relative(ROOT, args.manifest))}:`);
      for (const [p, e] of Object.entries(doc.entries)) {
        console.log(
          `  ${p}  ${e.hash ? `${e.hash.slice(0, 12)}… (${e.files} file(s))` : '<absent>'}`,
        );
      }
      break;
    }
    case 'compare':
      compareAgainstManifest(problems, args.manifest);
      break;
    case 'all':
      checkTracked(
        problems,
        ARTIFACTS.filter((a) => a.mustBeUntracked).map((a) => a.path),
      );
      checkClientRegenerates(problems);
      break;
    default:
      throw new Error(`Unknown --mode=${args.mode}`);
  }

  if (args.json) {
    mkdirSync(dirname(args.json), { recursive: true });
    writeFileSync(
      args.json,
      `${JSON.stringify({ checkedAt: new Date().toISOString(), mode: args.mode, watched, problems, ok: problems.length === 0 }, null, 2)}\n`,
    );
  }

  if (problems.length > 0) {
    for (const problem of problems) console.error(`error: ${problem}`);
    console.error(`\n${problems.length} generated-artifact problem(s).`);
    process.exit(1);
  }
  // `snapshot` records; it does not judge. Saying "no drift" there would be
  // a pass nobody earned.
  console.log(
    args.mode === 'snapshot'
      ? 'Snapshot recorded. Run --mode=compare after the stage that could rewrite these files.'
      : `OK — no generated-artifact drift (mode: ${args.mode}).`,
  );
}

try {
  main();
} catch (error) {
  console.error(`error: ${error.message}`);
  process.exit(1);
}
