// FS0.2 phase gate — "Inventory ghép đúng ít nhất VOL 9 Test 1 Reading/Listening
// với key và audio; file bị thay đổi hash được phát hiện."
//
// Every fixture here is built fresh in a throwaway temp directory that MIRRORS
// the real content layout — the same Vietnamese directory names, the same
// Google-Drive export suffix, the same misspellings — and is torn down after.
//
// This matters more than it looks. `/exam/` and `/Đề IELTS/` are gitignored, so
// a test that pointed at the real content would pass on exactly one machine and
// silently skip everywhere else. The fixture recreates the *shapes* the real
// source-descriptors match against, so the descriptors themselves are under
// test, not this developer's disk.
//
// Run: node --test scripts/content-inventory.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const SCRIPT = path.resolve(import.meta.dirname, 'content-inventory.mjs');

// The real on-disk parent. The `-20260819T082203Z-1-001` tail is a Google Drive
// export artefact: it is matched, and it must never reach a sourceId.
const VOL9_EXPORT_DIR = 'Đề thi thật (Chỉ L và R) VOL 9 - REAL IELTS-20260819T082203Z-1-001';
const VOL9 = `Đề IELTS/Đề CAM/${VOL9_EXPORT_DIR}/VOL 9 - REAL IELTS`;

// A distinctive string planted inside a *key* file. The script hashes answer
// keys; it must never echo what is in them.
const PLANTED_ANSWER = 'PLANTED-ANSWER-TOKEN-Q7-ELEPHANT';

function newRoot() {
  return mkdtempSync(path.join(tmpdir(), 'vni-content-inv-'));
}

function put(root, rel, content) {
  const full = path.join(root, ...rel.split('/'));
  mkdirSync(path.dirname(full), { recursive: true });
  writeFileSync(full, content, 'utf8');
  return full;
}

function drop(root, rel) {
  rmSync(path.join(root, ...rel.split('/')), { force: true });
}

/**
 * A two-test VOL 9, carrying every naming anomaly the survey found in the real
 * eight-test directory: the misspelled `KET`, the stray space in `TEST 1 -R`,
 * and the misspelled `KEY - EXPLAINATION` directory.
 */
function makeVol9(root) {
  put(root, `${VOL9}/READING/TEST 1 -R.docx`, 'reading paper one'); // stray space
  put(root, `${VOL9}/READING/TEST 2-R.docx`, 'reading paper two');
  put(root, `${VOL9}/READING/KEY - EXPLAINATION/KEY TEST 1-R.docx`, `key one ${PLANTED_ANSWER}`);
  put(root, `${VOL9}/READING/KEY - EXPLAINATION/KET TEST 2-R.docx`, 'key two'); // misspelled KEY

  put(root, `${VOL9}/LISTENING/TEST 1-L.docx`, 'listening paper one');
  put(root, `${VOL9}/LISTENING/TEST 2-L.docx`, 'listening paper two');
  put(root, `${VOL9}/LISTENING/KEY - TRANSCRIPT/KEY TEST 1-L.docx`, 'listening key one');
  put(root, `${VOL9}/LISTENING/KEY - TRANSCRIPT/KEY TEST 2-L.docx`, 'listening key two');
  put(root, `${VOL9}/LISTENING/AUDIO/TEST 1.mp4`, 'audio-bytes-one'); // .mp4: a video container
  put(root, `${VOL9}/LISTENING/AUDIO/TEST 2.mp4`, 'audio-bytes-two');
  return root;
}

function run(root, extra = []) {
  const out = path.join(root, 'inventory.json');
  const res = spawnSync(
    process.execPath,
    [SCRIPT, '--root', root, '--out', out, '--no-probe', ...extra],
    { encoding: 'utf8' },
  );
  const report = existsSync(out) ? JSON.parse(readFileSync(out, 'utf8')) : null;
  return { ...res, out, report };
}

function source(report, id) {
  return report.sources.find((s) => s.sourceId === id);
}

function slot(report, id, module, testId) {
  const src = source(report, id);
  const mod = src.modules.find((m) => m.module === module);
  return mod.tests.find((t) => t.test === testId);
}

function codes(report) {
  return report.problems.map((p) => p.code);
}

