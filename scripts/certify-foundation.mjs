#!/usr/bin/env node
//
// F5.5 — the aggregation step, and the refusal that goes with it.
//
// This script collects what every phase produced and decides ONE thing:
// whether the evidence required to certify Foundation Ready is all present
// and all green. It is written so that the easy failure mode of a final
// certification — a summary assembled from prose, where a missing artifact
// reads as an absent sentence rather than as a red line — cannot happen: it
// reads files, not claims.
//
// What it deliberately does NOT do:
//
//   * It never writes docs/development/infrastructure-foundation-report.md or
//     the todolist. Those belong to the orchestrator, and a script that edits
//     the document recording whether it passed is a script that can certify
//     itself. Passing --draft into docs/ is refused outright.
//   * It never ticks a checkbox. It READS them, and an unticked box is a
//     refusal.
//   * It never treats a PARTIAL pipeline run as a pass. `verify.mjs` writes a
//     `verdict` field precisely so this file does not have to re-derive it
//     from counts that could be interpreted generously.
//
// Usage:
//   node scripts/certify-foundation.mjs
//   node scripts/certify-foundation.mjs --draft=_workspace/dev3/certification-draft.md
//
// Exit codes: 0 every requirement is present and green · 2 NOT CERTIFIED,
// with the reasons listed · 1 the tool itself could not run.

import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const posix = (p) => p.split(sep).join('/');
const abs = (p) => join(ROOT, p);

const CHECKLIST = 'docs/development/infrastructure-foundation-todolist.md';
const EVIDENCE = 'ci/foundation-evidence.json';

// The run summaries this certification requires, and the verdict each must
// carry. A missing file is a refusal, not a warning: "the drills were not run"
// and "the drills passed" must never render the same way.
const REQUIRED_RUNS = [
  {
    id: 'pipeline',
    path: '_artifacts/verify/summary.json',
    producedBy: 'node scripts/verify.mjs',
    requires: (doc) =>
      doc.verdict === 'PASS' ? null : `verdict is ${doc.verdict}, not PASS (${doc.certifies})`,
    describe: (doc) =>
      `${doc.stagesPassed}/${doc.stagesTotal} stages passed on ${doc.host?.platform} at ${doc.commit?.slice(0, 12)}`,
  },
  {
    id: 'burn-in',
    path: '_artifacts/burn-in/summary.json',
    producedBy: 'node scripts/burn-in.mjs --suite=idempotency',
    requires: (doc) => {
      if (!doc.ok) return 'a burn-in iteration failed';
      const idem = (doc.runs ?? []).find((r) => r.suite === 'idempotency');
      if (!idem) return 'the idempotency suite was not burned in';
      if (idem.completed < 10)
        return `only ${idem.completed} of the required 10 consecutive iterations completed`;
      return null;
    },
    describe: (doc) =>
      (doc.runs ?? []).map((r) => `${r.suite} ${r.completed}/${r.requested}`).join(', '),
  },
  {
    id: 'drills',
    path: '_artifacts/drills/summary.json',
    producedBy: 'node scripts/failure-drills.mjs',
    requires: (doc) =>
      doc.verdict === 'PASS'
        ? null
        : `verdict is ${doc.verdict} (${doc.counts?.failed} failed, ${doc.counts?.notRun} not run)`,
    describe: (doc) =>
      `${doc.counts?.passed}/${doc.counts?.total} drills produced their required failure`,
  },
];

const HANDOFFS = [
  { agent: 'dev1', path: '_workspace/dev1/report.md', owns: 'F3.4-F3.5 backup and restore' },
  {
    agent: 'dev2',
    path: '_workspace/dev2/report.md',
    owns: 'F4 observability, security and supply chain',
  },
  { agent: 'dev3', path: '_workspace/dev3/report.md', owns: 'F5 CI, certification and timing' },
];

// ── Checklist parsing (read-only) ──────────────────────────────────────────

function readChecklist() {
  const text = readFileSync(abs(CHECKLIST), 'utf8');
  const items = [];
  for (const line of text.split(/\r?\n/)) {
    const match = line.match(/^\s*-\s\[( |x|X)\]\s+\*\*(F\d(?:\.\d)?)[^*]*\*\*\s*(.*)$/);
    if (match) {
      items.push({
        id: match[2],
        checked: match[1].toLowerCase() === 'x',
        title: match[3].trim() || line.trim(),
      });
    }
  }
  const foundationReady =
    /Foundation Ready:\*{0,2}\s*(đã đạt|chưa đạt)/.exec(text)?.[1] ?? 'unknown';
  const inProgress = /Đang thực hiện:\*{0,2}\s*(\S+)/.exec(text)?.[1] ?? 'unknown';
  return { items, foundationReady, inProgress };
}

