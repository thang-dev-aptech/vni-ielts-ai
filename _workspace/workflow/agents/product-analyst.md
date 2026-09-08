# Product Analyst Demo Acceptance Pack — 3-Skill Boss Demo

Scope: tomorrow's demo proves Reading + Listening + Writing only. Speaking scoring is explicitly out; recording/playback may be shown only as an already-built supporting capability (`P-01`, `P-02`). Do not demo token blocking, payments, AI Chat, native apps, or learner-production release of borrowed exams (`P-14`, `P-17`, `P-21`).

## 1. Demo Success Criteria

Boss-executable checklist, target under 45 minutes:

- Sign in as a learner, open `/practice`, and confirm Practice and Mock are visible as separate choices (`P-04`, `P-05`, `E-20`).
- Practice Reading: choose Reading single-skill practice, start with the open/untimed practice path, answer at least one text and one choice/fill item, submit, and land on `/practice/results/{sessionId}`. Pass if raw score/accuracy appear, correct answers appear after submission, band appears only when `bandVerified=true`, and the left side shows the passage/questions used (`A-11`, `P-06`-`P-11`).
- Practice Listening: choose Listening single-skill practice, start with the open/untimed practice path, play audio, answer at least one audio-linked item, submit, and review the result. Pass if audio loads, submitted answer vs correct answer is visible after submit, wrong/blank/right state is clear, and any band is gated by verified provenance (`A-11`, `P-10`, `P-11`, `E-26`-`E-30`).
- Practice Writing: choose Writing single-skill practice, write Task 1 and Task 2 responses, submit, wait for the Worker-backed AI marking path, refresh results if needed. Pass if Writing shows four criteria, evidence/feedback, combined Writing band using Task 1 : Task 2 = 1 : 2, and every Writing band carries `AI · tham khảo` (`A-13b`-`A-13d`, `P-09`, `P-12`, `P-13`).
- Mock R/L/W: start the full/mock path from `/practice?mode=full`, complete Reading -> Listening -> Writing in one session sequence, using Next between skills and Submit at the end. Pass if the session stays one sitting, advances in the exam `moduleSequence`, and final results show per-skill outcomes without inventing a three-skill overall band (`E-12`, `P-01`, `P-04`, `G-11`).
- Admin/content gate quick check: operator confirms the demo exam is Published only if rights allow it; borrowed `Exam1`/`Vol9Test1` are at most `InternalReview`, never `LearnerProduction` without `RightsProof` (`P-20`, `P-21`, `M-53`).

## 2. Demo Buckets

Must-work:

- Learner login/session persistence long enough to finish the demo.
- `/practice` catalogue loads visible R/L/W items and a full/mock item.
- Starting sessions, autosave/submit, full-session Next, and result navigation work for R/L/W (`E-4`-`E-8`, `E-12`, `E-13`).
- Reading/Listening deterministic scoring from answer keys; AI explanation must not affect band (`A-11`).
- Writing AI marking returns validated criteria/evidence and advisory labeling (`A-6`-`A-8`, `A-13c`, `P-13`).
- Listening audio asset URLs resolve from the published/internal-review package.
- Worker is running so Writing jobs leave pending state.

Nice-to-have:

- Speaking recording/playback as a brief "already stored, not scored" sidebar demo (`P-02`).
- Usage ledger view showing records and the 10-turn grant, with no blocking at zero (`P-14`, `P-15`).
- Admin import/review screens if T14 is actually complete; otherwise describe as post-demo evidence, not live demo material (`P-18`-`P-20`).
- Documents/Articles real API content, if the boss asks about non-exam modules (`P-22`).

Explicitly out of demo:

- Speaking AI scoring, transcript, pronunciation score, or overall four-skill result (`P-02`, `V-10`).
- AI Chat (`F-2` needs re-confirmation after the MVP blueprint).
- Token deduction, payment, invoices, refunds, subscriptions, or "out of turns" blocking (`P-14`, `P-17`, `T-4`).
- Native Android/iOS/Capacitor store build (`P-03`).
- RAR/folder/loose-file import and raw-document AI parse over HTTP; hosted HTTP import accepts ready `exam.json` packages only via the configured seam (`P-18`, `B-7`).
- Any claim that borrowed Cambridge-like content is production-licensed (`P-21`, `M-53`).

## 3. Data & Environment Prerequisites

Operator must supply before the demo:

