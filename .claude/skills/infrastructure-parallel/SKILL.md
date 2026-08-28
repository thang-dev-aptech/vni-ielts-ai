---
name: infrastructure-parallel
description: Coordinate dev1/dev2/dev3 parallel execution of the Foundation Ready infrastructure queue.
---

# Parallel Foundation execution

Use this skill when running the infrastructure queue with the three Claude agents.

## Agent map

| Agent | Phase ownership | Shared handoff |
|---|---|---|
| `dev1` | F3.4–F3.5 backup/restore | `_workspace/dev1/report.md` |
| `dev2` | F4.1–F4.5 observability/security/supply chain | `_workspace/dev2/report.md` |
| `dev3` | F5.1–F5.5 CI/certification/timing | `_workspace/dev3/report.md`, `_workspace/dev3/infrastructure-timing.json` |

## Coordination protocol

1. The orchestrator alone owns `docs/development/infrastructure-foundation-todolist.md` and
   `docs/development/infrastructure-foundation-report.md`.
2. Agents may work concurrently only inside their ownership boundary. Cross-phase preparation is allowed,
   but a phase is not closed until its predecessor's gate is evidenced.
3. Each agent must leave a handoff report containing files changed, commands, exit codes, test counts,
   negative proof, artifacts, blockers, and next dependency.
4. The orchestrator reviews all handoffs, runs the phase gate, updates the master checklist/report, then
   starts the next gate. Exactly one `Đang thực hiện` item is active.
5. Timing records must distinguish measured filesystem evidence from continuous active work.

## Completion condition

Only the orchestrator may set `Foundation Ready: đã đạt`, and only after the F5 final gate passes on a
reproducible clean-checkout matrix with no unchecked F0–F5 item.
