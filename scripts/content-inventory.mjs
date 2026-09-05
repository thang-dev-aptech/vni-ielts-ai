#!/usr/bin/env node
//
// FS0.2 — machine-readable inventory of the IELTS source content.
//
// What this is for
// ----------------
// The plan's constraint is "không dựa vào filename thủ công trong application":
// no part of the application may depend on a human having matched a test to its
// key by eye. This script does that matching once, in one place, writes the
// result to a file, and — crucially — writes down what it could NOT match.
//
// It is READ-ONLY with respect to content. It opens files to stream their bytes
// through a hash and, optionally, to let `ffprobe` read a media header. It never
// writes, moves, or deletes anything under a source directory, and it never
// echoes a byte of what it read: an answer key is hashed, never quoted.
//
// Why the output is a file and not stdout
// ---------------------------------------
// This machine runs Node v22 against an .nvmrc that says 24, so pnpm prints
// `WARN Unsupported engine` on stdout. That has already corrupted one
// JSON-capturing gate in this repo. Machine-readable output therefore goes to
// --out; stdout carries a human summary only.
//
// Exit codes — the distinction is the point
// -----------------------------------------
//   0  inventoried, no problems
//   1  inventoried, and found problems (missing file, ambiguity, hash drift,
//      or a source directory that should be here and is not)
//   2  NOTHING to inventory — every configured source directory is absent.
//      This is the CI and clean-checkout case: `/exam/` and `/Đề IELTS/` are
//      gitignored. It must never be confused with a clean bill of health.
//   3  usage error
//
// Usage:
//     node scripts/content-inventory.mjs [--root DIR] [--out FILE]
//                                        [--baseline FILE | --no-baseline]
//                                        [--no-probe]
//
// --baseline defaults to --out when that file already exists, so a second run
// compares against the first and a changed file surfaces as `hash-changed`.

import { createHash } from 'node:crypto';
import {
  createReadStream,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  statSync,
  writeFileSync,
} from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const SCHEMA_VERSION = 1;
const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MEDIA_EXTENSIONS = new Set([
  '.mp3',
  '.mp4',
  '.m4a',
  '.wav',
  '.ogg',
  '.webm',
  '.aac',
  '.flac',
]);
const MAX_DEPTH = 8;

// ---------------------------------------------------------------------------
// One choke point for path shape. Everything downstream sees forward slashes
// and NFC. macOS hands back NFD filenames for the Vietnamese directory names
// here and Windows hands back NFC, so `Đề IELTS` compared raw against a literal
// is a comparison that silently never matches on one of the two.
// ---------------------------------------------------------------------------

const nfc = (s) => s.normalize('NFC');
const toPosix = (p) => nfc(p.split(path.sep).join('/'));

// ---------------------------------------------------------------------------
// Source descriptors.
//
// Every pattern below is TOLERANT by construction, because the real filenames
// are not. The anomalies encoded here were found by survey, each in exactly one
// or two files, and each one silently loses a whole test under a strict matcher:
//
//   * `KET TEST 2-R.docx`   — "KEY" misspelled, one file
//   * `TEST 1 -R.docx`      — stray space before the hyphen, one file
//   * `KEY - EXPLAINATION`  — the DIRECTORY is misspelled; hard-coding the
//                             correct spelling matches nothing at all
//   * `IELTS17_t4_audio1`   — missing the `ELT_` prefix its 15 siblings carry
//   * Cam 18                — four separator spellings across 16 files
//
// A `dir` regex matches the directory path RELATIVE to the source root, posix
// and NFC, with '' meaning the source root itself.
// ---------------------------------------------------------------------------

const NOT_APPLICABLE_READING_AUDIO = 'IELTS Reading has no audio. Absent by design, not missing.';
const NOT_APPLICABLE_CAMBRIDGE_KEY =
  'The answer key is printed inside the book PDF, so filesystem key-pairing does not apply to this source.';

/** Cambridge books: one PDF for the whole book, loose audio, no key files. */
function cambridge(id, label, dirName, audio) {
  return {
    id,
    label,
    locate: { at: `Đề IELTS/Đề CAM/${dirName}` },
    modules: [
      {
        module: 'listening',
        paper: { kind: 'book', dir: /^$/, pattern: /\.pdf$/i },
        key: { kind: 'none', reason: NOT_APPLICABLE_CAMBRIDGE_KEY },
        audio,
      },
    ],
  };
}