- Learner account with verified or at least stable login; admin/operator account with permissions to inspect exams/roles/import if shown (`AU-1`, `AU-4`, `C-13`, `P-20`).
- One demo-ready R/L/W exam package with passages, answer keys, Writing tasks, and Listening audio. If using `Exam1` or `Vol9Test1`, set demo visibility to internal review only and disclose rights status (`P-18`, `P-21`).
- Exam version has `moduleSequence` containing Reading, Listening, Writing for the mock path; no dependence on Speaking scoring (`P-01`, `P-02`).
- Reading/Listening scoring profile includes `bandTableProvenance`; expect band only for verified/equated tables (`P-11`, `H-4`).
- Writing rubric/config present, with Task 1 : Task 2 weighting 1 : 2, and the AI key/base URL already configured by environment, not committed secrets (`P-12`, `P-13`).
- AI route for demo explicitly verified by operator: reseller `apithat.dev`, model `gpt-5.5`, `SyntheticDataOnly=false`, `AllowCrossBorderTransfer=true`. This must be disclosed as the accepted `B-2` compliance risk; do not show or write credentials.
- MongoDB available, MinIO/R2/S3-compatible object storage available for audio/assets, API process running, and Worker process running for Writing marking and async jobs.
- If recording is shown, retention/sweep configuration should be set or called out as still pending (`P-02`, `M-2`, T17).
- Published/internal-review state prepared so learner catalogue visibility is predictable; do not change content rights during the live demo (`P-20`, `P-21`).

## 4. Product Risks to Disclose

- Writing is AI-marked and must be labeled advisory: `AI · tham khảo`; it is not official IELTS marking (`P-13`, `A-17`).
- Reading/Listening bands appear only when the exam version's conversion table is verified; otherwise raw score/accuracy are the honest result (`P-11`, `H-4`).
- Three-skill mock has no settled official-like overall band. Until the owner chooses a policy, the API/UI must not invent one (`P-01`, `G-11`).
- Real learner Writing through a reseller is a PDPL cross-border transfer risk accepted by the owner; CTIA/DPA/reseller backend verification remain disclosure items (`B-2`, `V-12`).
- Borrowed `Exam1`/`Vol9Test1` are demo/internal content only; learner-production publish requires VNI ownership or `RightsProof` (`P-21`, `M-53`).
- AI explanations for R/L, if present, are support text only; they never change deterministic scoring (`A-11`).

## 5. Gap Hypotheses To Verify Against Code

These are checks, not requirements:

- Catalogue visibility: confirm whether `InternalReview` items are visible to the demo learner or only `Published`; the answer decides whether borrowed content can be used live without violating `P-21`.
- Listening audio URL resolution: confirm asset refs become playable signed/public URLs in both runner and result review.
- Writing async path: confirm submit creates a marking job, Worker picks it up, and results move from pending to criteria/band without manual DB edits.
- Result left column: confirm post-submit content appears for Reading/Listening/Writing and remains absent before submit (`P-08`, S2).
- JWT/session stability: confirm empty or wrong signing key is not causing logout/session loss during the demo.
- OpenAPI/client drift: T15 is pending; confirm the web client and backend contract are aligned before demo.
- Admin T14 status: confirm admin review/import UI is complete before placing it in the live script.
- Recording retention: T17 is pending; confirm config before mentioning retention as implemented.

## 6. Acceptance Test Script

1. Given API, web, Mongo, object storage, and Worker are running, and a learner account exists; when the learner signs in and opens `/practice`; then R/L/W practice choices and full/mock choice are visible, with no dead "coming soon" path (`P-04`, `P-05`, `S8`).
2. Given a Reading item is visible; when the learner starts single-skill open practice, answers at least two questions, and submits; then results show raw score/accuracy, submitted vs correct answer, the Reading passage/questions on the left, and no unverified band (`A-11`, `P-06`-`P-11`).
3. Given a Listening item with audio is visible; when the learner starts single-skill open practice, plays audio, answers at least two questions, and submits; then audio is playable, review shows submitted vs correct answers, and scoring is deterministic from the answer key (`A-11`, `E-26`-`E-30`).
4. Given a Writing item is visible and AI config is enabled; when the learner submits Task 1 and Task 2; then the first result state may be pending, and after Worker completion the result shows four criteria, evidence, feedback, combined Writing band, and `AI · tham khảo` (`A-13b`-`A-13d`, `P-09`, `P-12`, `P-13`).
5. Given a full/mock R/L/W exam is visible; when the learner starts `mode=full`, completes Reading, clicks Next to Listening, completes Listening, clicks Next to Writing, and submits Writing; then the same session reaches final results with per-skill outcomes and no invented three-skill overall band (`E-12`, `P-01`, `G-11`).
6. Given the demo exam uses borrowed content; when the operator opens admin/content metadata; then rights status is disclosed as internal/demo only and not `LearnerProduction` without proof (`P-21`, `M-53`).
7. Given the AI reseller path is enabled; when Writing marking is shown; then the presenter says the route is a configured demo/compliance risk, not a resolved compliance state (`B-2`, `V-12`).
8. Given any step fails; when the presenter continues; then it is classified as environment/config, content-rights/catalogue, asset-resolution, Worker/AI, or contract-drift, and not reframed as a product requirement.
