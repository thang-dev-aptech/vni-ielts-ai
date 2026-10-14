# Task 2 — Speaking audit fixes A1 + A2 (+ the refused-take deletion bug)

*Done 2026-09-26. Readme written for the collab developer; forward the message at the bottom as-is.*

## The bugs (from the Speaking backend audit)

| # | Bug | Where it lived |
|---|---|---|
| **A1** | Every take at a question wrote to the **same storage key** (derived from session + question). A presigned PUT whose 15-minute window outlived the section could **replace the audio of an answer already submitted** — the server refused the late complete, but the bytes had already changed | `SpeakingRecordingUpload.cs` — `SpeakingRecordingKey.For` + `InitSpeakingRecording` |
| **A2** | The refused-retake cleanup deleted **by the id the frozen answer also held** — a lost race left the submitted answer pointing at audio that no longer exists | `CompleteSpeakingRecording` catch of `SectionSheetClosedException` |
| **A2b** | The presigned link lasted **15 minutes regardless of the deadline** | `InitSpeakingRecording.UploadTtl` |

## The fix

**One take, one key.** Every presigned upload now files under
`recordings/<question-hash>/<uploadId>` and the answer sheet is pointed at
that take's own id on complete. Consequences:

* A re-take's PUT can never touch another take's bytes — the overwrite path
  is gone structurally, not by another check
* The refused-take cleanup deletes only the refused take's own unfiled bytes —
  exactly the orphan it exists to remove
* Playback resolves **through the answer sheet** (which take the learner
  actually submitted), never "first linked row"

**The presigned window is capped at `min(15 min, section deadline − now)`.**
A link that survives the deadline was a window in which a frozen answer could
still be written to. Open-ended sittings keep the full 15 minutes.

**The legacy single-segment key stays valid** (`recordings/<hash>`), so
anything already stored keeps deleting and playing back. The multipart body
path is untouched in this task — its metadata row is C-group work.

## Files changed

| File | Change |
|---|---|
| `backend/src/Vni.Ielts.Application/Exams/SpeakingRecordingUpload.cs` | `SpeakingRecordingKey.ForObject` + two-shape `IsValidObjectKey`; per-upload key/id in init; TTL cap; playback resolves via `IAnswerSheetStore`; comments |
| `backend/tests/Vni.Ielts.Application.Tests/Exams/SpeakingTakeIsolationTests.cs` | **New.** 7 tests: distinct keys per take, URL capped at deadline, refused retake never deletes the filed take, playback follows the sheet, key-shape bounds |
| `backend/tests/Vni.Ielts.Application.Tests/Exams/SpeakingRecordingRemainderTests.cs` | One assertion updated — it asserted the shared-key behaviour (the bug) |
| `backend/tests/Vni.Ielts.Application.Tests/Exams/SpeakingRecordingPlaybackTests.cs` | New ctor arg (sheet store fake) |
| `backend/tests/Vni.Ielts.Infrastructure.Tests/Storage/SpeakingRecordingUploadTests.cs` | Two real-MinIO tests re-pointed to per-upload keys; the re-record test now asserts **different** keys and that both takes' bytes survive independently |

## Evidence (the red-then-green discipline)

1. Tests written **first** → 3 failed against the old code (shared key, 15-min window, filed take deleted) — red confirmed
2. Implementation landed → all green: Application 548, Infrastructure 405 (real MinIO round trip included), full backend **1,497 tests, 0 failed**
3. Removing the implementation makes the tests uncompilable (they demand the new API) — the property cannot silently regress

## Known consequence (flagged, not solved here)

A superseded take's bytes now linger until purge or a retention rule instead
of being overwritten instantly. Same learner, same sitting, same retention
window — but "how long" now depends on the **retention decision that is still
open** (`M-2` / the deleted `SpeakingRecordingRetentionDays`). If the owner
wants instant supersede-cleanup, the orphan sweeper should grow a
"Linked-but-not-on-sheet" pass — that is C-group work.

## Out of scope here (frontend person's list)

A3–A7, B1–B3, B5–B6 from the audit — all in `apps/web` (`SpeakingRecorder.tsx`,
`PracticeRunnerPage.tsx`, `web.ts`, `ExamResultsPage.tsx`).
