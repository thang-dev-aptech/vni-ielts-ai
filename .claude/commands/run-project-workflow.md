---
description: Execute a user-authored plan with a dynamic Claude Code agent team
argument-hint: "[plan path or inline plan]"
---

Run the dynamic project workflow using `.claude/agents/workflow-orchestrator.md` and
`.claude/skills/project-workflow/SKILL.md`.

The user-authored plan is the source of scope. Inspect the repository, create or resume
`_workspace/workflow/task-board.json`, dynamically select the minimum specialist agents, and execute
independent tasks in parallel. Require review, targeted tests, negative proof where relevant, and durable
handoff reports before marking work done. Keep shared contracts and final checklist/report writes under
orchestrator ownership. Continue until the plan is complete or a blocker requiring user input is recorded.
