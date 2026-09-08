# Final Report — Demo RLW Readiness (`demo-rlw-2026-09-07`)

**Verdict:** Development demo for Reading + Listening + Writing is **ready enough for boss walkthrough tomorrow**, with mandatory disclosures and a short morning smoke. Not Production Ready. Speaking scoring remains out of scope (`P-02`).

## Team run (agents)

| Role | Agent | Outcome |
|---|---|---|
| BA | product-analyst (`e6a79a47-3711-4533-b383-97d56cae354e`) | Acceptance pack + 45‑min script |
| Audit | explore (`2b507931-3a60-404a-ba26-0c82d60598c4`) | Gap register (ops/content, not missing R/L/W flows) |
| DevOps | devops-engineer (`6ede114d-ff89-475c-9575-6e83c04998a4`) | Stable Jwt SigningKey; Worker/API Mongo aligned |
| Backend | backend-engineer (`52b93e11-41a2-4c0c-80f7-236d5500b86f`) | Listening assets already on R2 for Exam1 / VOL9‑1 / Cambridge |
| Frontend | frontend-engineer (`6e84c479-433c-4c75-89f0-44cf0bf6a2bc`) | Honest P-11 band copy when unverified |
| AI eval | ai-evaluation-engineer (`2d188526-230c-4165-964d-2cb3719c3b21`) | **Production fix:** strip quote-wrapping on evidence (gpt-5.5); live test green |
| QA | qa-engineer (`ce8b1802-80fd-4148-8eb5-6262bcb10106`) | Health/catalogue/Listening asset live PASS; Playwright E2E deferred (DLL lock) |

## What changed in code (keep)

1. `CriterionMarking.StripQuoteWrapping` + prompt instruction — real defect with `gpt-5.5` evidence quotes.
2. Learner results copy when `bandVerified=false` — clearer Vietnamese reason; no fake bands.
3. Local `secrets.develop.json`: `BaseUrl=https://apithat.dev/v1`, `Model=gpt-5.5`, stable `Jwt:SigningKey` (never commit).

## Evidence already green

- Live Writing: `VNI_LIVE_AI=1` → `LiveWritingMarkingTests` pass (~48s) against apithat / gpt-5.5
- Domain CriterionMarkingTests 29/29; WritingEvaluationValidatorTests 6/6
- Web focused: exam-flow + practice-four-skills + practice-dead-ends → 69/69
- Backend BandVerification + WritingBandResults → 17/17
- Live API `GET /health/ready` 200; 25 exams; Cambridge Listening asset `200 audio/mpeg`
- Worker marking jobs completed via `chat/completions` 200 (after binding `ASPNETCORE_URLS=http://localhost:5010`)

## Explicitly NOT done / not claimed

- Playwright full browser E2E while live API holds Debug DLLs
- Admin T14 polish (skipped for learner demo — D7)
- LearnerProduction rights / equated band tables on borrowed fixtures (`P-21`, `P-11`)
- Overall band for 3-skill mock (`G-11`)
- Official OpenAI (still reseller)

## Morning checklist — bạn cần làm

1. Bật stack theo `_workspace/workflow/runbooks/demo-rlw-operator.md` (API + **Worker trên :5010** + web).
2. Đăng nhập learner → `/practice` → Listening Cambridge part → **xác nhận audio phát**.
3. Writing Practice: nộp Task 1 + Task 2 → đợi Worker → thấy 4 tiêu chí + band tổng + **AI · tham khảo**.
4. Nói đúng 4 câu disclosure trong runbook (advisory Writing, band R/L chưa equated, fixture nội bộ, PDPL reseller).
5. Không restart API giữa buổi; không commit `secrets.develop.json`.

## Optional bạn cung cấp (không chặn demo nếu đã có)

- Tài khoản learner sạch cho sếp tự gõ (email/password).
- Nếu muốn band R/L hiện số: đề có `bandTableProvenance.status = equated` thật — **không** bịa trên fixture borrowed.
- Node 24 nếu muốn khớp engine repo (test đã chạy được trên Node 22).

## Docs / artifacts

- Plan: `_workspace/workflow/plans/2026-09-07-demo-rlw-readiness.md`
- Runbook: `_workspace/workflow/runbooks/demo-rlw-operator.md`
- Board: `_workspace/workflow/task-board.json`
- Agent reports: `_workspace/workflow/agents/*.md`
