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
// FS0.6 added the two failures below, because a gate that cannot read a report
// used to say nothing at all about it:
//
//   4. a result file that cannot be parsed was dropped in silence. On
//      2026-08-29 `verify.mjs` captured Playwright's JSON by scraping stdout,
//      pnpm printed `WARN Unsupported engine` onto line 1 of it, `JSON.parse`
//      threw, and `parseFile()` returned `null` — so the gate reported "OK, no
//      unauthorized skips" over 7 Playwright tests it never looked at. It
//      failed OPEN, which is the one direction a gate must never fail;
//   5. a report that a stage was supposed to produce and did not is now
//      declarable with --expect-report. Counting what happens to be on disk
//      cannot distinguish "the browser suite passed" from "the browser suite
//      wrote nothing".
//
// Usage:
//   node scripts/check-test-skips.mjs --results _artifacts/verify/test-results
//   node scripts/check-test-skips.mjs --results <dir> --allowlist ci/test-skip-allowlist.json
//   node scripts/check-test-skips.mjs --results <dir> --expect-report <file>
//   node scripts/check-test-skips.mjs --static-only     configuration only
//
// Exit codes: 0 clean · 1 an unauthorized skip, an expired exemption, a result
// file that could not be parsed, an expected report that is absent, a
// `dotnet test` script that does not require its dependencies, or (with
// --require-results) no result files at all.

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
    expectReports: [],
    staticOnly: false,
    allowlist: join(ROOT, 'ci', 'test-skip-allowlist.json'),
    json: null,
    requireResults: false,
    now: new Date(),
  };

  const takesValue = new Set(['--results', '--expect-report', '--allowlist', '--json', '--now']);
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
      case '--expect-report':
        // A report the caller knows a stage produced. Absent, it is a failure
        // rather than one fewer file to count.
        args.expectReports.push(value);
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
      case '--static-only':
        // `pnpm check` produces no machine-readable test results, so the only
        // honest thing it can check here is the configuration: that no
        // configured test command can skip its dependencies and still exit 0.
        // Reading a results directory it did not write would report on
        // whatever the last `pnpm verify` happened to leave behind.
        args.staticOnly = true;
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

export { parseArgs, parseTrx, dotnetTestScriptProblems };

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

