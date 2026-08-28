#!/usr/bin/env node
//
// Documentation integrity checks for VNI IELTS AI.
//
// F1.3 — this replaces check-docs.py. The Python version hard-depended on a
// `python3` executable that is not guaranteed present (it is not on this
// project's own Windows development machine), and it carried a
// platform-specific bug: `Path.relative_to(ROOT)` on Windows yields a
// backslash-separated string, and the script then compared that directly
// against forward-slash literals like `"docs/README.md"`. The comparison
// silently never matched on Windows, so docs/README.md — the one file that is
// SUPPOSED to contain the qualifier patterns and an unsourced CONFIRMED
// example, because it is the document defining those rules — was never
// exempted, and would have failed its own definitional checks.
//
// Node, because this project always has it (every workspace command already
// requires it) and every check below needs nothing beyond the standard
// library: no YAML parser, no third-party glob.
//
// Run locally before committing:
//
//     node scripts/check-docs.mjs
//
// Exits non-zero if any check fails, so CI can gate on it.

import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Overridable so the regression suite can point this at a throwaway fixture
// tree — spaces, Unicode filenames, a Windows checkout — instead of this
// repository's own docs, which is what actually exercises the platform bug
// this file exists to fix without requiring one to already be sitting in
// docs/ waiting to be found.
const ROOT = process.env.VNI_DOCS_CHECK_ROOT
  ? path.resolve(process.env.VNI_DOCS_CHECK_ROOT)
  : path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SKIP_DIRS = new Set(['.git', 'node_modules', 'bin', 'obj', 'dist']);
const DOC_SUFFIXES = new Set(['.md', '.mdc']);

// Files deleted on 2026-08-20. They targeted a discontinued Claude Design
// canvas project. Links to them must never come back.
const DELETED = [
  'design-prompts.md',
  'design-prompts-v2.md',
  'prompt-hoan-thien-man-con-lai.md',
  'v2-web-design-audit.md',
  'web-demo-review.md',
];

const failures = [];
const warnings = [];
const notes = [];

function walk(dir, out) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (SKIP_DIRS.has(entry.name)) continue;
      walk(path.join(dir, entry.name), out);
    } else {
      out.push(path.join(dir, entry.name));
    }
  }
  return out;
}

function docFiles() {
  return walk(ROOT, [])
    .filter((p) => DOC_SUFFIXES.has(path.extname(p)))
    .sort();
}

// <b>The fix this whole file exists for.</b> Every relative path this script
// ever compares, prints or matches against a literal goes through here first
// — forward slashes, always, regardless of the platform's own separator. A
// single choke point means there is exactly one place to have gotten this
// right, instead of one per call site.
function rel(p) {
  return path.relative(ROOT, p).split(path.sep).join('/');
}

// CRLF becomes LF before any regex sees the text, so `^`/`$` in multiline
// patterns and simple substring checks behave the same on a Windows
// checkout as on a Linux one, independent of how git or the editor wrote
// the file's line endings.
function readText(p) {
  return readFileSync(p, 'utf8').replace(/\r\n/g, '\n');
}

function fail(check, detail) {
  failures.push(`${check}: ${detail}`);
}

