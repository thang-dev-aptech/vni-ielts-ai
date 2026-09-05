#!/usr/bin/env node
//
// F3.3 — grandfather-father-son retention over PBM snapshots.
//
// PBM has `cleanup --older-than`, which is a single cut-off: it cannot
// express "keep 7 daily, 5 weekly, 12 monthly", because that policy keeps
// some old backups and drops some newer ones. So the selection is decided
// here and only the deletion is delegated.
//
// Node rather than bash for the same reason `check-docs.mjs` is (F1.3): this
// is date arithmetic across month and ISO-week boundaries, it has to behave
// identically on Windows and Linux, and — since the output of being wrong is
// a deleted backup — it needs unit tests. `selectForDeletion` is pure and is
// tested directly by `pbm-retention.test.mjs`.
//
// <b>Deletes nothing unless asked.</b> Default is a dry run; `--apply` is
// required to actually remove anything.
//
// Usage:
//   node scripts/pbm-retention.mjs                 # report only
//   node scripts/pbm-retention.mjs --apply         # actually delete
//   VNI_PBM_KEEP_DAILY=7 VNI_PBM_KEEP_WEEKLY=5 VNI_PBM_KEEP_MONTHLY=12 ...

import { execFileSync } from 'node:child_process';

export const DEFAULT_POLICY = { daily: 7, weekly: 5, monthly: 12 };

/** UTC everywhere: a retention policy that changes meaning with the operator's timezone is a bug. */
function utc(date) {
  return {
    day: `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}-${String(
      date.getUTCDate(),
    ).padStart(2, '0')}`,
    month: `${date.getUTCFullYear()}-${String(date.getUTCMonth() + 1).padStart(2, '0')}`,
    week: isoWeek(date),
  };
}

/**
 * ISO-8601 week key, e.g. "2026-W35".
 *
 * Written out rather than approximated with `Math.floor(dayOfYear / 7)`,
 * which drifts against the real calendar and would silently move a backup
 * between buckets near a year boundary.
 */
function isoWeek(date) {
  const d = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate()));
  // Thursday decides the year an ISO week belongs to.
  const dayNumber = (d.getUTCDay() + 6) % 7;
  d.setUTCDate(d.getUTCDate() - dayNumber + 3);
  const isoYear = d.getUTCFullYear();
  const firstThursday = new Date(Date.UTC(isoYear, 0, 4));
  const firstDayNumber = (firstThursday.getUTCDay() + 6) % 7;
  firstThursday.setUTCDate(firstThursday.getUTCDate() - firstDayNumber + 3);
  const week = 1 + Math.round((d - firstThursday) / (7 * 24 * 3600 * 1000));
  return `${isoYear}-W${String(week).padStart(2, '0')}`;
}

/**
 * Which snapshots to delete under a grandfather-father-son policy.
 *
 * <b>A snapshot is kept if ANY tier wants it.</b> Deciding tier by tier and
 * deleting whatever the current tier does not want would delete the backup
 * the next tier was relying on — the classic way a GFS implementation
 * quietly destroys its own monthly history.
 *
 * Within each calendar bucket the NEWEST snapshot is the representative, and
 * the most recent N buckets are kept. Anything not chosen by any tier goes.
 *
 * @param {{name: string, status?: string}[]} snapshots — `name` is a PBM
 *   backup name, which is an RFC3339 UTC instant.
 * @param {{daily: number, weekly: number, monthly: number}} policy
 * @returns {{keep: string[], remove: string[], reasons: Record<string,string[]>}}
 */