// <b>`<Counters notExecuted>` is not trustworthy, and this gate was built on
// it.</b>
//
// Measured on the real CI artifact from run 33193503434: the `.trx` for the
// Infrastructure suite carries `<Counters … notExecuted="0" …/>` while its own
// body holds three `<UnitTestResult … outcome="NotExecuted">` entries, and
// `dotnet test` printed `Skipped: 3` for that same run. The summary element and
// the results in the same document disagree, and the summary is the one that is
// wrong.
//
// So the per-result scan runs ALWAYS rather than only when the counter says
// there is something to find — that `if (notExecuted > 0)` guard is exactly why
// six skipped object-storage tests went unreported on every build. The count is
// the larger of the two readings: whichever source noticed a skip is believed,
// and neither gets to veto the other.
function parseTrx(file, text) {
  const skipped = [];
  let total = 0;

  const counters = text.match(/<Counters\b[^>]*\/>/);
  let notExecuted = 0;
  if (counters) {
    const attr = (n) => {
      const m = counters[0].match(new RegExp(`${n}="(\\d+)"`));
      return m ? Number(m[1]) : 0;
    };
    total = attr('total');
    notExecuted = attr('notExecuted');
  }

  // Both attribute orders: the TRX schema does not fix them, and a result whose
  // `outcome` precedes its `testName` is just as skipped.
  const named = [
    ...[
      ...text.matchAll(/<UnitTestResult\b[^>]*\btestName="([^"]*)"[^>]*\boutcome="NotExecuted"/g),
    ].map((m) => m[1]),
    ...[
      ...text.matchAll(/<UnitTestResult\b[^>]*\boutcome="NotExecuted"[^>]*\btestName="([^"]*)"/g),
    ].map((m) => m[1]),
  ];

  const unique = [...new Set(named)];
  for (const name of unique) skipped.push({ name, file, runner: 'dotnet' });

  // A count with no names is an alarm nobody can act on, but it is still an
  // alarm — so it is raised rather than dropped.
  for (let i = unique.length; i < notExecuted; i += 1) {
    skipped.push({ name: `<unnamed skip #${i + 1}>`, file, runner: 'dotnet' });
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

// <b>An unreadable report is a failure, never a shrug.</b>
//
// This function used to answer `null` for every JSON it could not make sense
// of, and `main()` then simply did not add it to the parsed set. So the single
// worst outcome — the gate cannot see the results — was indistinguishable from
// the best one. It is what happened on 2026-08-29: `verify.mjs` scraped
// Playwright's report off stdout, pnpm's `WARN Unsupported engine` landed on
// line 1, and 7 browser tests vanished from a gate whose whole purpose is that
// a `test.fixme` there cannot pass unnoticed.
//
// The capture mechanism is fixed at the other end (Playwright now writes the
// file itself), and no attempt is made here to salvage a file with a preamble:
// tolerating junk in a report would only hide the next tool that starts
// writing it. `{ unreadable }` is a problem the caller must raise.
function parseFile(file) {
  const ext = extname(file).toLowerCase();
  if (ext === '.trx') return parseTrx(file, readFileSync(file, 'utf8'));
  if (ext === '.json') {
    const text = readFileSync(file, 'utf8');
    let doc;
    try {
      doc = JSON.parse(text);
    } catch (error) {
      const firstLine = (text.split(/\r?\n/, 1)[0] ?? '').trim().slice(0, 160);
      return {
        unreadable:
          `${relative(ROOT, file)} could not be parsed as JSON (${error.message}). ` +
          `First line: ${firstLine || '<empty>'} — a test report nothing can read is ` +
          'not a test report that passed.',
      };
    }
    if (Array.isArray(doc?.testResults)) return parseVitestJson(file, doc);
    if (Array.isArray(doc?.suites)) return parsePlaywrightJson(file, doc);
    return {
      unreadable:
        `${relative(ROOT, file)} parses as JSON but is neither a vitest nor a Playwright ` +
        'report (no testResults[], no suites[]). Either the reporter changed shape or ' +
        'something that is not a test report is being counted as one.',
    };
  }
  return null;
}

// ── Static check: a test command that cannot fail for want of a database ───
//
// FS0.6, defect 2. `VNI_REQUIRE_MONGO` and `VNI_REQUIRE_MINIO` are what turn
// "no dependency" from a `Skip.IfNot` into a failed run. `scripts/verify.mjs`
// sets them per stage; `pnpm test:api` did not, and `pnpm check` calls
// `test:api` — so on a host with no Mongo up, all 164 conditional sites in the
// backend tree skipped and `pnpm check` reported success over a suite that
// tested nothing.
//
// This lives in the skip gate rather than in a new script because it is the
// same failure the gate exists for, one step earlier: not a skip that slipped
// past the report, but a command configured so that skipping everything is a
// pass. A runtime gate cannot catch it — by the time the results are written
// there is nothing in them to see.
const REQUIRED_DEPENDENCY_FLAGS = ['VNI_REQUIRE_MONGO', 'VNI_REQUIRE_MINIO'];

function dotnetTestScriptProblems(pkg) {
  const problems = [];
  for (const [name, command] of Object.entries(pkg?.scripts ?? {})) {
    if (!/\bdotnet\s+test\b/.test(command)) continue;
    for (const flag of REQUIRED_DEPENDENCY_FLAGS) {
      if (command.includes(flag)) continue;
      problems.push(
        `package.json scripts.${name} runs \`dotnet test\` without ${flag}=1 ` +
          `(\`${command}\`). Without it, a host with no Mongo/MinIO skips every ` +
          'dependency-backed test and the suite still exits 0. Pass it with ' +
          `\`-e ${flag}=1\`, which dotnet test forwards to the test host on every platform.`,
      );
    }
  }
  return problems;
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

  const staticProblems = dotnetTestScriptProblems(
    JSON.parse(readFileSync(join(ROOT, 'package.json'), 'utf8')),
  );

  if (args.staticOnly) {
    for (const problem of staticProblems) console.error(`error: ${problem}`);
    if (staticProblems.length > 0) process.exit(1);
    console.log(
      'OK — every configured `dotnet test` command requires its dependencies. ' +
        '(--static-only: no test results were read.)',
    );
    return;
  }

  const expected = args.expectReports.map((p) => resolve(ROOT, p));

  // An expected report may sit outside every --results directory, so it is
  // added to the file list rather than merely looked for in it.
  const files = [
    ...new Set([
      ...args.results
        .flatMap((dir) => walk(dir))
        .filter((f) => ['.trx', '.json'].includes(extname(f).toLowerCase()))
        .map((f) => resolve(f)),
      ...expected.filter((f) => existsSync(f)),
    ]),
  ];

  const problems = [];

  const parsed = [];
  for (const file of files) {
    const result = parseFile(file);
    if (!result) continue;
    if (result.unreadable) {
      problems.push(result.unreadable);
      continue;
    }
    parsed.push({ file, ...result });
  }

  for (const file of expected) {
    if (!existsSync(file)) {
      problems.push(
        `Expected test report is missing: ${relative(ROOT, file)}. The stage that ` +
          'should have written it either did not run or produced nothing, and a gate ' +
          'cannot pass over a report that is not there.',
      );
      continue;
    }
    if (!parsed.some((p) => p.file === file)) {
      // It exists and it was not parsed — the parse problem is already in the
      // list above, but say plainly that this was a report somebody expected.
      problems.push(`Expected test report ${relative(ROOT, file)} yielded no readable results.`);
    }
  }

  // The static half of the gate: a configured test command that cannot fail
  // for want of its dependencies is a skip nobody will ever see in a report.
  problems.push(...staticProblems);

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