// ── Main ───────────────────────────────────────────────────────────────────

function parseArgs(argv) {
  const args = { draft: null, json: '_workspace/dev3/certification-input.json' };
  for (const raw of argv) {
    const [key, ...rest] = raw.split('=');
    const value = rest.join('=');
    if (key === '--draft') args.draft = value;
    else if (key === '--json') args.json = value;
    else throw new Error(`Unknown argument: ${raw}`);
  }
  for (const target of [args.draft, args.json].filter(Boolean)) {
    const normalized = posix(target).replace(/^\.\//, '');
    if (normalized.startsWith('docs/')) {
      throw new Error(
        `Refusing to write ${target}. The Foundation report and checklist under docs/ belong to the orchestrator; a certification tool that edits the document recording its own result can certify itself.`,
      );
    }
  }
  return args;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const refusals = [];
  const notes = [];

  // 1. The checklist — the only place a phase is declared closed.
  const checklist = readChecklist();
  const unchecked = checklist.items.filter((i) => !i.checked);
  for (const item of unchecked) {
    refusals.push(`${item.id} is still unchecked in ${CHECKLIST}.`);
  }

  // 2. Per-phase evidence, from the shared manifest.
  const manifest = JSON.parse(readFileSync(abs(EVIDENCE), 'utf8'));
  const phases = [];
  for (const [phase, spec] of Object.entries(manifest.phases)) {
    const entries = spec.evidence.map((entry) => {
      const candidates = entry.anyOf ?? (entry.path ? [entry.path] : []);
      const present = candidates.filter((p) => existsSync(abs(p)));
      return {
        id: entry.id,
        description: entry.description,
        candidates,
        present,
        ok: present.length > 0,
      };
    });
    const missing = entries.filter((e) => !e.ok);
    for (const entry of missing) {
      refusals.push(
        `${phase} (${spec.owner}) has not produced "${entry.id}": ${entry.candidates.join(' | ')}`,
      );
    }
    const handoffPresent = existsSync(abs(spec.handoff));
    if (!handoffPresent)
      refusals.push(`${phase} (${spec.owner}) has left no handoff at ${spec.handoff}.`);
    phases.push({
      phase,
      title: spec.title,
      owner: spec.owner,
      handoff: spec.handoff,
      handoffPresent,
      entries,
    });
  }

  // 3. The run summaries. A missing summary is a refusal, never a blank.
  const runs = [];
  for (const required of REQUIRED_RUNS) {
    if (!existsSync(abs(required.path))) {
      refusals.push(`No ${required.id} summary at ${required.path}. Run: ${required.producedBy}`);
      runs.push({
        id: required.id,
        path: required.path,
        present: false,
        ok: false,
        detail: 'not run',
      });
      continue;
    }
    let doc;
    try {
      doc = JSON.parse(readFileSync(abs(required.path), 'utf8'));
    } catch (error) {
      refusals.push(`${required.path} is not readable JSON: ${error.message}`);
      runs.push({
        id: required.id,
        path: required.path,
        present: true,
        ok: false,
        detail: 'unparseable',
      });
      continue;
    }
    const problem = required.requires(doc);
    if (problem) refusals.push(`${required.id}: ${problem}  (${required.path})`);
    runs.push({
      id: required.id,
      path: required.path,
      present: true,
      ok: !problem,
      detail: problem ?? required.describe(doc),
      ranAt: doc.ranAt ?? doc.finishedAt ?? null,
      commit: doc.commit ?? null,
    });
  }

  // 4. Handoffs, and whether they were written against this commit.
  const head =
    spawnSync('git', ['rev-parse', 'HEAD'], { cwd: ROOT, encoding: 'utf8' }).stdout?.trim() ?? null;
  const dirty = (
    spawnSync('git', ['status', '--porcelain'], { cwd: ROOT, encoding: 'utf8' }).stdout ?? ''
  ).trim();
  if (dirty) {
    notes.push(
      `The working tree has ${dirty.split('\n').length} uncommitted change(s). A certification quotes a commit SHA, so it must be run on a clean tree — otherwise the SHA describes something other than what was tested.`,
    );
    refusals.push(
      'The working tree is dirty. A Foundation certification must name a commit that actually contains what was verified.',
    );
  }

  const handoffs = HANDOFFS.map((h) => ({ ...h, present: existsSync(abs(h.path)) }));

  const certified = refusals.length === 0;
  const result = {
    certified,
    verdict: certified ? 'ALL REQUIRED EVIDENCE PRESENT AND GREEN' : 'NOT CERTIFIED',
    // Even a fully green run of this tool is not the certification itself.
    // The orchestrator writes the report; this says the inputs are there.
    meaning: certified
      ? 'Every checklist item is closed and every required artifact is present and green. The orchestrator may now write the final report. This script does not itself certify Foundation Ready.'
      : 'Foundation Ready cannot be declared. The reasons below are exhaustive at the time of this run.',
    assessedAt: new Date().toISOString(),
    commit: head,
    workingTreeClean: !dirty,
    checklist: {
      foundationReady: checklist.foundationReady,
      inProgress: checklist.inProgress,
      items: checklist.items,
    },
    phases,
    runs,
    handoffs,
    refusals,
    notes,
  };

  mkdirSync(dirname(abs(args.json)), { recursive: true });
  writeFileSync(abs(args.json), `${JSON.stringify(result, null, 2)}\n`);

  // ── Human-readable output ────────────────────────────────────────────────
  console.log(`Foundation certification input — commit ${head?.slice(0, 12) ?? '?'}\n`);

  console.log('Checklist:');
  for (const item of checklist.items) {
    console.log(`  [${item.checked ? 'x' : ' '}] ${item.id.padEnd(5)} ${item.title.slice(0, 70)}`);
  }
  console.log(`  Foundation Ready in the checklist: ${checklist.foundationReady}\n`);

  console.log('Phase evidence:');
  for (const phase of phases) {
    const ok = phase.entries.filter((e) => e.ok).length;
    console.log(
      `  ${phase.phase} (${phase.owner})  ${ok}/${phase.entries.length} artifacts · handoff ${phase.handoffPresent ? 'present' : 'MISSING'}`,
    );
    for (const entry of phase.entries.filter((e) => !e.ok)) {
      console.log(`      missing: ${entry.id} — ${entry.candidates.join(' | ')}`);
    }
  }

  console.log('\nRun summaries:');
  for (const run of runs) {
    console.log(`  ${run.ok ? 'OK  ' : 'NO  '} ${run.id.padEnd(10)} ${run.detail}`);
  }

  if (args.draft) {
    const lines = [
      '# Foundation certification — DRAFT INPUT, NOT A CERTIFICATION',
      '',
      `Generated by \`scripts/certify-foundation.mjs\` at ${result.assessedAt}.`,
      '',
      `**Verdict: ${result.verdict}.** ${result.meaning}`,
      '',
      `- Commit: \`${head ?? 'unknown'}\`${dirty ? ' (working tree DIRTY)' : ''}`,
      `- Checklist says Foundation Ready: **${checklist.foundationReady}**`,
      '',
      '## Checklist state',
      '',
      ...checklist.items.map((i) => `- [${i.checked ? 'x' : ' '}] ${i.id} ${i.title}`),
      '',
      '## Run summaries',
      '',
      '| Run | Result | Detail |',
      '|---|---|---|',
      ...runs.map((r) => `| ${r.id} | ${r.ok ? 'green' : 'NOT GREEN'} | ${r.detail} |`),
      '',
      '## Phase evidence',
      '',
      '| Phase | Owner | Artifacts present | Handoff |',
      '|---|---|---|---|',
      ...phases.map(
        (p) =>
          `| ${p.phase} | ${p.owner} | ${p.entries.filter((e) => e.ok).length}/${p.entries.length} | ${p.handoffPresent ? 'yes' : 'MISSING'} |`,
      ),
      '',
      '## Refusals',
      '',
      refusals.length === 0 ? '_None._' : refusals.map((r) => `- ${r}`).join('\n'),
      '',
      '## Notes',
      '',
      notes.length === 0 ? '_None._' : notes.map((n) => `- ${n}`).join('\n'),
      '',
    ];
    mkdirSync(dirname(abs(args.draft)), { recursive: true });
    writeFileSync(abs(args.draft), `${lines.join('\n')}\n`);
    console.log(`\nDraft written to ${args.draft} — input for the orchestrator, not a report.`);
  }

  console.log(`\n${'='.repeat(72)}`);
  if (certified) {
    console.log('ALL REQUIRED EVIDENCE PRESENT AND GREEN.');
    console.log(result.meaning);
  } else {
    console.log(`NOT CERTIFIED — ${refusals.length} refusal(s):`);
    for (const refusal of refusals) console.log(`  - ${refusal}`);
  }
  console.log(`Input data: ${args.json}`);
  console.log('='.repeat(72));

  return certified ? 0 : 2;
}

try {
  process.exit(main());
} catch (error) {
  console.error(`error: ${error.message}`);
  process.exit(1);
}