export function selectForDeletion(snapshots, policy = DEFAULT_POLICY) {
  const parsed = snapshots
    .map((s) => ({ ...s, at: new Date(s.name) }))
    .filter((s) => !Number.isNaN(s.at.getTime()))
    // Newest first, so the first snapshot seen in a bucket is its representative.
    .sort((a, b) => b.at - a.at);

  const keep = new Set();
  const reasons = {};

  const note = (name, why) => {
    keep.add(name);
    (reasons[name] ??= []).push(why);
  };

  for (const tier of ['daily', 'weekly', 'monthly']) {
    const limit = policy[tier] ?? 0;
    if (limit <= 0) continue;

    const field = tier === 'daily' ? 'day' : tier === 'weekly' ? 'week' : 'month';
    const seen = new Map();

    for (const snap of parsed) {
      const bucket = utc(snap.at)[field];
      if (!seen.has(bucket)) seen.set(bucket, snap);
    }

    for (const [bucket, snap] of [...seen.entries()]
      .sort(([a], [b]) => (a < b ? 1 : -1))
      .slice(0, limit)) {
      note(snap.name, `${tier}:${bucket}`);
    }
  }

  return {
    keep: parsed.filter((s) => keep.has(s.name)).map((s) => s.name),
    remove: parsed.filter((s) => !keep.has(s.name)).map((s) => s.name),
    reasons,
  };
}

// ── CLI ────────────────────────────────────────────────────────────────────

/**
 * F3.5 — the same transport contract as `pbm-run.sh`.
 *
 * `direct` when the pbm binary is on PATH (a scheduler pod, or the database
 * host); `docker` otherwise. Retention that could only run from a laptop
 * would mean the bucket grows forever in every environment that matters.
 */
function resolveMode() {
  if (process.env.VNI_PBM_MODE) return process.env.VNI_PBM_MODE;
  try {
    execFileSync(process.platform === 'win32' ? 'where' : 'which', ['pbm'], {
      stdio: 'ignore',
    });
    return 'direct';
  } catch {
    return 'docker';
  }
}

function pbm(args) {
  const container = process.env.VNI_PBM_CONTAINER ?? 'vni-pbm';
  const uri = process.env.VNI_PBM_URI ?? 'mongodb://localhost:27017/?replicaSet=rs0';

  if (resolveMode() === 'direct') {
    return execFileSync('pbm', args, {
      encoding: 'utf8',
      env: { ...process.env, PBM_MONGODB_URI: uri },
    });
  }

  return execFileSync(
    'docker',
    ['exec', '-e', `PBM_MONGODB_URI=${uri}`, container, 'pbm', ...args],
    { encoding: 'utf8' },
  );
}

function main() {
  const apply = process.argv.includes('--apply');

  const policy = {
    daily: Number(process.env.VNI_PBM_KEEP_DAILY ?? DEFAULT_POLICY.daily),
    weekly: Number(process.env.VNI_PBM_KEEP_WEEKLY ?? DEFAULT_POLICY.weekly),
    monthly: Number(process.env.VNI_PBM_KEEP_MONTHLY ?? DEFAULT_POLICY.monthly),
  };

  const listed = JSON.parse(pbm(['list', '-o', 'json']));
  const snapshots = (listed.snapshots ?? []).filter((s) => s.status === 'done');

  const { keep, remove, reasons } = selectForDeletion(snapshots, policy);

  console.log(
    `pbm-retention: policy daily=${policy.daily} weekly=${policy.weekly} monthly=${policy.monthly}`,
  );
  console.log(`pbm-retention: ${snapshots.length} completed snapshot(s)`);
  console.log();

  for (const name of keep) console.log(`  keep    ${name}   (${reasons[name].join(', ')})`);
  for (const name of remove) console.log(`  DELETE  ${name}`);

  if (remove.length === 0) {
    console.log('\npbm-retention: nothing to delete.');
    return;
  }

  if (!apply) {
    console.log(
      `\npbm-retention: dry run — ${remove.length} would be deleted. Pass --apply to do it.`,
    );
    return;
  }

  for (const name of remove) {
    console.log(`pbm-retention: deleting ${name}...`);
    pbm(['delete-backup', '--yes', name]);
  }
  console.log(`pbm-retention: deleted ${remove.length} snapshot(s).`);
}

// Only run the CLI when invoked directly, so the test suite can import the
// pure selector without talking to Docker.
if (
  process.argv[1] &&
  import.meta.url.endsWith(process.argv[1].replace(/\\/g, '/').split('/').pop())
) {
  main();
}
