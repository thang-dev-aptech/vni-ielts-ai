#!/usr/bin/env node
//
// F1.1 — one Node/pnpm version, declared once, verified everywhere it is
// re-declared.
//
// This is exactly the bug it exists to catch: `.nvmrc`, package.json's
// `engines.node` and `.github/workflows/frontend.yml` all named Node 24, and
// `.github/workflows/e2e.yml` separately named Node 22 — four independent
// copies of one fact, and nothing compared them. A frontend job and the
// browser suite job could then run the same test suite on two different Node
// majors with no error anywhere, until a version-specific difference actually
// bit somebody and nobody knew where to look first.
//
// Node, not Python — this repo's docs/config checks are moving off `python3`
// (F1.3) because it is not guaranteed present, and Node always is here. This
// one has no reason to depend on anything else at all: no third-party parser,
// just `.nvmrc`, JSON and a line-oriented read of the workflow YAML.
//
// Usage: node scripts/check-toolchain-versions.mjs

import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

const problems = [];

const nvmrc = readFileSync(path.join(root, '.nvmrc'), 'utf8').trim();
if (!/^\d+$/.test(nvmrc)) {
  problems.push(`.nvmrc contains '${nvmrc}', which is not a plain major version number.`);
}
const expectedMajor = nvmrc;

const pkg = JSON.parse(readFileSync(path.join(root, 'package.json'), 'utf8'));

const engineNode = pkg.engines?.node;
if (!engineNode) {
  problems.push(
    'package.json has no engines.node — nothing pins the Node version for `npm install`/`pnpm install` outside CI.',
  );
} else {
  const match = engineNode.match(/(\d+)\.0\.0/);
  if (!match || match[1] !== expectedMajor) {
    problems.push(
      `package.json engines.node is '${engineNode}', which does not name major version ${expectedMajor} the way .nvmrc does.`,
    );
  }
}

const packageManager = pkg.packageManager;
if (!packageManager || !packageManager.startsWith('pnpm@')) {
  problems.push(
    `package.json packageManager is '${packageManager ?? '(missing)'}', expected a pinned 'pnpm@<version>'.`,
  );
}

// <b>Every workflow that declares a node-version, not a hard-coded list of
// two files.</b> A hard-coded list is exactly how this drifted in the first
// place — a third workflow gaining its own `node-version:` tomorrow would
// again go unchecked. Scanning the directory means a fifth workflow with the
// same mistake is caught the same way the fourth was.
const workflowsDir = path.join(root, '.github', 'workflows');
const workflowFiles = readdirSync(workflowsDir).filter(
  (f) => f.endsWith('.yml') || f.endsWith('.yaml'),
);

for (const file of workflowFiles) {
  const contents = readFileSync(path.join(workflowsDir, file), 'utf8');

  for (const line of contents.split(/\r?\n/)) {
    const found = line.match(/^\s*node-version:\s*['"]?(\d+)['"]?\s*$/);
    if (!found) continue;

    if (found[1] !== expectedMajor) {
      problems.push(
        `.github/workflows/${file} pins node-version '${found[1]}', which does not match .nvmrc's ${expectedMajor}.`,
      );
    }
  }
}

if (problems.length > 0) {
  console.error(
    `Toolchain version check failed (${problems.length} problem${problems.length === 1 ? '' : 's'}):\n`,
  );
  for (const p of problems) console.error(`  · ${p}`);
  console.error(
    '\nOne Node/pnpm version, declared in .nvmrc, has to be the version every other file that names one agrees with.',
  );
  process.exit(1);
}

console.log(
  `OK — Node ${expectedMajor} agrees across .nvmrc, package.json and ${workflowFiles.length} workflow file(s).`,
);
