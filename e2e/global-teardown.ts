import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** Remove the E2E-only synthetic catalogue fixture after the suite finishes. */
export default function globalTeardown() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const target = path.join(root, 'fixtures/exams/synthetic-full-1.json');
  const marker = path.join(root, 'fixtures/exams/.e2e-staged-synthetic');

  if (fs.existsSync(marker)) {
    fs.rmSync(target, { force: true });
    fs.rmSync(marker, { force: true });
  }
}
