// Reusable board updater. Usage: node _workspace/workflow/update-board.mjs '<json patch>'
//
// The patch is { board?: {...}, tasks?: { "<id>": {...}, ... }, addTasks?: [ {...} ] }.
// Task patches are merged field-by-field onto the matching task.
//
// Trailing newline matters: _workspace/ is NOT in .prettierignore (only *.md is),
// so task-board.json is checked by `pnpm format:check`.

import fs from 'node:fs';

const PATH = '_workspace/workflow/task-board.json';
const board = JSON.parse(fs.readFileSync(PATH, 'utf8'));
const patch = JSON.parse(process.argv[2] ?? '{}');

Object.assign(board, patch.board ?? {});

for (const t of patch.addTasks ?? []) {
  if (board.tasks.some((x) => x.id === t.id)) throw new Error(`duplicate task ${t.id}`);
  board.tasks.push({
    status: 'todo',
    dependsOn: [],
    startedAt: null,
    lastHeartbeatAt: null,
    completedAt: null,
    files: [],
    tests: [],
    negativeProof: null,
    artifacts: [],
    blocker: null,
    nextDependency: null,
    ...t,
  });
}

for (const [id, fields] of Object.entries(patch.tasks ?? {})) {
  const task = board.tasks.find((x) => x.id === id);
  if (!task) throw new Error(`unknown task ${id}`);
  Object.assign(task, fields);
}

board.updatedAt = new Date().toISOString();
fs.writeFileSync(PATH, JSON.stringify(board, null, 2) + '\n');

const byStatus = {};
for (const t of board.tasks) byStatus[t.status] = (byStatus[t.status] ?? 0) + 1;
console.log('board updated:', JSON.stringify(byStatus));