// ── 1 · Relative links resolve ────────────────────────────────────────────
const INLINE_LINK = /\]\(\s*(?!https?:|mailto:|tel:|#)([^)\s]+?)\s*(?:"[^"]*")?\)/g;
const REF_LINK = /^\s*\[[^\]]+\]:\s*(?!https?:|mailto:|#)(\S+)/gm;

function findAll(regex, text) {
  const out = [];
  for (const m of text.matchAll(regex)) out.push(m[1]);
  return out;
}

function safeDecode(raw) {
  try {
    return decodeURIComponent(raw);
  } catch {
    // A malformed percent-escape in a link target is not this check's
    // problem to diagnose — treat the raw string as the target, the same
    // way a browser would rather than crash the whole run over one link.
    return raw;
  }
}

function checkLinks(files) {
  let checked = 0;
  for (const file of files) {
    const text = readText(file);
    const targets = [...findAll(INLINE_LINK, text), ...findAll(REF_LINK, text)];
    for (const raw of targets) {
      const target = safeDecode(raw.split('#')[0]);
      if (!target) continue; // pure anchor
      checked += 1;
      const resolved = path.resolve(path.dirname(file), target);
      if (!existsSync(resolved)) {
        fail('broken link', `${rel(file)} -> ${target}`);
      }
    }
  }
  notes.push(`${checked} relative links checked`);
}

// ── 2 · No links to deleted files ─────────────────────────────────────────
function escapeRegExp(s) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function checkDeleted(files) {
  for (const file of files) {
    const text = readText(file);
    for (const name of DELETED) {
      // A link, not a mention. next-actions.md records the deletion by
      // name, which is correct and must not trip this check.
      if (new RegExp(`\\]\\([^)]*${escapeRegExp(name)}`).test(text)) {
        fail('link to deleted file', `${rel(file)} -> ${name}`);
      }
    }
  }
}

// ── 3 · Status taxonomy has no qualifiers ─────────────────────────────────
// docs/README.md states the rule using the forbidden forms as examples, so
// it is the one file allowed to contain them.
const QUALIFIER = /\b(CONFIRMED|EXISTING|PROPOSED|UNCONFIRMED)\s*\(/;

function checkQualifiers(files) {
  for (const file of files) {
    if (rel(file) === 'docs/README.md') continue;
    const lines = readText(file).split('\n');
    lines.forEach((line, i) => {
      if (QUALIFIER.test(line)) {
        fail('status qualifier', `${rel(file)}:${i + 1} — nuance belongs in a Note column`);
      }
    });
  }
}

// ── 4 · Canonical definitions live in exactly one place ───────────────────
const CANONICAL = {
  'status taxonomy': 'Verifiable as present today',
  'source precedence': 'Product-owner decision made in the current working session',
};

function checkCanonical(files) {
  for (const [label, marker] of Object.entries(CANONICAL)) {
    const holders = files.filter((f) => readText(f).includes(marker)).map(rel);
    if (!(holders.length === 1 && holders[0] === 'docs/README.md')) {
      fail(
        'duplicated canonical definition',
        `${label} should be defined only in docs/README.md, found in: ${holders.length ? holders.join(', ') : 'nowhere'}`,
      );
    }
  }
}

// ── 5 · Every CONFIRMED row cites a Source ────────────────────────────────
// Any table row whose own status cell is CONFIRMED (plain or bold) must be
// traceable to the product owner, wherever in the repository it appears —
// the 2026-08-20 audit found unsourced CONFIRMED rows outside confirmed.md.
// Cells that merely mention CONFIRMED alongside other text (cross-references
// like "**CONFIRMED** (`T-2`)") deliberately do not match.
// docs/README.md is exempt: its taxonomy table defines the status itself.
const CONFIRMED_CELL = /\|\s*\*{0,2}CONFIRMED\*{0,2}\s*\|/;
const OWNER_MARKERS = ['Owner brief', 'Owner decision', 'Chủ sản phẩm', 'chủ sản phẩm'];

function checkSources(files) {
  if (!existsSync(path.join(ROOT, 'docs/requirements/confirmed.md'))) {
    fail('missing file', 'docs/requirements/confirmed.md');
    return;
  }
  let total = 0;
  let missing = 0;
  for (const file of files) {
    if (rel(file) === 'docs/README.md') continue;
    const lines = readText(file).split('\n');
    lines.forEach((line, i) => {
      if (!CONFIRMED_CELL.test(line)) return;
      total += 1;
      if (!OWNER_MARKERS.some((marker) => line.includes(marker))) {
        missing += 1;
        fail('CONFIRMED without Source', `${rel(file)}:${i + 1}`);
      }
    });
  }
  notes.push(`${total} CONFIRMED rows, ${total - missing} with a traceable Source`);
}

// ── 6 · No credential-shaped strings ──────────────────────────────────────
const SECRET_PATTERNS = [
  [/AQ\.[A-Za-z0-9_-]{20,}/, 'Google API key'],
  [/AIza[A-Za-z0-9_-]{30,}/, 'Google API key'],
  [/sk-[A-Za-z0-9]{32,}/, 'OpenAI-style key'],
  [/gh[pousr]_[A-Za-z0-9]{30,}/, 'GitHub token'],
];

const BINARY_SUFFIXES = new Set([
  '.png',
  '.jpg',
  '.jpeg',
  '.gif',
  '.docx',
  '.pdf',
  '.rar',
  '.zip',
  '.ico',
]);
const SELF = 'scripts/check-docs.mjs';

function trackedFiles() {
  try {
    const out = execFileSync('git', ['-C', ROOT, 'ls-files', '-z'], { encoding: 'utf8' });
    return out
      .split('\0')
      .filter(Boolean)
      .map((name) => path.join(ROOT, name));
  } catch {
    return null; // not a git checkout, or git is not on PATH
  }
}

function scanForSecrets(file) {
  if (BINARY_SUFFIXES.has(path.extname(file)) || rel(file) === SELF) {
    return null; // the patterns themselves live in this file
  }
  let text;
  try {
    text = readFileSync(file, 'utf8');
  } catch {
    return null;
  }
  for (const [pattern, label] of SECRET_PATTERNS) {
    if (pattern.test(text)) return label;
  }
  return null;
}

function checkSecrets() {
  const tracked = trackedFiles();
  const scope = tracked ?? walk(ROOT, []);

  for (const file of scope) {
    if (!existsSync(file)) continue;
    const label = scanForSecrets(file);
    if (label) fail('secret in a tracked file', `${rel(file)} — looks like a ${label}`);
  }

  if (tracked === null) return;

  // Untracked and ignored files: warn, do not fail.
  const trackedSet = new Set(tracked.map((p) => path.resolve(p)));
  for (const file of walk(ROOT, [])) {
    if (trackedSet.has(path.resolve(file))) continue;
    const label = scanForSecrets(file);
    if (label) {
      warnings.push(
        `${rel(file)} holds what looks like a ${label}. It is not in git, but excluding a key is not revoking it.`,
      );
    }
  }
}

// ── 7 · Phase status is consistent ────────────────────────────────────────
const STALE_PHASE = [
  ['Status: Phase 0', 'README once claimed Phase 0 while CLAUDE.md said Phase 1'],
  ['Phase 0/1', 'ambiguous phase marker'],
  ['## Phase 1 — Google Stitch', 'Stitch was evaluated and dropped'],
];

function checkPhase(files) {
  for (const file of files) {
    const text = readText(file);
    for (const [marker, why] of STALE_PHASE) {
      if (text.includes(marker)) {
        fail('stale phase claim', `${rel(file)} contains ${JSON.stringify(marker)} — ${why}`);
      }
    }
  }
}

// ── 8 · No id claimed by two headings ─────────────────────────────────────
// The two registries carry their ids differently: open questions as
// headings, confirmed requirements as the first cell of a table row. Both
// are names other documents point at, so both are checked.
const ID_HEADING = /^#{2,4}\s+`?([A-Z]-\d+[a-z]?)`?\s*(?:·|-|—|:)/gm;
const ID_TABLE_ROW = /^\|\s*`?([A-Z]-\d+[a-z]?)`?\s*\|/gm;

const REGISTRIES = [
  'docs/requirements/assumptions-and-open-questions.md',
  'docs/requirements/confirmed.md',
];

function checkRequirementIds() {
  for (const name of REGISTRIES) {
    const file = path.join(ROOT, name);
    if (!existsSync(file)) continue;

    const text = readText(file);
    const seen = new Map();

    for (const pattern of [ID_HEADING, ID_TABLE_ROW]) {
      for (const m of text.matchAll(pattern)) {
        seen.set(m[1], (seen.get(m[1]) ?? 0) + 1);
      }
    }

    for (const [ident, count] of [...seen.entries()].sort(([a], [b]) =>
      a < b ? -1 : a > b ? 1 : 0,
    )) {
      if (count > 1) {
        fail(
          'requirement ids',
          `${name}: \`${ident}\` is claimed by ${count} headings. An id is what other documents ` +
            'point at, so two of them means somebody will answer the wrong question.',
        );
      }
    }

    notes.push(`${seen.size} requirement ids in ${path.basename(name)}, all distinct`);
  }
}

// ── 9 · The migration runbook still describes what is actually stored ─────
//
// F3.2 — this is the drift that made the previous migration plan wrong. It
// was written before the persistence layer existed, named four entities that
// were never built, and omitted six collections that were — including the two
// that need a replacement sweep job in PostgreSQL because it has no TTL index.
// Nothing forced the document to move when the code did.
//
// So the inventory is checked rather than trusted: every collection the
// application actually opens has to appear in the runbook's table. A new
// collection added without a row is a migration that will be planned against
// a store that is missing part of itself.
const COLLECTION_SOURCES = [
  'backend/src/Vni.Ielts.Infrastructure/Persistence/MongoContext.cs',
  'backend/src/Vni.Ielts.Infrastructure/Persistence/Identity/AuditLog.cs',
];

const RUNBOOK = 'docs/database/migration-plan.md';

// GetCollection<Whatever>("name") — the name is what the runbook must list.
const GET_COLLECTION = /GetCollection<[^>]+>\(\s*"([a-z_]+)"\s*\)/g;

function checkMigrationInventory() {
  const runbookPath = path.join(ROOT, RUNBOOK);
  if (!existsSync(runbookPath)) return;

  const collections = new Set();
  for (const name of COLLECTION_SOURCES) {
    const file = path.join(ROOT, name);
    if (!existsSync(file)) continue;
    for (const m of readText(file).matchAll(GET_COLLECTION)) collections.add(m[1]);
  }

  if (collections.size === 0) return;

  const runbook = readText(runbookPath);
  const missing = [...collections].filter((c) => !runbook.includes(`\`${c}\``)).sort();

  if (missing.length > 0) {
    fail(
      'migration inventory',
      `${RUNBOOK} does not mention ${missing.map((c) => `\`${c}\``).join(', ')}, ` +
        'which the application opens. Add a row to "What is actually stored" saying what the ' +
        'collection maps to and anything that makes it non-trivial — a TTL, a unique index, a ' +
        'compare-and-swap, a lease. A migration plan that is wrong about what is stored is ' +
        'worse than none.',
    );
  }

  notes.push(`${collections.size} collections, all described in ${path.basename(RUNBOOK)}`);
}

function main() {
  const files = docFiles();
  notes.push(`${files.length} documentation files`);

  checkLinks(files);
  checkDeleted(files);
  checkQualifiers(files);
  checkCanonical(files);
  checkSources(files);
  checkRequirementIds();
  checkSecrets();
  checkPhase(files);
  checkMigrationInventory();

  for (const note of notes) console.log(`  ${note}`);
  console.log();

  if (warnings.length > 0) {
    console.log(`WARNINGS — ${warnings.length}, not blocking:\n`);
    for (const w of warnings) console.log(`  ${w}`);
    console.log();
  }

  if (failures.length > 0) {
    console.log(`FAILED — ${failures.length} problem(s):\n`);
    for (const f of failures) console.log(`  ${f}`);
    return 1;
  }

  console.log('All documentation checks passed.');
  return 0;
}

process.exit(main());
