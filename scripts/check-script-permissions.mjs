#!/usr/bin/env node
//
// F5.2 — every shell script this repository runs is executable in git.
//
// ── The failure this exists to catch ────────────────────────────────────────
//
// A script committed with mode `100644` runs fine on Windows, where the
// executable bit is not consulted, and dies on Linux CI with:
//
//     ../scripts/otel-smoke.sh: Permission denied
//     ##[error]Process completed with exit code 126
//
// <b>And it cannot be fixed by remembering to `chmod +x`.</b> Under Git Bash
// on Windows, MSYS `chmod` does not change the effective mode — the same
// platform behaviour that makes `scripts/restore-drill.sh` unrunnable there.
// So every script authored on Windows is committed non-executable by default,
// and the author has no local signal at all: it works on their machine.
//
// Eight scripts reached `main` this way and the first CI run caught one of
// them. The rest would have failed one at a time, each looking like a new
// problem.
//
// The fix, when this fails:
//     git update-index --chmod=+x scripts/<name>.sh
//
// Usage: node scripts/check-script-permissions.mjs

import { execFileSync } from 'node:child_process';
import path from 'node:path';

const ROOT = path.resolve(import.meta.dirname, '..');

/** `git ls-files -s` → [{ mode, file }] for every tracked shell script. */
export function trackedShellScripts(lsFilesOutput) {
  return lsFilesOutput
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      // 100755 <sha> <stage>\t<path>
      const [meta, file] = line.split('\t');
      return { mode: meta.split(/\s+/)[0], file };
    })
    .filter((e) => e.file?.endsWith('.sh'));
}

/** A tracked `.sh` that git does not consider executable. */
export function nonExecutable(entries) {
  return entries.filter((e) => e.mode !== '100755').map((e) => e.file);
}

function main() {
  const output = execFileSync('git', ['ls-files', '-s', '--', '*.sh'], {
    cwd: ROOT,
    encoding: 'utf8',
  });

  const scripts = trackedShellScripts(output);

  if (scripts.length === 0) {
    // Never pass over an empty set: if the glob stops matching, this check is
    // reporting success over nothing at all.
    console.error('No tracked .sh files were found. Fix the glob rather than trusting this.');
    process.exit(1);
  }

  const broken = nonExecutable(scripts);

  if (broken.length > 0) {
    console.error(`${broken.length} shell script(s) are committed without the executable bit:\n`);
    for (const file of broken) console.error(`  · ${file}`);
    console.error(
      '\nOn Linux CI these fail with "Permission denied" (exit 126). Fix each with:\n' +
        broken.map((f) => `  git update-index --chmod=+x ${f}`).join('\n') +
        '\n\nNote: `chmod +x` alone does nothing under Git Bash on Windows.',
    );
    process.exit(1);
  }

  console.log(`  ${scripts.length} shell script(s), all executable in git`);
}

if (process.argv[1] && import.meta.url.endsWith(path.basename(process.argv[1]))) {
  main();
}