const SOURCES = [
  {
    id: 'vol9-real-ielts',
    label: 'VOL 9 — REAL IELTS (Reading and Listening only)',
    // The on-disk parent ends in `-20260819T082203Z-1-001`, a Google Drive
    // export artefact. It is matched loosely and NEVER used to build the id:
    // a re-export renames the directory and would silently orphan every hash
    // recorded against it.
    locate: {
      under: 'Đề IELTS/Đề CAM',
      match: /VOL\s*9\s*-\s*REAL\s*IELTS/i,
      into: /^VOL\s*9\s*-\s*REAL\s*IELTS$/i,
    },
    modules: [
      {
        module: 'reading',
        paper: {
          kind: 'per-test',
          dir: /^READING$/i,
          pattern: /^TEST\s*(\d+)\s*-\s*R\.docx$/i,
          test: (m) => m[1],
        },
        key: {
          // `[A-Z]+` rather than the word: the real directory is misspelled.
          kind: 'per-test',
          dir: /^READING\/KEY\s*-\s*[A-Z]+$/i,
          pattern: /^(KEY|KET)\s+TEST\s*(\d+)\s*-\s*R\.docx$/i,
          test: (m) => m[2],
          anomaly: (m) => (m[1].toUpperCase() === 'KEY' ? null : 'misspelled-key-prefix'),
        },
        audio: { kind: 'none', reason: NOT_APPLICABLE_READING_AUDIO },
      },
      {
        module: 'listening',
        paper: {
          kind: 'per-test',
          dir: /^LISTENING$/i,
          pattern: /^TEST\s*(\d+)\s*-\s*L\.docx$/i,
          test: (m) => m[1],
        },
        key: {
          kind: 'per-test',
          dir: /^LISTENING\/KEY\s*-\s*[A-Z]+$/i,
          pattern: /^(KEY|KET)\s+TEST\s*(\d+)\s*-\s*L\.docx$/i,
          test: (m) => m[2],
          anomaly: (m) => (m[1].toUpperCase() === 'KEY' ? null : 'misspelled-key-prefix'),
        },
        audio: {
          // One .mp4 per test — a VIDEO container holding audio. Cardinality
          // here is 1:1; Cam 16-20 is 1:4. Neither is assumed anywhere.
          kind: 'per-test',
          dir: /^LISTENING\/AUDIO$/i,
          pattern: /^TEST\s*(\d+)\.mp4$/i,
          test: (m) => m[1],
          part: () => null,
        },
      },
    ],
  },

  cambridge('cambridge-16', 'Cambridge IELTS 16', 'Cam 16', {
    kind: 'per-test',
    dir: /^$/,
    pattern: /^Test\s*(\d+)\s*Part\s*(\d+)\.mp3$/i,
    test: (m) => m[1],
    part: (m) => m[2],
  }),

  cambridge('cambridge-17', 'Cambridge IELTS 17', 'Cam 17', {
    kind: 'per-test',
    dir: /^$/,
    // The `ELT_` prefix is optional because exactly one of sixteen files omits it.
    pattern: /^(ELT_)?IELTS\s*17_t(\d+)_audio(\d+)\.mp3$/i,
    test: (m) => m[2],
    part: (m) => m[3],
    anomaly: (m) => (m[1] ? null : 'irregular-prefix'),
  }),

  cambridge('cambridge-18', 'Cambridge IELTS 18', 'Cam 18', {
    kind: 'per-test',
    dir: /^$/,
    pattern: /^18\s*section\s*(\d+)\s*-?\s*part\s*(\d+)\.mp3$/i,
    test: (m) => m[1],
    part: (m) => m[2],
    // Deliberately unresolved. The 4x4 shape says one axis is the test and the
    // other the part, but the LABELS say "section"/"part", which is inverted
    // versus IELTS usage where a Listening test has four parts. Nothing in the
    // filenames settles which number is which, so this source produces no test
    // slots at all — the files are inventoried and left unassigned. Guessing
    // here would put Test 2's audio on Test 1's paper.
    axisResolved: false,
    axisNote:
      'Filenames carry two numbers labelled "section" and "part". IELTS vocabulary makes that ' +
      'inverted, and nothing in the filename settles which axis is the test. Audio is ' +
      'inventoried but left unassigned until the PDF is read.',
  }),

  cambridge('cambridge-19', 'Cambridge IELTS 19', 'Cam 19', {
    kind: 'per-test',
    dir: /^$/,
    pattern: /^Test\s*(\d+)\s*Part\s*(\d+)\.mp3$/i,
    test: (m) => m[1],
    part: (m) => m[2],
  }),

  cambridge('cambridge-20', 'Cambridge IELTS 20', 'Cam 20', {
    kind: 'per-test',
    dir: /^audio$/i,
    pattern: /^T(\d+)S(\d+)\.m4a$/i,
    test: (m) => m[1],
    part: (m) => m[2],
  }),

  cambridge('cambridge-21', 'Cambridge IELTS 21', 'Cam 21', {
    kind: 'per-test',
    dir: /^Cambridge\s*IELTS\s*21\s*Audio$/i,
    // One file per test where the other books have four. Whether that is a
    // concatenated test or three missing files is a duration question, not a
    // filename question — see `concatenationSuspected` below.
    pattern: /^Cambridge\s*21\s*-\s*Test\s*(\d+)\.mp3$/i,
    test: (m) => m[1],
    part: () => null,
  }),

  {
    id: 'exam1-package',
    label: 'exam/Exam1 — authored Package Format v1 fixture',
    locate: { at: 'exam/Exam1' },
    // Everything else in the package (README, _source/, writing/, speaking/,
    // images) is real content that belongs in the ledger but pairs through the
    // manifest, not through filenames. It is hashed and marked `supporting`
    // rather than reported as unclaimed.
    supportingCatchAll: true,
    modules: [
      {
        module: 'reading',
        paper: {
          kind: 'per-test',
          dir: /^reading$/i,
          pattern: /^section\.json$/i,
          test: () => '1',
        },
        key: { kind: 'book', dir: /^$/, pattern: /^answer-keys\.json$/i },
        audio: { kind: 'none', reason: NOT_APPLICABLE_READING_AUDIO },
      },
      {
        module: 'listening',
        paper: {
          kind: 'per-test',
          dir: /^listening$/i,
          pattern: /^section\.json$/i,
          test: () => '1',
        },
        key: { kind: 'book', dir: /^$/, pattern: /^answer-keys\.json$/i },
        audio: {
          kind: 'per-test',
          dir: /^assets\/audio$/i,
          pattern: /^listening-part(\d+)\.mp3$/i,
          test: () => '1',
          part: (m) => m[1],
        },
      },
    ],
  },
];

