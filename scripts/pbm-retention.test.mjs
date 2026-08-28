// F3.3 — the retention selector decides which backups get deleted, so it is
// tested directly rather than exercised through Docker.
//
// Run: node --test scripts/pbm-retention.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { selectForDeletion, DEFAULT_POLICY } from './pbm-retention.mjs';

const snaps = (...names) => names.map((name) => ({ name, status: 'done' }));

test('the documented default policy is 7 daily, 5 weekly, 12 monthly', () => {
  assert.deepEqual(DEFAULT_POLICY, { daily: 7, weekly: 5, monthly: 12 });
});

test('nothing is deleted when there is less than the policy keeps', () => {
  const { keep, remove } = selectForDeletion(
    snaps('2026-08-28T02:00:00Z', '2026-08-27T02:00:00Z', '2026-08-26T02:00:00Z'),
  );

  assert.equal(remove.length, 0);
  assert.equal(keep.length, 3);
});

test('only the newest snapshot of a day represents that day', () => {
  // Three backups on one day: the daily tier keeps one. With every other tier
  // also pointing at the same newest one, the two older ones have no keeper.
  const { keep, remove } = selectForDeletion(
    snaps('2026-08-28T18:00:00Z', '2026-08-28T12:00:00Z', '2026-08-28T02:00:00Z'),
    { daily: 7, weekly: 5, monthly: 12 },
  );

  assert.deepEqual(keep, ['2026-08-28T18:00:00Z']);
  assert.equal(remove.length, 2);
});

test('an eighth consecutive day falls out of the daily tier', () => {
  const days = [];
  for (let d = 28; d >= 20; d--) days.push(`2026-08-${String(d).padStart(2, '0')}T02:00:00Z`);

  const { remove } = selectForDeletion(snaps(...days), { daily: 7, weekly: 0, monthly: 0 });

  // 9 days, 7 kept.
  assert.equal(remove.length, 2);
  assert.ok(remove.includes('2026-08-20T02:00:00Z'));
  assert.ok(remove.includes('2026-08-21T02:00:00Z'));
});

test('a backup the daily tier drops is still kept when a weekly tier wants it', () => {
  // <b>The bug this guards against.</b> Deciding tier by tier and deleting
  // what the current tier does not want destroys the older history the next
  // tier exists to preserve. Here the daily tier keeps only 1, but the
  // weekly tier still wants one representative per week.
  const { keep } = selectForDeletion(
    snaps(
      '2026-08-28T02:00:00Z', // W35
      '2026-08-21T02:00:00Z', // W34
      '2026-08-14T02:00:00Z', // W33
    ),
    { daily: 1, weekly: 3, monthly: 0 },
  );

  assert.equal(keep.length, 3, 'every week still has its representative');
});

test('monthly representatives survive long after the daily and weekly windows', () => {
  const { keep, remove } = selectForDeletion(
    snaps(
      '2026-08-28T02:00:00Z',
      '2026-07-15T02:00:00Z',
      '2026-06-15T02:00:00Z',
      '2026-05-15T02:00:00Z',
    ),
    { daily: 1, weekly: 1, monthly: 12 },
  );

  assert.equal(remove.length, 0, 'four distinct months, all within a 12-month policy');
  assert.equal(keep.length, 4);
});

test('a thirteenth month is dropped', () => {
  const months = [];
  for (let m = 1; m <= 13; m++) {
    months.push(`2026-${String(m > 12 ? 12 : m).padStart(2, '0')}-15T02:00:00Z`);
  }
  // 13 distinct months across a year boundary.
  const names = [];
  for (let m = 12; m >= 1; m--) names.push(`2026-${String(m).padStart(2, '0')}-15T02:00:00Z`);
  names.push('2025-12-15T02:00:00Z');

  const { remove } = selectForDeletion(snaps(...names), { daily: 0, weekly: 0, monthly: 12 });

  assert.deepEqual(remove, ['2025-12-15T02:00:00Z']);
});

test('ISO week keys follow the real calendar across a year boundary', () => {
  // 2026-12-28 is a Monday in ISO week 53 of 2026; 2027-01-01 is a Friday in
  // the SAME ISO week. A naive day-of-year/7 would split them, which would
  // silently keep two backups where the policy says one.
  const { keep } = selectForDeletion(snaps('2027-01-01T02:00:00Z', '2026-12-28T02:00:00Z'), {
    daily: 0,
    weekly: 1,
    monthly: 0,
  });

  assert.equal(keep.length, 1, 'both dates are one ISO week, so one representative');
  assert.deepEqual(keep, ['2027-01-01T02:00:00Z'], 'the newest of the week represents it');
});

test('snapshots that never completed are not counted as backups', () => {
  const mixed = [
    { name: '2026-08-28T02:00:00Z', status: 'done' },
    { name: '2026-08-27T02:00:00Z', status: 'error' },
  ];
  // The CLI filters on status before calling in; this asserts the selector
  // itself does not invent a keeper out of whatever it is handed.
  const onlyDone = mixed.filter((s) => s.status === 'done');
  const { keep, remove } = selectForDeletion(onlyDone);

  assert.deepEqual(keep, ['2026-08-28T02:00:00Z']);
  assert.equal(remove.length, 0);
});

test('an unparseable backup name is ignored rather than deleted', () => {
  // Refusing to classify something is not a licence to delete it.
  const { keep, remove } = selectForDeletion(snaps('not-a-timestamp', '2026-08-28T02:00:00Z'));

  assert.ok(!remove.includes('not-a-timestamp'));
  assert.deepEqual(keep, ['2026-08-28T02:00:00Z']);
});
