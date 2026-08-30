# Four Skills Functional Core — final report

**Certification:** Functional Core Ready — Speaking AI deferred  
**Date:** 2026-08-29  
**Run:** `fscore-20260829`  
**Plan:** `docs/development/four-skills-functional-core-todolist.md`  
**Evidence:** `docs/development/four-skills-functional-core-report.md`

## Verdict

All master checklist phases **FS0–FS9** are closed with phase-gate evidence. Speaking AI remains intentionally deferred (`AwaitingVoiceProvider`; overall band stays null). Live OpenAI/Gemini and R2 smokes are **conditional pending** owner credentials — adapters and recorded contracts are complete.

## Phase gates (summary)

| Phase | Gate | Key evidence |
|---|---|---|
| FS0–FS3 | Closed | Package v2, import, PracticeUnit, provenance |
| FS4 | Closed | Playwright practice/fault/leak **22/22** (desktop+mobile) |
| FS5 | Closed | Slot scorer + results + explanation validators |
| FS6 | Closed | Writing recorded marking; live smoke conditional |
| FS7 | Closed | Mock four-skill Playwright **6/6** |
| FS8 | Closed | MinIO **8/8** + App **34/34** + Kestrel **3/3**; R2 live conditional |
| FS9 | Closed | Security/a11y/reliability + FS9.4 matrix + this report |

## Certification matrix (FS9.4 + browser gates)

| Suite | Exit | Count |
|---|---:|---|
| `check-docs` / generated drift / solution build | 0 | pass |
| Domain / Application / Infrastructure / Integration | 0 | 198 / 249 / 164 / 208 |
| `@vni/web` typecheck + vitest | 0 | 298 |
| FS4 Playwright focused | 0 | 22/22 |
| FS7 mock Playwright | 0 | 6/6 |

## Known non-pass (documented, not claimed green)

- GitGuardian R18
- Security-fixture CI
- Full `pnpm e2e` 30/34 late auth `RATE_LIMITED` on non-FS4 specs
- R2 live smoke / Writing live smoke (no owner keys)
- Playwright CORS direct-PUT to object storage
- Native Capacitor speaking plugin (deferred seam)
- Node host `v22.22.2` vs engine `>=24` warning

## Deferred voice backlog

V1–V5 remain in the todolist. A separate **Speaking AI Completion** queue is required before claiming Full Four-Skills AI Ready.

## Git

No commit or push performed by the orchestrator for this certification close.