// ---------------------------------------------------------------------------
// Argument parsing
// ---------------------------------------------------------------------------

function usage(message) {
  console.error(`content-inventory: ${message}`);
  console.error(
    'usage: node scripts/content-inventory.mjs [--root DIR] [--out FILE] ' +
      '[--baseline FILE | --no-baseline] [--no-probe]',
  );
  process.exit(3);
}

function parseArgs(argv) {
  const opts = {
    root: process.env.VNI_CONTENT_ROOT || REPO_ROOT,
    out: process.env.VNI_CONTENT_INVENTORY_OUT || null,
    baseline: null,
    noBaseline: false,
    probe: true,
  };
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    const next = () => {
      const v = argv[i + 1];
      if (v === undefined || v.startsWith('--')) usage(`${arg} needs a value`);
      i += 1;
      return v;
    };
    if (arg === '--root') opts.root = next();
    else if (arg === '--out') opts.out = next();
    else if (arg === '--baseline') opts.baseline = next();
    else if (arg === '--no-baseline') opts.noBaseline = true;
    else if (arg === '--no-probe') opts.probe = false;
    else if (arg === '--help' || arg === '-h') usage('help');
    else usage(`unknown argument ${arg}`);
  }
  opts.root = path.resolve(opts.root);
  opts.out = opts.out
    ? path.resolve(opts.out)
    : path.join(opts.root, '_workspace', 'content-inventory.json');
  return opts;
}

// ---------------------------------------------------------------------------
// Filesystem walk — files only, bounded depth, no symlink following
// ---------------------------------------------------------------------------

function walk(dir, relBase, out, depth = 0) {
  if (depth > MAX_DEPTH) return;
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    const rel = relBase ? `${relBase}/${nfc(entry.name)}` : nfc(entry.name);
    if (entry.isDirectory()) walk(full, rel, out, depth + 1);
    else if (entry.isFile()) out.push({ full, rel, rawName: entry.name, name: nfc(entry.name) });
  }
}

// ---------------------------------------------------------------------------
// Hashing — streamed. A 1.3 GB archive in a sibling directory once killed a
// Node tool here by being slurped into a string; nothing in this file ever
// holds a content file in memory.
// ---------------------------------------------------------------------------

function sha256Stream(file) {
  return new Promise((resolve, reject) => {
    const hash = createHash('sha256');
    const stream = createReadStream(file, { highWaterMark: 1024 * 1024 });
    stream.on('error', reject);
    stream.on('data', (chunk) => hash.update(chunk));
    stream.on('end', () => resolve(hash.digest('hex')));
  });
}

// ---------------------------------------------------------------------------
// Media probing — optional, and its absence is a reported state, not a crash
// ---------------------------------------------------------------------------

function resolveProbe(enabled) {
  if (!enabled) return { status: 'disabled', tool: null, command: null, prefixArgs: [] };
  const tool = process.env.VNI_FFPROBE || 'ffprobe';
  // A `.mjs` tool is run through this Node binary. That is a deliberate test
  // seam: CI has no ffmpeg, and the duration reasoning further down is worth
  // testing on a machine that lacks one.
  const viaNode = /\.(mjs|cjs|js)$/i.test(tool);
  const command = viaNode ? process.execPath : tool;
  const prefixArgs = viaNode ? [tool] : [];
  const res = spawnSync(command, [...prefixArgs, '-version'], { encoding: 'utf8', timeout: 15000 });
  if (res.error || res.status !== 0) return { status: 'unavailable', tool, command, prefixArgs };
  const version = (res.stdout || '').split('\n')[0].trim();
  return { status: 'available', tool, version, command, prefixArgs };
}

