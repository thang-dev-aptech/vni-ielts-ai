---
name: project-workflow
description: Dynamic multi-agent implementation workflow for any user-authored project plan. Use whenever the user asks to execute, resume, update, split, coordinate, review, test, or complete a feature plan; dynamically assign agents instead of fixed dev1/dev2/dev3 ownership.
---

# Dynamic plan execution

This is the default workflow after the infrastructure foundation work. The user supplies the plan; the
orchestrator supplies the task graph and agent coordination.

## Start/resume protocol

1. Read `CLAUDE.md`, the user's plan, and all relevant canonical docs.
2. Read `_workspace/workflow/task-board.json` and agent reports if present.
3. Choose one mode:
   - no board: create a new run;
   - board + resume request: continue unfinished tasks;
   - board + materially new plan: copy the old board to `_workspace/workflow/archive/<timestamp>/` first.
4. State the proposed task graph and wait only when a missing business decision changes scope; otherwise
   begin independent tasks immediately.

## Dynamic team composition

Select from `.claude/agents/` per task. Typical mapping (not mandatory):

| Need | Specialist |
|---|---|
| Domain/rules/package format | `domain-analyst` |
| API/persistence | `backend-engineer` |
| Web/CMS | `frontend-engineer` |
| AI contracts/evaluation | `ai-evaluation-engineer` |
| Native audio/offline | `mobile-engineer` |
| Tests/E2E | `qa-engineer` |
| Threat model/security | `security-engineer` |
| Runtime/CI/deploy | `devops-engineer` |

Do not start every specialist by default. Add an agent only when it owns a distinct output or catches a
failure mode another active agent cannot.

## Parallelism and communication

- Create a team and task graph with explicit dependencies.
- Fan out independent tasks; use `SendMessage` for decisions and `_workspace/workflow/` for durable,
  structured artifacts.
- Reserve shared API schemas, migrations, generated clients, master checklist, and final report for a
  single owner at a time.
- QA runs incrementally after each module and again at integration; security reviews boundary changes.

## Task board contract

Each task records `id`, `title`, `owner`, `status`, `dependsOn`, `startedAt`, `lastHeartbeatAt`,
`completedAt`, `files`, `tests`, `negativeProof`, `artifacts`, `blocker`, and `nextDependency`.

## Completion gate

The orchestrator may mark a task or plan complete only after code review, targeted tests, integration tests,
generated-artifact checks, and documented risks. A blocked task remains visible with exact evidence and the
input required from the user.

## Status command

When the user asks for status, summarize the board as: counts by status, active agents + heartbeat age,
completed artifacts/tests, blockers requiring user input, and the next runnable tasks.

## Test scenarios

- Normal: a plan with independent backend and frontend tasks creates two workers, runs them in parallel,
  then gates integration through QA and updates the board to `done`.
- Error: a worker fails a targeted test; the orchestrator sets `review`/`blocked`, records the command and
  error, assigns a fix, and refuses to mark the task done until the rerun passes.
