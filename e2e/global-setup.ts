import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Stage the synthetic four-module paper for E2E only.
 *
 * Learner dev catalogues use owner content (`exam-1.json`). The synthetic
 * paper lives under test fixtures and is copied here briefly so the E2E API
 * can seed it with `Seed:IncludeSyntheticExams` without putting demo content
 * back into the product catalogue on a normal dev boot.
 *
 * The actual copy lives in `stage-synthetic.mjs` so the API `webServer`
 * command can run the same step immediately before `dotnet run` — the seeder
 * must see the file at boot, and relying on globalSetup alone left the
 * catalogue without "VNI Synthetic Practice Test".
 */
export default function globalSetup() {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const result = spawnSync(process.execPath, [path.join(here, 'stage-synthetic.mjs')], {
    stdio: 'inherit',
  });
  if (result.status !== 0) {
    throw new Error(`stage-synthetic.mjs exited with status ${result.status}`);
  }
}
