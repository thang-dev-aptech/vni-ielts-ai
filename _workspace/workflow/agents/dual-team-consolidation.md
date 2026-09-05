# Dual-team consolidation — UI QA + bugs/warnings

Date: 2026-08-29  
Run: GPT live wiring + learner UI verification

## Teams

| Team | Agent | Report |
|---|---|---|
| UI QA | UI feature QA (agent `75f4e22d`) | `ui-qa-report.md` |
| UI UX | UI UX review (agent `81b3775f`) | `ui-ux-review.md` |
| Frontend bugs | Frontend bugs (agent `a780bfdc`) | `frontend-bugs-report.md` |
| Backend bugs | Backend bugs (agent `4b764d99`) | `backend-bugs-report.md` |

## What is OK

- Auth, Reading/Listening practice → results (deterministic scores)
- No fabricated Writing/Speaking bands
- Mock overall stays pending when Speaking unscored
- Learner results UI now has R/L explanation request + Writing criteria panels (UX team)
- Web typecheck + 302 vitest green; backend AI filters green (temp build)
- GPT key connectivity PASS on vietapi (earlier probe)

## Blockers / product gaps (priority)

1. **P0 — Test path vs egress:** Personalized explanation + Writing marking require `LearnerPersonal`. vietapi + `SyntheticDataOnly=true` **refuses** those workloads. UI will show pending/failed explanation and Writing Awaiting* unless official OpenAI + PDPL switches are set. Documented in `secrets.TEAM-README.md`.
2. **P1 — Stale API process:** `:5099` may still run pre-wiring build (DLL lock). Restart before re-test.
3. **P1 — Explanation completion not proven** on live UI (QA).
4. **P2 — Full-mock Listening audio load failure** (QA).
5. **P2 — Speaking entry points inconsistent** (QA).
6. **P3 — Mobile unnamed account button**; E2E harness Windows/rubric path issues (QA).
7. **P3 — Node engine** `v22` vs `>=24` warning.

## Already fixed this session

- Post-submit R/L explanation UI + Writing criteria/pending copy
- Frontend test/typecheck hygiene
- Backend: LearnerPersonal classification, `/responses` vs chat for official OpenAI, fallback re-auth
- **QA P2 follow-up** (agent `b7111597`): status-code pending copy; Speaking reason line; pause audio on confirm; Speaking practice → recorder.

## Owner approval (2026-08-30)

**Duyệt** — proceed with current QA fixes and restarted dev stack.

- API restarted on `:5099` (PID 26544) with latest build + user-secrets loaded.
- Web vitest spot-check: **81/81** pass on exam-flow / practice-runner / speaking-contract.
- **Accepted limitation:** vietapi + `SyntheticDataOnly=true` remains smoke/canonical-only until official OpenAI + PDPL switches for Writing + personalized explanations.

## Recommended next actions

1. ~~Restart API with current build + user-secrets.~~ **Done**
2. When ready for real learner AI: official OpenAI (`BaseUrl` empty), `SyntheticDataOnly=false`, `AllowCrossBorderTransfer` per PDPL.
3. Full-mock Listening audio asset — still open (QA).
4. Manual UI pass on explanation + Writing after stack restart.