function probeMedia(probe, file) {
  const res = spawnSync(
    probe.command,
    [
      ...probe.prefixArgs,
      '-v',
      'error',
      '-print_format',
      'json',
      '-show_format',
      '-show_streams',
      file,
    ],
    { encoding: 'utf8', maxBuffer: 8 * 1024 * 1024, timeout: 60000 },
  );
  if (res.error || res.status !== 0) return { probed: false, reason: 'probe-failed' };
  let parsed;
  try {
    parsed = JSON.parse(res.stdout);
  } catch {
    return { probed: false, reason: 'probe-failed' };
  }
  const streams = Array.isArray(parsed.streams) ? parsed.streams : [];
  const audio = streams.find((s) => s.codec_type === 'audio');
  const duration = Number(parsed.format?.duration);
  return {
    probed: true,
    container: parsed.format?.format_name ?? null,
    durationSeconds: Number.isFinite(duration) ? Math.round(duration * 1000) / 1000 : null,
    codecs: streams.map((s) => `${s.codec_type}:${s.codec_name}`).sort(),
    audio: audio
      ? {
          codec: audio.codec_name ?? null,
          sampleRateHz: audio.sample_rate ? Number(audio.sample_rate) : null,
          channels: audio.channels ?? null,
        }
      : null,
  };
}

// ---------------------------------------------------------------------------
// Filename anomalies. These are recorded, not corrected — the file on disk is
// the file on disk.
// ---------------------------------------------------------------------------

function whitespaceAnomaly(name) {
  const tidied = name
    .replace(/\s+/g, ' ')
    .replace(/\s*-\s*/g, '-')
    .trim();
  const comparable = name.replace(/\s+/g, ' ').trim();
  return tidied !== comparable ? 'irregular-whitespace' : null;
}

