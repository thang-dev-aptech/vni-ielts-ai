// One place that knows how to start a child process on both platforms,
// because getting this wrong is silent and platform-specific.
//
// The defect this exists to prevent, found by running the F5.4 drills on
// Windows before trusting them:
//
//     dotnet test … --filter 'WorkerHealthTests|GracefulShutdownTests'
//     → 'GracefulShutdownTests' is not recognized as an internal or external command
//
// The harness had passed `shell: process.platform === 'win32'` for every
// command. With a shell, `cmd.exe` re-parses the already-split argv, so the
// `|` inside a test filter became a pipe and the `;` inside
// `trx;LogFileName=…` became a separator. The drill exited 255 in 0.1s and
// the harness dutifully reported "the drill did not produce its required
// failure" — which was true, and for entirely the wrong reason. A fault
// harness that fails on its own argument quoting is worse than none: it
// teaches people to ignore red.
//
// `shell: true` was there for one real reason, which is that on Windows
// `pnpm`, `npm` and `npx` are `.cmd` shims and Node refuses to spawn them
// without a shell (EINVAL since Node 20's CVE-2024-27980 fix). So the rule
// is: a shell for those, never for a real executable.
//
// Anything that genuinely needs shell syntax — a pipeline, a redirect —
// should be a `bash -c` argv, explicitly, on both platforms.

import { spawnSync } from 'node:child_process';

// The Windows shims. Everything else here (dotnet, node, git, docker, bash)
// is a real .exe and must be spawned without a shell so its arguments survive.
const WINDOWS_SHIMS = new Set(['pnpm', 'npm', 'npx', 'yarn', 'pnpx']);

export function needsShell(command) {
  return (
    process.platform === 'win32' && WINDOWS_SHIMS.has(command.replace(/\.(cmd|bat|exe)$/i, ''))
  );
}

/**
 * Run argv, returning the spawnSync result. `argv[0]` is the executable.
 *
 * @param {string[]} argv
 * @param {object} [options] passed through to spawnSync, minus `shell`
 */
export function runPortable(argv, options = {}) {
  const [command, ...args] = argv;
  const shell = needsShell(command);

  // When a shell IS used, cmd.exe re-parses the arguments, so anything with a
  // metacharacter has to be quoted here or it will be split again. The shims
  // above are only ever called with plain flags in this repository, but a
  // future caller should not have to know that.
  const finalArgs = shell
    ? args.map((arg) => (/[\s&|<>^"();,]/.test(arg) ? `"${arg.replace(/"/g, '\\"')}"` : arg))
    : args;

  return spawnSync(command, finalArgs, { ...options, shell });
}

/** True if the command exists and exits 0 — used for capability probes. */
export function probe(argv) {
  return runPortable(argv, { encoding: 'utf8', stdio: 'pipe' }).status === 0;
}