// ---------------------------------------------------------------------------
// Absence — the CI case, and the one that must never look clean
// ---------------------------------------------------------------------------

test('an absent source tree exits 2 and never reports a clean bill', () => {
  const root = newRoot();
  try {
    const { status, stdout, report } = run(root);

    assert.equal(status, 2, 'nothing to inventory must not share an exit code with success');
    assert.equal(report.summary.files, 0);
    assert.ok(report.summary.sourcesPresent === 0);
    assert.ok(report.summary.sourcesAbsent > 0);
    assert.ok(
      report.sources.every((s) => s.present === false && s.absenceReason),
      'every source must say why it contributed nothing',
    );
    assert.match(stdout, /Nothing to inventory/);
    assert.doesNotMatch(stdout, /no problems/, 'zero files is not zero problems');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('a partially absent tree still inventories what is there and exits 1', () => {
  const root = makeVol9(newRoot());
  try {
    const { status, report } = run(root);
    assert.equal(status, 1);
    assert.ok(report.summary.files > 0);
    assert.ok(report.summary.sourcesAbsent > 0);
    assert.ok(codes(report).includes('source-absent'));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------
// The phase gate: VOL 9 Test 1
// ---------------------------------------------------------------------------

test('VOL 9 Test 1 Reading pairs paper with key, and claims no audio', () => {
  const root = makeVol9(newRoot());
  try {
    const { report } = run(root);
    const t1 = slot(report, 'vol9-real-ielts', 'reading', '1');

    assert.equal(t1.paper.status, 'paired');
    assert.equal(t1.paper.files.length, 1);
    assert.match(t1.paper.files[0], /READING\/TEST 1 -R\.docx$/);

    assert.equal(t1.key.status, 'paired');
    assert.match(t1.key.files[0], /KEY - EXPLAINATION\/KEY TEST 1-R\.docx$/);

    // IELTS Reading has no audio. Reporting eight missing files would be a
    // defect, not a finding.
    assert.equal(t1.audio.status, 'not-applicable');
    assert.ok(t1.audio.reason);
    assert.equal(
      report.problems.filter((p) => p.code === 'missing-audio' && p.module === 'reading').length,
      0,
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('VOL 9 Test 1 Listening pairs paper, key and its single .mp4', () => {
  const root = makeVol9(newRoot());
  try {
    const { report } = run(root);
    const t1 = slot(report, 'vol9-real-ielts', 'listening', '1');

    assert.equal(t1.paper.status, 'paired');
    assert.equal(t1.key.status, 'paired');
    assert.equal(t1.audio.status, 'paired');
    assert.equal(t1.audio.files.length, 1);
    assert.match(t1.audio.files[0], /LISTENING\/AUDIO\/TEST 1\.mp4$/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('the misspelled KET key still pairs, and the anomaly is recorded', () => {
  const root = makeVol9(newRoot());
  try {
    const { report } = run(root);
    const t2 = slot(report, 'vol9-real-ielts', 'reading', '2');

    assert.equal(t2.key.status, 'paired', 'a strict matcher would drop Reading Test 2 silently');
    assert.match(t2.key.files[0], /KET TEST 2-R\.docx$/);

    const entry = report.files.find((f) => f.path.endsWith('KET TEST 2-R.docx'));
    assert.ok(entry.anomalies.includes('misspelled-key-prefix'));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('the stray-space filename still pairs, and the anomaly is recorded', () => {
  const root = makeVol9(newRoot());
  try {
    const { report } = run(root);
    const entry = report.files.find((f) => f.path.endsWith('TEST 1 -R.docx'));
    assert.ok(entry.anomalies.includes('irregular-whitespace'));
    assert.equal(slot(report, 'vol9-real-ielts', 'reading', '1').paper.status, 'paired');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------
// Missing and ambiguous
// ---------------------------------------------------------------------------

test('a genuinely missing key is reported as missing, not paired', () => {
  const root = makeVol9(newRoot());
  try {
    drop(root, `${VOL9}/LISTENING/KEY - TRANSCRIPT/KEY TEST 1-L.docx`);
    const { status, report } = run(root);

    const t1 = slot(report, 'vol9-real-ielts', 'listening', '1');
    assert.equal(t1.key.status, 'missing');
    assert.equal(t1.key.files.length, 0);
    assert.equal(status, 1);

    const p = report.problems.find((x) => x.code === 'missing-key' && x.test === '1');
    assert.ok(p, 'the missing key must surface as a problem, not only as a slot status');
    assert.equal(p.severity, 'error');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('two candidates for one slot are flagged ambiguous, and neither is chosen', () => {
  const root = makeVol9(newRoot());
  try {
    // A second file that the tolerant key regex also matches for Reading 1.
    put(root, `${VOL9}/READING/KEY - EXPLAINATION/KEY  TEST 1 -R.docx`, 'a rival key');
    const { status, report } = run(root);

    const t1 = slot(report, 'vol9-real-ielts', 'reading', '1');
    assert.equal(t1.key.status, 'ambiguous');
    assert.equal(t1.key.files.length, 0, 'an ambiguity must not be resolved by picking one');
    assert.equal(t1.key.candidates.length, 2);
    assert.equal(status, 1);

    const p = report.problems.find((x) => x.code === 'ambiguous-key');
    assert.ok(p);
    assert.equal(p.severity, 'ambiguity');
    assert.equal(p.paths.length, 2);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------
// Cambridge: 1:1 vs 1:4 is never assumed, keys are not "missing"
// ---------------------------------------------------------------------------

test('Cambridge keys are not-applicable rather than missing', () => {
  const root = newRoot();
  try {
    put(root, 'Đề IELTS/Đề CAM/Cam 16/Cam 16.pdf', 'book');
    for (const t of [1, 2]) {
      for (const p of [1, 2, 3, 4]) {
        put(root, `Đề IELTS/Đề CAM/Cam 16/Test ${t} Part ${p}.mp3`, `t${t}p${p}`);
      }
    }
    const { report } = run(root);
    const t1 = slot(report, 'cambridge-16', 'listening', '1');

    assert.equal(t1.key.status, 'not-applicable');
    assert.match(t1.key.reason, /PDF/i);
    assert.equal(
      report.problems.filter((p) => p.code === 'missing-key' && p.sourceId === 'cambridge-16')
        .length,
      0,
    );
    assert.equal(t1.audio.files.length, 4);
    assert.equal(t1.paper.status, 'book-level');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('audio cardinality is observed per source, and an inconsistency is flagged', () => {
  const root = newRoot();
  try {
    put(root, 'Đề IELTS/Đề CAM/Cam 16/Cam 16.pdf', 'book');
    for (const p of [1, 2, 3, 4]) put(root, `Đề IELTS/Đề CAM/Cam 16/Test 1 Part ${p}.mp3`, `a${p}`);
    for (const p of [1, 2, 3]) put(root, `Đề IELTS/Đề CAM/Cam 16/Test 2 Part ${p}.mp3`, `b${p}`);

    const { report } = run(root);
    const mod = source(report, 'cambridge-16').modules[0];
    assert.deepEqual(mod.audioCardinality.observed.sort(), [3, 4]);
    assert.equal(mod.audioCardinality.consistent, false);
    assert.ok(codes(report).includes('inconsistent-audio-cardinality'));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('a one-audio-per-test source is not reported as three missing files', () => {
  const root = newRoot();
  try {
    put(root, 'Đề IELTS/Đề CAM/Cam 21/Cambridge IELTS 21.pdf', 'book');
    for (const t of [1, 2]) {
      put(
        root,
        `Đề IELTS/Đề CAM/Cam 21/Cambridge IELTS 21 Audio/Cambridge 21 - Test ${t}.mp3`,
        'x',
      );
    }
    const { report } = run(root);
    const mod = source(report, 'cambridge-21').modules[0];

    assert.deepEqual(mod.audioCardinality.observed, [1]);
    assert.equal(mod.audioCardinality.consistent, true);
    assert.equal(
      report.problems.filter((p) => p.code === 'missing-audio').length,
      0,
      '1:1 must not be measured against an assumed 1:4',
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('the Cam 17 file missing its ELT_ prefix still pairs', () => {
  const root = newRoot();
  try {
    put(root, 'Đề IELTS/Đề CAM/Cam 17/Cambridge Ielts 17.pdf', 'book');
    for (const a of [2, 3, 4]) {
      put(root, `Đề IELTS/Đề CAM/Cam 17/ELT_IELTS17_t4_audio${a}.mp3`, `a${a}`);
    }
    put(root, 'Đề IELTS/Đề CAM/Cam 17/IELTS17_t4_audio1.mp3', 'a1'); // no ELT_
    const { report } = run(root);
    const t4 = slot(report, 'cambridge-17', 'listening', '4');

    assert.equal(t4.audio.files.length, 4);
    assert.ok(t4.audio.files.some((f) => f.endsWith('/IELTS17_t4_audio1.mp3')));
    const entry = report.files.find((f) => f.path.endsWith('/IELTS17_t4_audio1.mp3'));
    assert.ok(entry.anomalies.includes('irregular-prefix'));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('Cam 18 two-number naming is flagged as an unresolved axis, not guessed', () => {
  const root = newRoot();
  try {
    put(root, 'Đề IELTS/Đề CAM/Cam 18/0. Cambridge 18 (1).pdf', 'book');
    put(root, 'Đề IELTS/Đề CAM/Cam 18/18 section1-part1.mp3', 'a');
    put(root, 'Đề IELTS/Đề CAM/Cam 18/18 section1- part2.mp3', 'b'); // separator variant
    put(root, 'Đề IELTS/Đề CAM/Cam 18/18 section2 part1.mp3', 'c'); // another variant
    put(root, 'Đề IELTS/Đề CAM/Cam 18/18 section2-part2.mp3', 'd');

    const { report } = run(root);
    const mod = source(report, 'cambridge-18').modules[0];

    assert.equal(mod.testAxis.resolved, false);
    assert.ok(mod.testAxis.note);
    const p = report.problems.find((x) => x.code === 'ambiguous-test-axis');
    assert.ok(p, 'section/part vocabulary is inverted vs IELTS usage — say so, do not pick');
    assert.equal(p.severity, 'ambiguity');
    // All four files must still be inventoried and hashed.
    assert.equal(report.files.filter((f) => f.sourceId === 'cambridge-18').length, 5);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------
// Hash drift — the second half of the phase gate
// ---------------------------------------------------------------------------

test('a changed file is detected against a baseline', () => {
  const root = makeVol9(newRoot());
  try {
    const first = run(root);
    assert.ok(first.report.summary.files > 0);

    const baseline = path.join(root, 'baseline.json');
    writeFileSync(baseline, readFileSync(first.out, 'utf8'), 'utf8');

    // One byte of one paper changes. Nothing else moves.
    put(root, `${VOL9}/READING/TEST 2-R.docx`, 'reading paper two (edited)');

    const second = run(root, ['--baseline', baseline]);
    assert.equal(second.status, 1);

    const changed = second.report.problems.filter((p) => p.code === 'hash-changed');
    assert.equal(changed.length, 1, 'exactly the edited file, and only it');
    assert.match(changed[0].paths[0], /READING\/TEST 2-R\.docx$/);
    assert.equal(changed[0].severity, 'error');
    assert.notEqual(changed[0].baselineSha256, changed[0].currentSha256);
    assert.equal(second.report.summary.hashChanges, 1);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('an unchanged tree reports no hash drift against its own baseline', () => {
  const root = makeVol9(newRoot());
  try {
    const first = run(root);
    const baseline = path.join(root, 'baseline.json');
    writeFileSync(baseline, readFileSync(first.out, 'utf8'), 'utf8');

    const second = run(root, ['--baseline', baseline]);
    assert.equal(second.report.summary.hashChanges, 0);
    assert.equal(
      second.report.problems.filter((p) => p.code.startsWith('hash-')).length,
      0,
      'a stable tree must not produce drift noise, or the signal is worthless',
    );
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('added and removed files are distinguished from a changed one', () => {
  const root = makeVol9(newRoot());
  try {
    const first = run(root);
    const baseline = path.join(root, 'baseline.json');
    writeFileSync(baseline, readFileSync(first.out, 'utf8'), 'utf8');

    drop(root, `${VOL9}/LISTENING/AUDIO/TEST 2.mp4`);
    put(root, `${VOL9}/LISTENING/AUDIO/TEST 3.mp4`, 'audio-bytes-three');
    put(root, `${VOL9}/LISTENING/TEST 3-L.docx`, 'listening paper three');
    put(root, `${VOL9}/LISTENING/KEY - TRANSCRIPT/KEY TEST 3-L.docx`, 'listening key three');

    const { report } = run(root, ['--baseline', baseline]);
    const c = codes(report);
    assert.ok(c.includes('file-removed'));
    assert.ok(c.includes('file-added'));
    assert.ok(!c.includes('hash-changed'));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('hashes are sha256 of the file bytes, streamed', () => {
  const root = makeVol9(newRoot());
  try {
    // 8 MiB: large enough that a readFileSync implementation would be an
    // obvious memory decision, and big enough to catch a truncating read.
    const big = Buffer.alloc(8 * 1024 * 1024, 0x61);
    const full = path.join(root, ...`${VOL9}/LISTENING/AUDIO/TEST 2.mp4`.split('/'));
    writeFileSync(full, big);

    const { report } = run(root);
    const entry = report.files.find((f) => f.path.endsWith('AUDIO/TEST 2.mp4'));
    assert.equal(entry.sha256, createHash('sha256').update(big).digest('hex'));
    assert.equal(entry.bytes, big.length);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------
// Output discipline
// ---------------------------------------------------------------------------

test('machine-readable output goes to a file; stdout stays human', () => {
  const root = makeVol9(newRoot());
  try {
    const { stdout, out } = run(root);
    assert.ok(existsSync(out));
    assert.ok(
      !stdout.trimStart().startsWith('{'),
      'a stray engine warning on stdout has already corrupted one JSON gate here',
    );
    assert.throws(() => JSON.parse(stdout));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('no answer-key content ever reaches stdout or the report', () => {
  const root = makeVol9(newRoot());
  try {
    const { stdout, stderr, out } = run(root);
    const report = readFileSync(out, 'utf8');
    for (const [what, text] of [
      ['stdout', stdout],
      ['stderr', stderr],
      ['report', report],
    ]) {
      assert.ok(!text.includes(PLANTED_ANSWER), `${what} leaked answer-key content`);
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('the sourceId never carries the Google Drive export segment', () => {
  const root = makeVol9(newRoot());
  try {
    const { report } = run(root);
    for (const s of report.sources) {
      assert.doesNotMatch(s.sourceId, /20260819T082203Z/);
      assert.doesNotMatch(s.sourceId, /-\d{8}T\d{6}Z-\d+-\d+/);
    }
    assert.ok(source(report, 'vol9-real-ielts').present);
    // The volatile path is still recorded — just not as identity.
    assert.match(source(report, 'vol9-real-ielts').resolvedPath, /20260819T082203Z/);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('the report is deterministic and sorted', () => {
  const root = makeVol9(newRoot());
  try {
    const a = run(root).report;
    const b = run(root).report;
    assert.deepEqual(a.files, b.files);
    assert.deepEqual(a.problems, b.problems);
    const paths = a.files.map((f) => f.path);
    assert.deepEqual(paths, [...paths].sort());
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('every path in the report is forward-slashed and NFC-normalised', () => {
  const root = makeVol9(newRoot());
  try {
    const { report } = run(root);
    for (const f of report.files) {
      assert.ok(!f.path.includes('\\'), `backslash leaked: ${f.path}`);
      assert.equal(f.path, f.path.normalize('NFC'));
    }
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------
// Media probing degrades instead of failing
// ---------------------------------------------------------------------------

test('a missing ffprobe is reported, not fatal', () => {
  const root = makeVol9(newRoot());
  try {
    const out = path.join(root, 'inventory.json');
    const res = spawnSync(process.execPath, [SCRIPT, '--root', root, '--out', out], {
      encoding: 'utf8',
      env: { ...process.env, VNI_FFPROBE: path.join(root, 'no-such-ffprobe') },
    });
    assert.notEqual(res.status, 3, 'a missing optional probe is not a usage error');
    const report = JSON.parse(readFileSync(out, 'utf8'));
    assert.equal(report.probe.status, 'unavailable');
    const audio = report.files.find((f) => f.path.endsWith('AUDIO/TEST 1.mp4'));
    assert.equal(audio.media.probed, false);
    assert.equal(audio.media.reason, 'probe-unavailable');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('--no-probe still inventories and says probing was disabled', () => {
  const root = makeVol9(newRoot());
  try {
    const { report } = run(root);
    assert.equal(report.probe.status, 'disabled');
    const audio = report.files.find((f) => f.path.endsWith('AUDIO/TEST 1.mp4'));
    assert.equal(audio.media.reason, 'probe-disabled');
    const docx = report.files.find((f) => f.path.endsWith('TEST 2-R.docx'));
    assert.equal(docx.media.reason, 'not-media');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------
// Read-only
// ---------------------------------------------------------------------------

test('the inventory does not touch the content it reads', () => {
  const root = makeVol9(newRoot());
  try {
    const before = run(root).report.files.map((f) => `${f.path}:${f.sha256}:${f.bytes}`);
    run(root);
    const after = run(root).report.files.map((f) => `${f.path}:${f.sha256}:${f.bytes}`);
    assert.deepEqual(after, before);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('an unexpected extra file is surfaced rather than ignored', () => {
  const root = makeVol9(newRoot());
  try {
    put(root, `${VOL9}/READING/notes about test 4.docx`, 'stray');
    const { report } = run(root);
    const p = report.problems.find((x) => x.code === 'unclaimed-file');
    assert.ok(p, 'a file no pattern claimed is content nobody is inventorying');
    assert.equal(p.severity, 'ambiguity');
    assert.match(p.paths[0], /notes about test 4\.docx$/);
    // Still hashed: it exists, so it is in the ledger.
    assert.ok(report.files.some((f) => f.path.endsWith('notes about test 4.docx')));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('usage errors are their own exit code', () => {
  const root = newRoot();
  try {
    const res = spawnSync(process.execPath, [SCRIPT, '--root'], { encoding: 'utf8' });
    assert.equal(res.status, 3);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------
// Size outliers — a truncated file is worth flagging, but only against the
// right siblings
// ---------------------------------------------------------------------------

test('a truncated file is flagged against its own module, not a pooled median', () => {
  const root = newRoot();
  try {
    // Reading papers are small (~30 KB in the real VOL 9); Listening papers are
    // an order of magnitude bigger (~350 KB). Pooling them under one "paper"
    // median flags six perfectly healthy Reading papers and buries the one
    // Listening file that is actually truncated.
    for (const t of [1, 2, 3, 4]) {
      put(root, `${VOL9}/READING/TEST ${t}-R.docx`, 'r'.repeat(30_000));
      put(root, `${VOL9}/READING/KEY - EXPLAINATION/KEY TEST ${t}-R.docx`, 'k'.repeat(20_000));
      put(
        root,
        `${VOL9}/LISTENING/TEST ${t}-L.docx`,
        t === 2 ? 'l'.repeat(2_000) : 'l'.repeat(350_000),
      );
      put(root, `${VOL9}/LISTENING/KEY - TRANSCRIPT/KEY TEST ${t}-L.docx`, 'k'.repeat(20_000));
      put(root, `${VOL9}/LISTENING/AUDIO/TEST ${t}.mp4`, 'a'.repeat(1_000));
    }

    const { report } = run(root);
    const outliers = report.problems.filter((p) => p.code === 'size-outlier');
    assert.equal(
      outliers.length,
      1,
      `expected only the truncated Listening paper, got ${outliers.map((o) => o.paths[0]).join(', ')}`,
    );
    assert.match(outliers[0].paths[0], /LISTENING\/TEST 2-L\.docx$/);
    assert.equal(outliers[0].module, 'listening');

    const healthy = report.files.find((f) => f.path.endsWith('READING/TEST 1-R.docx'));
    assert.deepEqual(healthy.anomalies, []);
    assert.equal(healthy.module, 'reading');
    assert.equal(healthy.role, 'paper');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

// ---------------------------------------------------------------------------
// Probed durations, and the one-file-per-test question
// ---------------------------------------------------------------------------

// A stub ffprobe. It reports whatever duration the fixture file's own bytes
// ask for, which keeps this test honest about the script's parsing without
// requiring ffmpeg on the machine running it — CI has no ffmpeg.
function stubProbe(root) {
  const file = path.join(root, 'stub-ffprobe.mjs');
  writeFileSync(
    file,
    `
import { readFileSync } from 'node:fs';
const args = process.argv.slice(2);
if (args.includes('-version')) { console.log('ffprobe version stub'); process.exit(0); }
const target = args[args.length - 1];
const seconds = Number(readFileSync(target, 'utf8').split('DURATION=')[1] ?? 0);
console.log(JSON.stringify({
  format: { duration: String(seconds), format_name: 'mp3' },
  streams: [{ codec_type: 'audio', codec_name: 'mp3', sample_rate: '44100', channels: 2 }],
}));
`,
    'utf8',
  );
  return file;
}

function runProbed(root, extra = []) {
  const out = path.join(root, 'inventory.json');
  const res = spawnSync(process.execPath, [SCRIPT, '--root', root, '--out', out, ...extra], {
    encoding: 'utf8',
    env: { ...process.env, VNI_FFPROBE: stubProbe(root) },
  });
  const report = existsSync(out) ? JSON.parse(readFileSync(out, 'utf8')) : null;
  return { ...res, out, report };
}

test('probed duration and codec reach the report', () => {
  const root = newRoot();
  try {
    put(root, 'Đề IELTS/Đề CAM/Cam 16/Cam 16.pdf', 'book');
    for (const p of [1, 2, 3, 4]) {
      put(root, `Đề IELTS/Đề CAM/Cam 16/Test 1 Part ${p}.mp3`, 'DURATION=450');
    }
    const { report } = runProbed(root);
    assert.equal(report.probe.status, 'available');
    const f = report.files.find((x) => x.path.endsWith('Test 1 Part 1.mp3'));
    assert.equal(f.media.probed, true);
    assert.equal(f.media.durationSeconds, 450);
    assert.equal(f.media.container, 'mp3');
    assert.equal(f.media.audio.codec, 'mp3');
    assert.equal(f.media.audio.sampleRateHz, 44100);
    assert.deepEqual(f.media.codecs, ['audio:mp3']);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('one audio file carrying a whole test is observed, not called missing', () => {
  const root = newRoot();
  try {
    // Cam 21 ships four files where the other books ship sixteen. Size alone
    // could not tell "concatenated test" from "twelve files lost"; duration can.
    put(root, 'Đề IELTS/Đề CAM/Cam 21/Cambridge IELTS 21.pdf', 'book');
    for (const t of [1, 2]) {
      put(
        root,
        `Đề IELTS/Đề CAM/Cam 21/Cambridge IELTS 21 Audio/Cambridge 21 - Test ${t}.mp3`,
        'DURATION=1800',
      );
    }
    const { report } = runProbed(root);
    const mod = source(report, 'cambridge-21').modules[0];

    const obs = mod.observations.find((o) => o.code === 'audio-likely-concatenated');
    assert.ok(obs, 'a 30-minute single file is a whole Listening test, and FS1 needs to know');
    assert.equal(obs.evidence.filesPerTest, 1);
    assert.equal(obs.evidence.medianDurationSeconds, 1800);
    assert.equal(report.problems.filter((p) => p.code === 'missing-audio').length, 0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('four part-length files per test are not called concatenated', () => {
  const root = newRoot();
  try {
    put(root, 'Đề IELTS/Đề CAM/Cam 16/Cam 16.pdf', 'book');
    for (const t of [1, 2]) {
      for (const p of [1, 2, 3, 4]) {
        put(root, `Đề IELTS/Đề CAM/Cam 16/Test ${t} Part ${p}.mp3`, 'DURATION=450');
      }
    }
    const { report } = runProbed(root);
    const mod = source(report, 'cambridge-16').modules[0];
    assert.deepEqual(mod.audioCardinality.observed, [4]);
    assert.equal(mod.observations.filter((o) => o.code === 'audio-likely-concatenated').length, 0);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test('no duration means no concatenation claim either way', () => {
  const root = newRoot();
  try {
    put(root, 'Đề IELTS/Đề CAM/Cam 21/Cambridge IELTS 21.pdf', 'book');
    for (const t of [1, 2]) {
      put(
        root,
        `Đề IELTS/Đề CAM/Cam 21/Cambridge IELTS 21 Audio/Cambridge 21 - Test ${t}.mp3`,
        'x',
      );
    }
    const { report } = run(root); // --no-probe
    const mod = source(report, 'cambridge-21').modules[0];
    assert.deepEqual(mod.observations, [], 'unprobed is unknown, and unknown is not an answer');
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
