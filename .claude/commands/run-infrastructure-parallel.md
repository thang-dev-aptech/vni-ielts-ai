---
description: Launch and coordinate dev1/dev2/dev3 for the Foundation Ready queue
argument-hint: "[resume | status]"
---

Coordinate the three configured Claude agents for the infrastructure queue.

1. Read `CLAUDE.md`, the `infrastructure-parallel` skill, the Foundation checklist and report.
2. Start `dev1`, `dev2`, and `dev3` in separate Claude Code sessions/worktrees or approved parallel
   sessions. Do not give two agents overlapping file ownership.
3. `dev1` handles F3.4–F3.5, `dev2` handles F4.1–F4.5, and `dev3` handles F5.1–F5.5 plus timing.
4. Require each agent to write its `_workspace/<agent>/report.md` and exact test evidence before handoff.
5. Reconcile handoffs sequentially at the phase gates. Only the orchestrator edits the master checklist and
   Foundation report. Keep going through F5; report blockers with evidence instead of overclaiming readiness.
