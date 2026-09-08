# Demo RLW Readiness — Implementation Plan

> **For agentic workers:** Use subagent-driven development per task. Checkbox steps track progress.
> Spec inputs: `_workspace/workflow/agents/product-analyst.md`, `_workspace/workflow/agents/explore-demo-gaps.md`

**Goal:** Make Reading + Listening + Writing reliably demoable tomorrow for the product owner (boss), with Worker AI marking via apithat.dev `gpt-5.5`, without inventing business rules or claiming production rights on borrowed papers.

**Architecture:** Features are largely built (S1–S8). This plan hardens **operations, content assets, honesty of band display (P-11), and live smoke evidence**. No UI redesign. Speaking scoring stays out (`P-02`).

**Tech Stack:** ASP.NET Core API + Worker, MongoDB rs0 `:27018`, Cloudflare R2 exam assets, React `apps/web`, OpenAI-compatible reseller `https://apithat.dev/v1` model `gpt-5.5`.

**Spec:** BA pack + gap audit under `_workspace/workflow/agents/`.

## Global Constraints

- Never invent `bandTableProvenance: equated` on fixtures that are not actually equated (`P-11`, `G-11`).
- Never grant `LearnerProduction` to Exam1/Vol9 without RightsProof (`P-21`).
- Never commit `secrets.develop.json` or paste API keys into chat/docs.
- Reseller BaseUrl must remain `https://apithat.dev/v1` (not empty = not official OpenAI).
- Model id: `gpt-5.5`.
- Frame demo as **Development / internal fixture content**, not production launch.

---

### Task D1: Plan + operator runbook

**Files:**
- Create: `_workspace/workflow/plans/2026-09-07-demo-rlw-readiness.md` (this file)
- Create: `_workspace/workflow/runbooks/demo-rlw-operator.md`

- [x] Capture BA criteria + gap register into runnable start order
- [ ] Operator runbook: processes, papers to use, disclosures script

### Task D2: Ops harden (JWT + Worker)

**Files:**
- Modify: `backend/src/Vni.Ielts.Api/secrets.develop.json` (Jwt SigningKey only — local, never commit)
- Verify: `backend/src/Vni.Ielts.Worker/appsettings.Development.json` Mongo `27018`
- Verify: Worker `AddVniSecretsFile()` loads Api secrets

- [ ] Generate stable 32+ char `Jwt:SigningKey` in develop secrets
- [ ] Prove Worker boots against same Mongo + Ai section
- [ ] Document start commands in runbook

### Task D3: Listening assets on R2

**Files / tools:**
- Run: `dotnet run --project backend/tools/Vni.Ielts.AssetSync -- push`
- Prefer papers whose audio exists remotely (Cambridge 16–19 / VOL9 2/4/6) OR push local `exam-1-*` / `vol9-test-1-*`

- [ ] Dry-run push; identify missing keys
- [ ] Push local listening media for chosen demo paper
- [ ] Smoke: GET `/api/v1/exams/assets/...` returns 200/206 for first Listening part

### Task D4: Band display honesty (P-11)

**Do not** rewrite fixture provenance to equated.

- [ ] Confirm fixture `scoringProfile` provenance (missing/synthetic/provisional)
- [ ] Ensure results UI copy is clear when `bandVerified=false` (raw score still shown)
- [ ] Add/adjust focused test only if copy/behavior is wrong

### Task D5: Writing live smoke

- [ ] API + Worker running with Ai OpenAi gpt-5.5 / apithat
- [ ] Submit Writing Task1+Task2 on a seeded paper
- [ ] Evidence: marking completes; combined band 1:2; advisory label visible
- [ ] Record latency / failure modes in agent report

### Task D6: QA acceptance

Execute BA script sections 1–5 (Practice R/L/W + Mock R→L→W). Capture pass/fail with screenshots or API traces in `_workspace/workflow/agents/qa-engineer.md`.

### Task D7: Admin T14

Skip for learner demo unless boss must see CMS. Document status.

### Task D8: Final report

`_workspace/workflow/final-report.md` + morning checklist of what user must start.
