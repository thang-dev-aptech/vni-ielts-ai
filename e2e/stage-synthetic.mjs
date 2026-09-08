/**
 * Stage the synthetic four-module paper for E2E only.
 *
 * Shared by `global-setup.ts` and the API `webServer` command. The seeder
 * (`DevelopmentExamSeeder`) reads `fixtures/exams` at boot; if this file is
 * missing then, `UnpublishOrphanedFixturesAsync` will *unpublish* a leftover
 * synthetic from a previous run, and every harness call looking for
 * "VNI Synthetic Practice Test" fails. Staging must therefore finish before
 * `dotnet run`, not only in globalSetup (Playwright may start webServers in a
 * window where the staged file is not yet visible to the seeder).
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

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
