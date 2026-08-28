#!/usr/bin/env node
//
// F5.1 — a skipped test is not a failure, so nothing else in a pipeline
// notices it.
//
// `backend.yml` already had an inline bash version of this check, which is
// where the idea comes from and why it is worth generalising: a green build
// that skipped every concurrency test reports success over rules nothing
// checked. Three problems with the inline version, all of which this file
// exists to fix:
//
//   1. it is bash, so it cannot run on the Windows half of the F5.2 matrix;
//   2. it reads only `.trx`, so a `describe.skip` in vitest or a
//      `test.fixme` in Playwright passes it silently;
//   3. it is all-or-nothing — there is no way to say "this one skip is
//      deliberate, here is who owns it and when the exemption dies", so the
//      first legitimate skip forces the gate to be deleted rather than
//      narrowed.
//
// Usage:
//   node scripts/check-test-skips.mjs --results _artifacts/verify/test-results
//   node scripts/check-test-skips.mjs --results <dir> --allowlist ci/test-skip-allowlist.json
//
// Exit codes: 0 clean · 1 an unauthorized skip, an expired exemption, or
// (with --require-results) no result files at all.

import { readFileSync, readdirSync, statSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import { join, dirname, extname, relative, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');

// `--flag=value` and `--flag value` both, and that is not politeness.
//
// The parser used to split on `=` and nothing else, so `--results <dir>` — the
// form written in this file's own usage block above, and the form
// `.github/workflows/verify.yml` calls it with — died on
// `Unknown argument: <dir>`. The skip gate therefore never ran in the pipeline
// that runs it, on either platform; the Windows matrix leg is simply where it
// surfaced first, because the Linux leg had already failed earlier.
//
// An empty value is rejected rather than accepted, because the failure it
// causes is silent: `--results=` pushes `''`, `args.results.length === 0` is
// then false so the default directory is never substituted, walking `''` finds
// no files, and the gate reports a clean run over nothing at all.
function parseArgs(argv) {
  const args = {
    results: [],
    allowlist: join(ROOT, 'ci', 'test-skip-allowlist.json'),
    json: null,
    requireResults: false,
    now: new Date(),
  };

  const takesValue = new Set(['--results', '--allowlist', '--json', '--now']);
  const rest = [...argv];

  while (rest.length > 0) {
    const raw = rest.shift();
    const eq = raw.indexOf('=');
    const key = eq === -1 ? raw : raw.slice(0, eq);

    let value = eq === -1 ? undefined : raw.slice(eq + 1);
    if (takesValue.has(key) && value === undefined) {
      // Space form. Only consume the next token if there is one and it is not
      // itself a flag — `--results --require-results` is a missing value, not
      // a directory named `--require-results`.
      if (rest.length > 0 && !rest[0].startsWith('--')) value = rest.shift();
    }
    if (takesValue.has(key) && !value) {
      throw new Error(`${key} needs a value`);
    }

    switch (key) {
      case '--results':
        args.results.push(value);
        break;
      case '--allowlist':
        args.allowlist = value;
        break;
      case '--json':
        args.json = value;
        break;
      case '--require-results':
        args.requireResults = true;
        break;
      case '--now':
        // Test seam. The expiry rule is a date comparison and a gate whose
        // red path can only be reached by waiting is a gate nobody proves.
        args.now = new Date(value);
        break;
      default:
        throw new Error(`Unknown argument: ${raw}`);
    }
  }

  if (args.results.length === 0)
    args.results.push(join(ROOT, '_artifacts', 'verify', 'test-results'));
  return args;
}

export { parseArgs };

function walk(dir, out = []) {
  let entries;
  try {
    entries = readdirSync(dir);
  } catch {
    return out;
  }
  for (const entry of entries) {
    const full = join(dir, entry);
    let st;
    try {
      st = statSync(full);
    } catch {
      continue;
    }
    if (st.isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

// ── Parsers ────────────────────────────────────────────────────────────────
//
// Each returns { skipped: [{ name, file, runner }], total, executed }.
// Deliberately regex-based rather than pulling an XML dependency in: these
// are two attributes and one element name in a format .NET has emitted
// unchanged for a decade, and this file has to run before `pnpm install` on
// a clean checkout.

function parseTrx(file, text) {
  const skipped = [];
  let total = 0;
  const counters = text.match(/<Counters\b[^>]*\/>/);
  if (counters) {
    const attr = (n) => {
      const m = counters[0].match(new RegExp(`${n}="(\\d+)"`));
      return m ? Number(m[1]) : 0;
    };
    total = attr('total');
    const notExecuted = attr('notExecuted');
    if (notExecuted > 0) {
      // Name them where the document allows it; a count with no names is
      // an alarm nobody can act on.
      const named = [
        ...text.matchAll(/<UnitTestResult\b[^>]*\btestName="([^"]*)"[^>]*\boutcome="NotExecuted"/g),
      ]
        .map((m) => m[1])
        .concat(
          [
            ...text.matchAll(
              /<UnitTestResult\b[^>]*\boutcome="NotExecuted"[^>]*\btestName="([^"]*)"/g,
            ),
          ].map((m) => m[1]),
        );
      const unique = [...new Set(named)];
      if (unique.length > 0) {
        for (const name of unique) skipped.push({ name, file, runner: 'dotnet' });
      } else {
        for (let i = 0; i < notExecuted; i += 1) {
          skipped.push({ name: `<unnamed skip #${i + 1}>`, file, runner: 'dotnet' });
        }
      }
    }
  }
  return { skipped, total };
}

function parseVitestJson(file, doc) {
  const skipped = [];
  let total = 0;
  for (const suite of doc.testResults ?? []) {
    for (const assertion of suite.assertionResults ?? []) {
      total += 1;
      if (['pending', 'skipped', 'todo'].includes(assertion.status)) {
        skipped.push({
          name: assertion.fullName || assertion.title || '<unnamed>',
          file,
          runner: 'vitest',
        });
      }
    }
  }
  return { skipped, total };
}

function parsePlaywrightJson(file, doc) {
  const skipped = [];
  let total = 0;
  const visit = (suite, trail) => {
    const here = suite.title ? [...trail, suite.title] : trail;
    for (const spec of suite.specs ?? []) {
      for (const test of spec.tests ?? []) {
        total += 1;
        const statuses = (test.results ?? []).map((r) => r.status);
        const annotated = (test.annotations ?? []).some((a) => ['skip', 'fixme'].includes(a.type));
        if (annotated || statuses.includes('skipped') || test.status === 'skipped') {
          skipped.push({ name: [...here, spec.title].join(' › '), file, runner: 'playwright' });
        }
      }
    }
    for (const child of suite.suites ?? []) visit(child, here);
  };
  for (const suite of doc.suites ?? []) visit(suite, []);
  return { skipped, total };
}

function parseFile(file) {
  const ext = extname(file).toLowerCase();
  if (ext === '.trx') return parseTrx(file, readFileSync(file, 'utf8'));
  if (ext === '.json') {
    let doc;
    try {
      doc = JSON.parse(readFileSync(file, 'utf8'));
    } catch {
      return null;
    }
    if (Array.isArray(doc?.testResults)) return parseVitestJson(file, doc);
    if (Array.isArray(doc?.suites)) return parsePlaywrightJson(file, doc);
    return null;
  }
  return null;
}

// ── Allowlist ──────────────────────────────────────────────────────────────

function loadAllowlist(path) {
  if (!existsSync(path)) return { entries: [], path, present: false };
  const doc = JSON.parse(readFileSync(path, 'utf8'));
  const entries = doc.allow ?? [];
  for (const entry of entries) {
    for (const field of ['test', 'reason', 'owner', 'expires']) {
      if (!entry[field]) {
        throw new Error(
          `${path}: an allowlist entry is missing "${field}". Every exemption needs a reason, an owner and an expiry.`,
        );
      }
    }
    if (Number.isNaN(Date.parse(entry.expires))) {
      throw new Error(`${path}: "${entry.expires}" is not an ISO-8601 date.`);
    }
  }
  return { entries, path, present: true };
}

function matches(entry, name) {
  return (
    entry.test === name || (entry.test.endsWith('*') && name.startsWith(entry.test.slice(0, -1)))
  );
}

// ── Main ───────────────────────────────────────────────────────────────────

function main() {
  const args = parseArgs(process.argv.slice(2));
  const allowlist = loadAllowlist(args.allowlist);

  const files = args.results
    .flatMap((dir) => walk(dir))
    .filter((f) => ['.trx', '.json'].includes(extname(f).toLowerCase()));

  const parsed = [];
  for (const file of files) {
    const result = parseFile(file);
    if (result) parsed.push({ file, ...result });
  }

  const problems = [];
  const allowed = [];
  const unauthorized = [];
  const usedEntries = new Set();

  for (const { skipped } of parsed) {
    for (const skip of skipped) {
      const entry = allowlist.entries.find((e) => matches(e, skip.name));
      if (!entry) {
        unauthorized.push(skip);
        continue;
      }
      usedEntries.add(entry.test);
      const expiry = new Date(entry.expires);
      if (expiry.getTime() < args.now.getTime()) {
        problems.push(
          `Exemption for "${entry.test}" expired on ${entry.expires} (owner: ${entry.owner}). Fix the test or renew the exemption deliberately.`,
        );
      } else {
        allowed.push({ ...skip, reason: entry.reason, owner: entry.owner, expires: entry.expires });
      }
    }
  }

  const totalTests = parsed.reduce((sum, p) => sum + p.total, 0);

  if (parsed.length === 0) {
    const message = `No parseable test results were found under: ${args.results.join(', ')}`;
    if (args.requireResults)
      problems.push(`${message} — a run that produced no results proves nothing.`);
    else console.warn(`warning: ${message}`);
  }

  for (const skip of unauthorized) {
    problems.push(
      `Skipped test with no exemption: ${skip.name}  [${skip.runner}] (${relative(ROOT, skip.file)})`,
    );
  }

  const summary = {
    checkedAt: new Date().toISOString(),
    resultFiles: parsed.map((p) => relative(ROOT, p.file).split('\\').join('/')),
    totalTests,
    skipped: unauthorized.length + allowed.length,
    unauthorizedSkips: unauthorized.length,
    allowedSkips: allowed,
    allowlist: {
      path: relative(ROOT, allowlist.path).split('\\').join('/'),
      present: allowlist.present,
      entries: allowlist.entries.length,
    },
    unusedExemptions: allowlist.entries.filter((e) => !usedEntries.has(e.test)).map((e) => e.test),
    problems,
    ok: problems.length === 0,
  };

  if (args.json) {
    mkdirSync(dirname(args.json), { recursive: true });
    writeFileSync(args.json, `${JSON.stringify(summary, null, 2)}\n`);
  }

  console.log(
    `Result files: ${parsed.length}   tests counted: ${totalTests}   skips: ${summary.skipped} (${unauthorized.length} unauthorized)`,
  );
  for (const entry of allowed) {
    console.log(
      `  allowed skip: ${entry.name} — ${entry.reason} (owner ${entry.owner}, expires ${entry.expires})`,
    );
  }

  if (problems.length > 0) {
    for (const problem of problems) console.error(`error: ${problem}`);
    console.error(`\n${problems.length} problem(s). A hidden test is a hole in the gate.`);
    process.exit(1);
  }

  console.log('OK — no unauthorized test skips.');
}

// Run only when invoked as a command. Without the guard, importing `parseArgs`
// from a test runs the whole gate and calls `process.exit`, which is why the
// argument bug this guard exists alongside had no test to catch it.
const invokedDirectly =
  process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href;

if (invokedDirectly) {
  try {
    main();
  } catch (error) {
    // A malformed allowlist is a gate failure, not a stack trace. The exemption
    // file is the one place a skip can be authorized, so it fails loud and
    // readable rather than crashing the runner that called it.
    console.error(`error: ${error.message}`);
    process.exit(1);
  }
}
