import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Stage the synthetic four-module paper for E2E only.
 *
 * Learner dev catalogues use owner content (`exam-1.json`). The synthetic
 * paper lives under test fixtures and is copied here briefly so the E2E API
 * can seed it with `Seed:IncludeSyntheticExams` without putting demo content
 * back into the product catalogue on a normal dev boot.
 */
export default function globalSetup() {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const source = path.join(
    root,
    'backend/tests/Vni.Ielts.Infrastructure.Tests/Content/Fixtures/synthetic-full-1.json',
  );
  const target = path.join(root, 'fixtures/exams/synthetic-full-1.json');
  const marker = path.join(root, 'fixtures/exams/.e2e-staged-synthetic');

  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.copyFileSync(source, target);
  fs.writeFileSync(marker, new Date().toISOString(), 'utf8');
}
