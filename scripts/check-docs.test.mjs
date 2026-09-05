// F1.3 phase gate — "Path có dấu cách, Unicode và separator Windows được đưa
// vào regression fixture của docs checker."
//
// Every fixture here is built fresh in a throwaway temp directory and torn
// down after, so this suite proves the checker's own behaviour rather than
// asserting on whatever this repository's docs/ happens to contain today.
//
// Run: node --test scripts/check-docs.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const CHECKER = path.resolve(import.meta.dirname, 'check-docs.mjs');

function makeFixture(files) {
  const root = mkdtempSync(path.join(tmpdir(), 'vni-docs-check-'));
  for (const [rel, content] of Object.entries(files)) {
    const full = path.join(root, rel);
    mkdirSync(path.dirname(full), { recursive: true });
    writeFileSync(full, content, 'utf8');
  }
  return root;
}

function run(root) {
  return spawnSync(process.execPath, [CHECKER], {
    encoding: 'utf8',
    env: { ...process.env, VNI_DOCS_CHECK_ROOT: root },
  });
}

// The two registries checkSources/checkRequirementIds read unconditionally,
// and checkCanonical requires docs/README.md to carry both marker sentences
// (it is the one file that must). Kept minimal and valid so a fixture's
// *actual* scenario is what fails or passes, not a complaint about a check
// this test isn't about.
const MINIMAL_REGISTRIES = {
  'docs/requirements/confirmed.md': '# Confirmed\n\nNothing here yet.\n',
  'docs/requirements/assumptions-and-open-questions.md': '# Open questions\n\nNothing here yet.\n',
};

const MINIMAL_README = [
  '# Docs',
  '',
  'Verifiable as present today — see the narrow definition below.',
  '',
  'Product-owner decision made in the current working session — the highest tier.',
  '',
].join('\n');

test('a path with spaces and a Vietnamese Unicode filename resolves cleanly', () => {
  const root = makeFixture({
    ...MINIMAL_REGISTRIES,
    'docs/README.md': MINIMAL_README,
    'docs/sub folder/tài liệu.md': '# Tài liệu\n\nSee [the other doc](../other%20doc.md).\n',
    'docs/other doc.md': '# Other doc\n',
  });

  try {
    const result = run(root);
    assert.equal(result.status, 0, `expected a clean pass, got:\n${result.stdout}${result.stderr}`);
    assert.match(result.stdout, /All documentation checks passed/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('a genuinely broken link is still caught through a spaced/Unicode path', () => {
  const root = makeFixture({
    ...MINIMAL_REGISTRIES,
    'docs/README.md': MINIMAL_README,
    // The target is percent-encoded, as a well-formed Markdown link with a
    // space in its destination has to be — the same reason
    // `docs/other%20doc.md` is spelled that way in the passing fixture above.
    'docs/sub folder/tài liệu.md': '# Tài liệu\n\nSee [nowhere](../does%20not%20exist.md).\n',
  });

  try {
    const result = run(root);
    assert.equal(result.status, 1, 'a broken link must fail the check, not pass silently');
    assert.match(result.stdout, /broken link/);
    assert.match(result.stdout, /does not exist\.md/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// <b>The exact regression this file exists for.</b> The pre-port
// check-docs.py compared `Path.relative_to(ROOT)` — backslash-separated on
// Windows — against the forward-slash literal `"docs/README.md"`, so the
// exemption below never matched on this platform and docs/README.md failed
// its own definitional checks. This fixture reproduces precisely the content
// docs/README.md carries for that reason (a qualifier example, an unsourced
// CONFIRMED example) and asserts the exemption holds.
test('docs/README.md is exempt from the qualifier and Source checks, on this OS', () => {
  const root = makeFixture({
    ...MINIMAL_REGISTRIES,
    'docs/README.md': [
      '# Docs',
      '',
      '`CONFIRMED (business intent)` is not a valid status — nuance belongs in a Note column.',
      '',
      '| Status | Source |',
      '|---|---|',
      '| CONFIRMED | — |',
      '',
      'Verifiable as present today — see the narrow definition below.',
      '',
      'Product-owner decision made in the current working session — the highest tier.',
      '',
    ].join('\n'),
  });

  try {
    const result = run(root);
    assert.equal(
      result.status,
      0,
      `docs/README.md's own examples must not fail its own rules:\n${result.stdout}${result.stderr}`,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('the same qualifier example in a non-exempt file is still caught', () => {
  const root = makeFixture({
    ...MINIMAL_REGISTRIES,
    'docs/README.md':
      '# Docs\n\nVerifiable as present today.\n\nProduct-owner decision made in the current working session.\n',
    'docs/other.md': '`CONFIRMED (business intent)` is written here, outside docs/README.md.\n',
  });

  try {
    const result = run(root);
    assert.equal(result.status, 1, 'a qualifier outside docs/README.md must still fail');
    assert.match(result.stdout, /status qualifier/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ── F3.2 · the migration runbook must keep describing what is stored ───────
//
// The rule exists because the real runbook drifted: written before the
// persistence layer, it named four entities that were never built and omitted
// six collections that were. These two fixtures prove the checker notices a
// collection the runbook forgot, and stays quiet once it is described.

const MONGO_CONTEXT = 'backend/src/Vni.Ielts.Infrastructure/Persistence/MongoContext.cs';

function contextOpening(...collections) {
  const lines = collections.map(
    (c) => `    internal IMongoCollection<Doc> X => _db.GetCollection<Doc>("${c}");`,
  );
  return `internal sealed class MongoContext\n{\n${lines.join('\n')}\n}\n`;
}

test('a collection the migration runbook never mentions is caught', () => {
  const root = makeFixture({
    ...MINIMAL_REGISTRIES,
    'docs/README.md': MINIMAL_README,
    [MONGO_CONTEXT]: contextOpening('users', 'refresh_tokens'),
    'docs/database/migration-plan.md': '# Runbook\n\n| `users` | Table | — |\n',
  });

  try {
    const result = run(root);
    assert.equal(result.status, 1, 'an undescribed collection must fail the build');
    assert.match(result.stdout, /migration inventory/);
    assert.match(result.stdout, /refresh_tokens/);
    assert.doesNotMatch(
      result.stdout,
      /does not mention .*`users`/,
      'the described collection must not be reported',
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('a runbook describing every collection passes', () => {
  const root = makeFixture({
    ...MINIMAL_REGISTRIES,
    'docs/README.md': MINIMAL_README,
    [MONGO_CONTEXT]: contextOpening('users', 'refresh_tokens'),
    'docs/database/migration-plan.md':
      '# Runbook\n\n| `users` | Table | — |\n| `refresh_tokens` | Table | TTL needs a sweep |\n',
  });

  try {
    const result = run(root);
    assert.equal(
      result.status,
      0,
      `a complete runbook must pass:\n${result.stdout}${result.stderr}`,
    );
    assert.match(result.stdout, /2 collections, all described/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