function median(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

// ---------------------------------------------------------------------------
// Locating a source directory
// ---------------------------------------------------------------------------

function locateSource(root, locate) {
  if (locate.at) {
    const full = path.join(root, ...locate.at.split('/'));
    if (existsSync(full) && statSync(full).isDirectory()) return { full, rel: locate.at };
    return { full: null, rel: locate.at };
  }
  const parent = path.join(root, ...locate.under.split('/'));
  if (!existsSync(parent)) return { full: null, rel: `${locate.under}/ (parent absent)` };
  let entries;
  try {
    entries = readdirSync(parent, { withFileTypes: true }).filter((e) => e.isDirectory());
  } catch {
    return { full: null, rel: locate.under };
  }
  const hit = entries.find((e) => locate.match.test(nfc(e.name)));
  if (!hit) return { full: null, rel: `${locate.under}/<matching ${locate.match}>` };
  let full = path.join(parent, hit.name);
  let rel = `${locate.under}/${nfc(hit.name)}`;
  if (locate.into) {
    const inner = readdirSync(full, { withFileTypes: true })
      .filter((e) => e.isDirectory())
      .find((e) => locate.into.test(nfc(e.name)));
    if (!inner) return { full: null, rel: `${rel}/<matching ${locate.into}>` };
    full = path.join(full, inner.name);
    rel = `${rel}/${nfc(inner.name)}`;
  }
  return { full, rel };
}

// ---------------------------------------------------------------------------
// The inventory itself
// ---------------------------------------------------------------------------

function emptySlot(status, reason) {
  return { status, files: [], candidates: [], ...(reason ? { reason } : {}) };
}

/** Group matches by test id; return {slot, ambiguousGroups}. */
function resolveSingleFileSlot(matches) {
  if (matches.length === 0) return emptySlot('missing');
  if (matches.length > 1) {
    // Two files claim one slot. Choosing one would be inventing a fact.
    return { status: 'ambiguous', files: [], candidates: matches.map((m) => m.path).sort() };
  }
  return { status: 'paired', files: [matches[0].path], candidates: [] };
}

async function inventorySource(descriptor, opts, probe, problems, fileEntries) {
  const located = locateSource(opts.root, descriptor.locate);
  if (!located.full) {
    problems.push({
      severity: 'absent',
      code: 'source-absent',
      sourceId: descriptor.id,
      module: null,
      test: null,
      message:
        `source directory absent at ${located.rel} — it is gitignored, so this is expected in ` +
        'CI and in a clean checkout. Nothing was inventoried for it.',
      paths: [],
    });
    return {
      sourceId: descriptor.id,
      label: descriptor.label,
      present: false,
      expectedPath: located.rel,
      resolvedPath: null,
      absenceReason: `no directory at ${located.rel}`,
      modules: [],
      fileCount: 0,
      bytes: 0,
    };
  }

  const found = [];
  walk(located.full, '', found);

  // Hash and probe every file, whether or not a pattern claims it. A file the
  // patterns do not recognise is exactly the file worth knowing about.
  const claimed = new Map(); // rel -> {role, module, anomalies[]}
  const byRel = new Map();
  for (const f of found) byRel.set(f.rel, f);

  const modules = [];
  for (const mod of descriptor.modules) {
    const module = { module: mod.module, testAxis: { resolved: true }, observations: [] };
    const tests = new Set();

    const matchAll = (spec, role) => {
      if (!spec || spec.kind === 'none') return [];
      const out = [];
      for (const f of found) {
        const dir = f.rel.includes('/') ? f.rel.slice(0, f.rel.lastIndexOf('/')) : '';
        if (!spec.dir.test(dir)) continue;
        const m = spec.pattern.exec(f.name);
        if (!m) continue;
        const anomalies = [];
        const specific = spec.anomaly ? spec.anomaly(m) : null;
        if (specific) anomalies.push(specific);
        const ws = whitespaceAnomaly(f.name);
        if (ws) anomalies.push(ws);
        if (f.rawName !== f.name) anomalies.push('unicode-nfd-on-disk');
        out.push({
          path: `${located.rel}/${f.rel}`,
          rel: f.rel,
          test: spec.test ? String(spec.test(m)) : null,
          part: spec.part ? (spec.part(m) === null ? null : String(spec.part(m))) : null,
          anomalies,
          role,
        });
        const prior = claimed.get(f.rel);
        claimed.set(f.rel, {
          // First claimer wins the label. `answer-keys.json` in the Exam1
          // package is the key for both modules; it is listed once.
          role: prior ? prior.role : role,
          module: prior ? prior.module : mod.module,
          anomalies: [...new Set([...(prior?.anomalies ?? []), ...anomalies])],
        });
      }
      return out;
    };

    const papers = matchAll(mod.paper, mod.paper?.kind === 'book' ? 'book-paper' : 'paper');
    const keys = matchAll(mod.key, mod.key?.kind === 'book' ? 'book-key' : 'key');
    const audio = matchAll(mod.audio, 'audio');

    // Book-level artefacts cover every test in the source rather than one.
    if (mod.paper?.kind === 'book') {
      module.bookPaper = resolveSingleFileSlot(papers);
    }
    if (mod.key?.kind === 'book') {
      module.bookKey = resolveSingleFileSlot(keys);
    }

    if (mod.paper?.kind === 'per-test') for (const p of papers) tests.add(p.test);
    if (mod.key?.kind === 'per-test') for (const k of keys) tests.add(k.test);

    const axisResolved = mod.audio?.axisResolved !== false;
    if (!axisResolved) {
      module.testAxis = { resolved: false, note: mod.audio.axisNote };
      module.unassignedAudio = audio.map((a) => a.path).sort();
      problems.push({
        severity: 'ambiguity',
        code: 'ambiguous-test-axis',
        sourceId: descriptor.id,
        module: mod.module,
        test: null,
        message: mod.audio.axisNote,
        paths: audio.map((a) => a.path).sort(),
      });
    } else if (mod.audio?.kind === 'per-test') {
      for (const a of audio) tests.add(a.test);
    }

    const testIds = [...tests].sort((a, b) => Number(a) - Number(b) || a.localeCompare(b));
    module.tests = [];
    const perTestAudioCount = {};

    for (const id of testIds) {
      const entry = { test: id };

      if (!mod.paper) entry.paper = emptySlot('not-applicable', 'no paper for this module');
      else if (mod.paper.kind === 'book') {
        entry.paper = {
          status: 'book-level',
          files: module.bookPaper.files,
          candidates: module.bookPaper.candidates,
          reason: 'One book PDF covers every test in this source.',
        };
      } else entry.paper = resolveSingleFileSlot(papers.filter((p) => p.test === id));

      if (!mod.key || mod.key.kind === 'none')
        entry.key = emptySlot('not-applicable', mod.key?.reason ?? 'no key for this module');
      else if (mod.key.kind === 'book') {
        entry.key = {
          status: 'book-level',
          files: module.bookKey.files,
          candidates: module.bookKey.candidates,
          reason: 'One key file covers every test in this source.',
        };
      } else entry.key = resolveSingleFileSlot(keys.filter((k) => k.test === id));

      if (!mod.audio || mod.audio.kind === 'none') {
        entry.audio = emptySlot('not-applicable', mod.audio?.reason ?? 'no audio for this module');
      } else if (!axisResolved) {
        entry.audio = emptySlot('unassigned', mod.audio.axisNote);
      } else {
        const mine = audio.filter((a) => a.test === id);
        const parts = mine.map((a) => a.part).filter((p) => p !== null);
        const dupes = parts.filter((p, i) => parts.indexOf(p) !== i);
        perTestAudioCount[id] = mine.length;
        if (mine.length === 0) entry.audio = emptySlot('missing');
        else if (dupes.length > 0) {
          entry.audio = {
            status: 'ambiguous',
            files: [],
            candidates: mine.map((a) => a.path).sort(),
            parts: [...new Set(parts)].sort(),
          };
        } else {
          entry.audio = {
            status: 'paired',
            files: mine.map((a) => a.path).sort(),
            candidates: [],
            parts: parts.sort(),
          };
        }
      }
      module.tests.push(entry);
    }

    // Cardinality is OBSERVED, never assumed. VOL 9 is one audio file per test,
    // Cam 16-20 four, Cam 21 one. A source whose own tests disagree with each
    // other is the interesting case.
    const counts = Object.values(perTestAudioCount);
    const observed = [...new Set(counts)].sort((a, b) => a - b);
    module.audioCardinality = {
      observed,
      consistent: observed.length <= 1,
      perTest: perTestAudioCount,
    };
    if (observed.length > 1) {
      problems.push({
        severity: 'ambiguity',
        code: 'inconsistent-audio-cardinality',
        sourceId: descriptor.id,
        module: mod.module,
        test: null,
        message:
          `tests in this module carry ${observed.join(' and ')} audio files respectively. ` +
          'Either the source mixes conventions or files are missing; the filenames do not say which.',
        paths: [],
      });
    }

    // Missing-file problems, raised only where the slot is genuinely expected.
    for (const entry of module.tests) {
      for (const [kind, slot] of [
        ['paper', entry.paper],
        ['key', entry.key],
        ['audio', entry.audio],
      ]) {
        if (slot.status === 'missing') {
          problems.push({
            severity: 'error',
            code: `missing-${kind}`,
            sourceId: descriptor.id,
            module: mod.module,
            test: entry.test,
            message: `test ${entry.test} has no ${kind} file, while its siblings in this module do`,
            paths: [],
          });
        } else if (slot.status === 'ambiguous') {
          problems.push({
            severity: 'ambiguity',
            code: `ambiguous-${kind}`,
            sourceId: descriptor.id,
            module: mod.module,
            test: entry.test,
            message:
              `${slot.candidates.length} files match the ${kind} slot for test ${entry.test}. ` +
              'Left unresolved deliberately — picking one would invent a pairing.',
            paths: slot.candidates,
          });
        }
      }
    }

    modules.push(module);
  }

  // Hash every file found, claimed or not.
  let bytes = 0;
  let count = 0;
  for (const f of found.sort((a, b) => a.rel.localeCompare(b.rel))) {
    const claim = claimed.get(f.rel);
    const anomalies = new Set(claim?.anomalies ?? []);
    if (f.rawName !== f.name) anomalies.add('unicode-nfd-on-disk');
    let role = claim?.role ?? (descriptor.supportingCatchAll ? 'supporting' : 'unclaimed');
    const stat = statSync(f.full);
    const ext = path.extname(f.name).toLowerCase();
    let media;
    if (!MEDIA_EXTENSIONS.has(ext)) media = { probed: false, reason: 'not-media' };
    else if (probe.status === 'disabled') media = { probed: false, reason: 'probe-disabled' };
    else if (probe.status === 'unavailable') media = { probed: false, reason: 'probe-unavailable' };
    else media = probeMedia(probe, f.full);

    if (media.probed === false && media.reason === 'probe-failed') {
      problems.push({
        severity: 'ambiguity',
        code: 'probe-failed',
        sourceId: descriptor.id,
        module: null,
        test: null,
        message: 'ffprobe could not read this media file; duration and codec are unknown',
        paths: [`${located.rel}/${f.rel}`],
      });
    }

    if (role === 'unclaimed') {
      problems.push({
        severity: 'ambiguity',
        code: 'unclaimed-file',
        sourceId: descriptor.id,
        module: null,
        test: null,
        message:
          'no pattern in this source descriptor claims this file, so nothing pairs it to a test. ' +
          'It is hashed and listed, but the application cannot use it.',
        paths: [`${located.rel}/${f.rel}`],
      });
    }

    fileEntries.push({
      sourceId: descriptor.id,
      path: `${located.rel}/${f.rel}`,
      module: claim?.module ?? null,
      role,
      bytes: stat.size,
      sha256: await sha256Stream(f.full),
      anomalies: [...anomalies].sort(),
      media,
    });
    bytes += stat.size;
    count += 1;
  }

  // One audio file per test: a concatenated whole test, or three files lost?
  //
  // Size alone cannot tell those apart, which is why the survey could only guess
  // about Cam 21. Duration can: a single IELTS Listening PART runs six to eight
  // minutes and a whole test about thirty. This is recorded as an OBSERVATION
  // carrying its own evidence — never as a pairing, and never as a missing-file
  // claim. An unprobed file yields no claim in either direction.
  const mediaByPath = new Map(
    fileEntries.filter((e) => e.sourceId === descriptor.id).map((e) => [e.path, e]),
  );
  const FULL_TEST_SECONDS = 1200;
  for (const module of modules) {
    if (!module.testAxis.resolved) continue;
    if (module.audioCardinality.observed.length !== 1) continue;
    if (module.audioCardinality.observed[0] !== 1) continue;
    const durations = [];
    for (const t of module.tests) {
      for (const one of t.audio.files ?? []) {
        const d = mediaByPath.get(one)?.media?.durationSeconds;
        if (typeof d === 'number') durations.push(d);
      }
    }
    if (durations.length === 0 || durations.length !== module.tests.length) continue;
    const mid = median(durations);
    if (mid < FULL_TEST_SECONDS) continue;
    module.observations.push({
      code: 'audio-likely-concatenated',
      message:
        `each test carries exactly one audio file with a median duration of ${Math.round(mid)}s. ` +
        'That is a whole Listening test in one file rather than one part, so part-level playback ' +
        'needs offsets and the other three quarters of a 1:4 source are not missing.',
      evidence: {
        filesPerTest: 1,
        medianDurationSeconds: Math.round(mid * 1000) / 1000,
        testsMeasured: durations.length,
      },
    });
  }

  // Size outliers, computed once every size is known.
  //
  // The grouping key is module AND role, not role alone. Pooling them is a real
  // defect this script had: VOL 9 Reading papers run ~30 KB and Listening papers
  // ~350 KB, so one shared "paper" median flagged six healthy Reading files and
  // said nothing useful about the one Listening paper that is actually a 10x
  // outlier. Only meaningful with enough siblings for a median worth trusting.
  const byGroup = new Map();
  for (const e of fileEntries.filter((e) => e.sourceId === descriptor.id)) {
    if (e.role === 'unclaimed' || e.role === 'supporting') continue;
    const key = `${e.module ?? '-'}|${e.role}`;
    if (!byGroup.has(key)) byGroup.set(key, []);
    byGroup.get(key).push(e);
  }
  for (const [key, group] of byGroup) {
    if (group.length < 4) continue;
    const mid = median(group.map((g) => g.bytes));
    for (const e of group) {
      if (mid > 0 && e.bytes < mid * 0.25) {
        e.anomalies = [...new Set([...e.anomalies, 'size-outlier'])].sort();
        problems.push({
          severity: 'ambiguity',
          code: 'size-outlier',
          sourceId: descriptor.id,
          module: e.module,
          test: null,
          message:
            `${e.bytes} bytes against a ${key.replaceAll('|', ' ')} median of ${mid} — possibly ` +
            'truncated. Flagged on size alone; the file was not opened.',
          paths: [e.path],
        });
      }
    }
  }

  return {
    sourceId: descriptor.id,
    label: descriptor.label,
    present: true,
    expectedPath: located.rel,
    resolvedPath: located.rel,
    absenceReason: null,
    modules,
    fileCount: count,
    bytes,
  };
}

// ---------------------------------------------------------------------------
// Baseline comparison — the drift half of the phase gate
// ---------------------------------------------------------------------------

function compareBaseline(baselinePath, fileEntries, problems) {
  let baseline;
  try {
    baseline = JSON.parse(readFileSync(baselinePath, 'utf8'));
  } catch (err) {
    problems.push({
      severity: 'error',
      code: 'baseline-unreadable',
      sourceId: null,
      module: null,
      test: null,
      message: `could not read baseline ${toPosix(baselinePath)}: ${err.message}`,
      paths: [],
    });
    return 0;
  }
  const before = new Map((baseline.files ?? []).map((f) => [f.path, f]));
  const after = new Map(fileEntries.map((f) => [f.path, f]));
  let changes = 0;

  for (const [p, current] of after) {
    const prior = before.get(p);
    if (!prior) {
      problems.push({
        severity: 'ambiguity',
        code: 'file-added',
        sourceId: current.sourceId,
        module: null,
        test: null,
        message: 'present now, absent from the baseline',
        paths: [p],
      });
      continue;
    }
    if (prior.sha256 !== current.sha256) {
      changes += 1;
      problems.push({
        severity: 'error',
        code: 'hash-changed',
        sourceId: current.sourceId,
        module: null,
        test: null,
        message:
          'the bytes of this file changed since the baseline. Anything derived from it — a ' +
          'published package, a parsed answer key — is now stale.',
        paths: [p],
        baselineSha256: prior.sha256,
        currentSha256: current.sha256,
        baselineBytes: prior.bytes ?? null,
        currentBytes: current.bytes,
      });
    }
  }
  for (const [p, prior] of before) {
    if (after.has(p)) continue;
    problems.push({
      severity: 'error',
      code: 'file-removed',
      sourceId: prior.sourceId ?? null,
      module: null,
      test: null,
      message: 'in the baseline, absent now',
      paths: [p],
    });
  }
  return changes;
}

// ---------------------------------------------------------------------------

const PROBLEM_ORDER = { error: 0, ambiguity: 1, absent: 2 };

function sortProblems(problems) {
  problems.sort(
    (a, b) =>
      PROBLEM_ORDER[a.severity] - PROBLEM_ORDER[b.severity] ||
      (a.sourceId ?? '').localeCompare(b.sourceId ?? '') ||
      a.code.localeCompare(b.code) ||
      (a.module ?? '').localeCompare(b.module ?? '') ||
      (a.test ?? '').localeCompare(b.test ?? '') ||
      (a.paths[0] ?? '').localeCompare(b.paths[0] ?? ''),
  );
}

async function main() {
  const opts = parseArgs(process.argv.slice(2));
  const probe = resolveProbe(opts.probe);

  const problems = [];
  const fileEntries = [];
  const sources = [];
  for (const descriptor of SOURCES) {
    sources.push(await inventorySource(descriptor, opts, probe, problems, fileEntries));
  }
  fileEntries.sort((a, b) => a.path.localeCompare(b.path));

  let baselinePath = null;
  if (!opts.noBaseline) {
    if (opts.baseline) baselinePath = path.resolve(opts.baseline);
    else if (existsSync(opts.out)) baselinePath = opts.out;
  }
  const hashChanges = baselinePath ? compareBaseline(baselinePath, fileEntries, problems) : 0;
  sortProblems(problems);

  const present = sources.filter((s) => s.present).length;
  const report = {
    schemaVersion: SCHEMA_VERSION,
    generatedAt: new Date().toISOString(),
    tool: 'scripts/content-inventory.mjs',
    root: toPosix(opts.root),
    baseline: baselinePath ? toPosix(baselinePath) : null,
    probe: { status: probe.status, tool: probe.tool, version: probe.version ?? null },
    summary: {
      sourcesConfigured: sources.length,
      sourcesPresent: present,
      sourcesAbsent: sources.length - present,
      files: fileEntries.length,
      bytes: fileEntries.reduce((n, f) => n + f.bytes, 0),
      errors: problems.filter((p) => p.severity === 'error').length,
      ambiguities: problems.filter((p) => p.severity === 'ambiguity').length,
      hashChanges,
    },
    sources,
    files: fileEntries,
    problems,
  };

  mkdirSync(path.dirname(opts.out), { recursive: true });
  writeFileSync(opts.out, `${JSON.stringify(report, null, 2)}\n`, 'utf8');

  // Human summary. Paths, hashes and counts only — never a byte of content.
  const s = report.summary;
  console.log(
    `content-inventory: ${s.sourcesConfigured} source(s) configured, ` +
      `${s.sourcesPresent} present, ${s.sourcesAbsent} absent`,
  );
  console.log(`  probe: ${probe.status}${probe.tool ? ` (${probe.tool})` : ''}`);
  console.log(`  baseline: ${report.baseline ?? 'none'}`);
  for (const src of sources) {
    if (src.present) console.log(`  PRESENT  ${src.sourceId}  ${src.fileCount} file(s)`);
    else console.log(`  ABSENT   ${src.sourceId}  expected at ${src.expectedPath}`);
  }
  if (problems.length > 0) {
    console.log('');
    for (const p of problems) {
      const where = [p.sourceId, p.module, p.test && `test ${p.test}`].filter(Boolean).join(' / ');
      console.log(`  ${p.severity.toUpperCase()} ${p.code}  ${where}`);
      for (const one of p.paths) console.log(`      ${one}`);
    }
  }
  console.log('');
  console.log(`Wrote ${toPosix(opts.out)}`);

  if (s.sourcesPresent === 0) {
    console.log(
      'Nothing to inventory — every configured source directory is absent. ' +
        'This is NOT a clean result: the content is gitignored, so a clean checkout and CI ' +
        'both land here. Nothing was checked.',
    );
    return 2;
  }
  if (problems.length > 0) {
    console.log(
      `Inventory complete — ${s.files} file(s), ${s.errors} error(s), ` +
        `${s.ambiguities} ambiguity(ies), ${s.sourcesAbsent} absent source(s).`,
    );
    return 1;
  }
  console.log(`Inventory complete — ${s.files} file(s), no problems.`);
  return 0;
}

process.exit(await main());
