# Cambridge batch — status (2026-09-03)

## Goal
Đọc Cambridge 16–21 (có đáp án đầy đủ) → lọc → đẩy lên web để test chấm R/L chuẩn hơn.

## Live config
- Host: `apithat.dev` (allowlisted)
- Writing / default secrets model: **`deepseek-v4-pro`**
- Exam parse Reading: **`deepseek-v4-flash`** override — pro truncated mid-JSON then hung SSE >3h on T3 Reading
- Exam parse Listening: pro (smaller output)

## Cam 17 shelf

| Paper | Package / fixture | Status |
|---|---|---|
| Test 1 R+L | `fixtures/exams/cam17-test-1.json` | seeded |
| Test 2 R+L | `fixtures/exams/cam17-test-2.json` | seeded |
| Test 3 R+L | in progress | Listening→Reading→assemble |
| Test 4 R+L | queued after T3 | same pipeline |

## Fixes this evening
1. Incomplete SSE JSON → `TransientAiParserException` (retry) instead of crash in `Wrap`
2. Linked 18-minute deadline around SSE body (HttpClient.Timeout alone did not cancel hung stream)
3. Importer `MaxAttempts: 3`

## Fixes tonight (cont'd)
4. `scripts/prepare-cambridge-sources.py`: `is_spaced_module_heading` only matched
   letter-spaced headings (`LIST E NI NG`, Cam 17's pdftotext shape). Cam 21's
   headings extract as a plain all-caps word (`LISTENING`) with no internal
   spacing — same shape as the page-footer (`Listening`, Title Case) it was
   built to reject — so the check found zero headings and every module split
   came back empty. Fixed: renamed to `is_module_heading`, now also accepts an
   exact all-caps match against the target name. Re-ran `prepare-cambridge-sources.py
   cam21` — all 4 tests now split into `paper-listening.txt` / `paper-reading.txt`
   (+ `paper-writing.txt`, and `paper-speaking.txt` for tests 2–4; test 1's page
   range 10–30 doesn't reach its Speaking section — not needed, R/L only ships).

## Next after T3–T4 land
1. Restart API/worker so seeder picks up `cam17-test-3.json` / `cam17-test-4.json`
2. Cam 21 — sources are prepped (see above); run T1–T4 Listening+Reading through
   `Vni.Ielts.ExamImporter` same as Cam 17, then generalize
   `scripts/assemble-cambridge-fixture.py` (currently `--book` hardcoded to
   `choices=["cam17"]` and `CAM17_AUDIO`/title text hardcoded) to take Cam 21's
   audio layout before assembling
3. OCR papers for Cam 16/18/19 — **done, 2026-09-03 evening.** Confirmed exact
   paper page ranges per book/test by rendering + tesseract-probing page
   boundaries (Cam 16: T1@11 T2@33 T3@55 T4@77, 22-page cadence; Cam 18:
   T1@12 T2@34 T3@57 T4@80; Cam 19: T1@9 T2@32 T3@54 T4@77, 23-page cadence).
   Added `ocr_pages_single_column` (paper pages are one-column; the existing
   `ocr_pages` crops each page in half for the two-column key pages and would
   cut paper text mid-line). Mode renamed `ocr-keys` → `ocr-full` for these
   three books.
   Also found and fixed: the module heading is white-on-black badge art:
   tesseract reads it fine most of the time but dropped the Reading badge on
   3 of 4 Cam 16 tests, leaving `paper-reading.txt` unwritten even though the
   section was fully present in the OCR text. Added `FALLBACK_HEADING_MARKERS`
   — a secondary, content-based start marker (`READING PASSAGE 1`) used only
   when the badge scan finds nothing for that module. Cam 16 now splits
   listening+reading clean on all 4 tests; Cam 18/19 running now.
   Same badge-drop also hit the Listening badge on Cam 18 T1/T2/T4 (glued
   noise, e.g. `ea LISTENING`) and Cam 19 T3 (dropped outright, no trace).
   Generalized the fix: `is_module_heading`'s plain-caps branch now matches
   the module name as an uppercase run anywhere on the line (bounded against
   other uppercase letters only, so lowercase OCR noise can sit right next
   to it) instead of requiring the whole line to equal it; added a
   `"listening": PART 1 Questions 1` fallback marker alongside the reading
   one. Re-verified by re-splitting every already-OCR'd `paper.txt` (no
   re-OCR needed): **all 20 tests across Cam 16/17/18/19/21 now produce both
   `paper-reading.txt` and `paper-listening.txt`.** Cam 17/21 (pdftotext,
   unaffected by this bug) re-checked clean too — no regression.
   Speaking still doesn't OCR/split on every test (badge or page-range gaps)
   — not chased further, not needed: the assemble step only uses reading +
   listening.
4. Once Cam 16/18/19 paper text is ready: same AI-importer pipeline as
   Cam 17/21. Audio confirmed present for all three — 16 mp3s each (4 tests ×
   4 parts). Cam 16 (`Test N Part M.mp3`) and Cam 19 (`TestN PartM.mp3`) match
   the naming `assemble-cambridge-fixture.py` expects; Cam 18 is `18 sectionN
   partM.mp3` (section ↔ test not yet verified) so the script's audio-glob
   needs a per-book mapping, not just a per-book constant, when it's
   generalized off `choices=["cam17"]`.

## 2026-09-04 — batch runner, and the Reading model changed again

- `scripts/run-cambridge-batch.sh` runs every prepared paper through the importer
  (built once, Release) and assembles each test; `.ok` markers make it resumable.
  Logs: `_workspace/cambridge/batch/<slug>.log`, progress in `batch.out`.
- `scripts/assemble-cambridge-fixture.py` now takes any book: per-book audio
  layout table (`BOOKS`), title from the book number, and `--listening` optional
  for Cam 20/21 (no audio on disk → Reading-only fixture, description says so).
- **`deepseek-v4-flash` also stalls on Reading.** Cam 17 T3 Reading: three
  attempts, each hit the 18-minute stream deadline, `parse refused: stream
  stalled` after 54 minutes (`batch-flash-attempt.out`). Reading is now parsed
  with **`gpt-5.4`** (`READING_MODEL` env, default in the script is still
  flash so the choice is explicit on the command line). Listening stays on the
  configured `deepseek-v4-pro`, which produced T3/T4 Listening fine.
- Cam 17 T3/T4 Listening packages were re-validated offline (`--package`) and
  marked `.ok`: 39 q/40 marks and 38 q/40 marks.

## 2026-09-04 (cont'd) — key page map and OCR facts, read off the rendered pages

- Cam 16: PDF page = printed folio + 1. Test 1 keys 122–123, Test 2 124–125, Test 3 126–127,
  Test 4 128–129 (badges verified on 122/124/126/128 renders). The old 123–124 window was
  Test 1 Reading + Test 2 Listening.
- Cam 18/19: key ranges were right (badges TEST 1–4 in OCR text; Cam 19 p.119 = TEST 1 verified).
- Right column question numbers sit at ~48 % of page width → `ocr_pages` crops at 45 %.
- Cam 21 has no audio → Reading-only fixtures; description says so.
- Shelf now: Cam 17 T1–T4 (R+L), Cam 21 T1–T4 (R), Cam 18 T1 R ok; Cam 16/18/19 batches running.
- VOL 9 Test 2 Reading: gpt-5.4 dropped Q24–26 and Q36–40 (word-bank groups) — same defect class
  as the 2026-09-03 note; not yet resolved, batch continues with T3–T8.

## 2026-09-04 02:15 — shelf
fixtures/exams: exam-1, vol9-test-1, vol9-test-2, cam16-test-1, cam16-test-2, cam17-test-1..4,
cam18-test-1, cam18-test-3, cam21-test-1..4 (Reading-only). Batches still running: cam18 t4,
cam19 t1–4, cam16 t3–4 (t3 Reading blocked: paper OCR lost Q9 and Q23–26 — needs paper re-OCR),
cam18 t2 Listening (model dropped Q20 — draft deleted for re-parse), VOL 9 t3 R (v7 re-parse),
t4 L, t5–t8.
Hand corrections applied to OCR key text, each read from the rendered page: cam16 t1 R Q32 E,
Q37 C; cam16 t2 L Q23 C; cam18 t1 L Q11 C; cam18 t3 R Q3 C, Q31 H, Q33 F, Q34 E; cam18 t3 L
Q21&22 A/E.
- 02:20 — added vol9-test-4 (40/40 both), cam18-test-3 (R 40/40, L 36q/40).
- Blocked, needs a person: **VOL 9 T3 Reading** — `KEY TEST 3-R.docx` prints 39 answers for 40
  questions (Q40 absent from the key document itself; nothing to inject). **Cam 16 T3** — paper OCR
  lost Q9 and Q23–26 (Reading) and the model numbered 13&14 / 23&24 so that the key's "13 B, C" and
  "23 C, E" find no question (Listening); both need a paper re-OCR/re-parse, not a key fix.
- 02:29 — `deepseek-v4-pro` now also hangs on Listening (Cam 16 T4, VOL 9 T5: stream opened,
  nothing for 10 min). Both batch scripts route Listening to `gpt-5.4` too (`LISTENING_MODEL`).
  Shelf +cam18-test-4 (R 38q/40, L 39q/40) and cam16 t4 Reading validated (40/40).
- 02:34 — shelf +cam16-test-4 (R 40/40, L 38q/40); cam19 t1 R and vol9 t5 L validated. Hand fixes:
  cam18 t4 R Q20 E, Q31 I; cam18 t4 L Q17 E, Q21&22 B/D; cam19 t1 R Q35 E. Normaliser now reads
  "22823 IN EITHER ORDER" (& as 8, consecutive pair only) and "31° G". Importer relabels a lone
  section to `--keep-module` (VOL 9 Listening papers always came back as `reading`).
  Batches restarted: cambridge cam19 cam18 · vol9 5 6 7 8.
- Blocked, needs a person: **VOL 9 T5 Reading** — `TEST 5-R.docx` prints headings i–viii only while
  the rubric says "i–ix" and `KEY TEST 5-R.docx` answers Q16 = ix. The source document is missing
  heading ix; nothing to transcribe.
- 02:48 — shelf 22 fixtures. Reading-only for now (audio book, Listening not cleared):
  **cam18-test-2** (two parses both dropped Q17 and Q34; key 25&26 second letter lost in OCR),
  **cam19-test-2** (Part 1 form OCR garbled → model drops Q1–4; also Q18/Q20/Q32). Both need a
  better OCR of the Listening pages, then one more parse. Cam 19 T3/T4 re-running (T4 hit two
  provider 500s). VOL 9 T7 Reading 40/40, T7 Listening running, T8 next.
- 02:54 — shelf 24 fixtures. VOL 9 T7/T8 shipped Reading-only: gpt-5.4 returns a truncated
  Listening for both (T7 9k chars → 10 questions; T8 13k → 2 questions; T6 was 31k and whole).
  Same source shape (bulleted notes with inline "31 ……" gaps) — a third parse is a gamble; needs
  either a source clean-up or a prompt rule for inline-gap note completion. Cam 19 T2 now R+L;
  Cam 19 T3 R 40/40, T3 L / T4 running.
- 03:03 — **Cam 19 T4 not parsed**: since 02:54 the reseller answers every full-paper request with
  500 `get_channel_failed` ("分组 auto 下模型 <model> 的可用渠道不存在") for gpt-5.4, gpt-5.5 and
  gemini-3.1-pro-preview alike, while a 20-token probe on the same models returns 200 — a
  size-dependent routing failure on their side, not the paper. Retry later:
  `scripts/run-cambridge-batch.sh cam19` (T1–T3 are `.ok` and skip). Cam 19 T3 shipped Reading-only.
