==========================================================
TASK 3 - SPEAKING FIXES C-GROUP (readme for Hisha)
Done 26/09/2026 - commit 400f126, pushed to main
==========================================================

WHAT THIS TASK FIXED (2 bugs)
-----------------------------

BUG C1 - "invisible recordings"
  WHAT WAS WRONG:
    The Speaking section has TWO upload routes:
      1. The new "presigned" route (files under 5MB)
      2. The old "legacy" route (big files, 5-12MB)
    The old route saved the audio file but wrote NO database row.
    So for those recordings:
      - Playback was impossible (nothing to look up)
      - Purge could not find them (a deleted account's voice
        stayed in storage - a VIETNAM PRIVACY LAW problem)
      - The cleanup robot could not see them
  THE FIX:
    When a legacy upload succeeds, it now writes the same
    database row the new route writes. If the upload is refused,
    no row is written (the bytes were deleted anyway).

BUG C2 - "the stuck cleanup robot"
  WHAT WAS WRONG:
    A background job (Worker) sweeps storage and deletes
    recordings that no exam answer references anymore.
    It reads the OLDEST 200 recordings each pass.
    If those 200 are all still-valid answers (correctly kept),
    the next pass re-reads THE SAME 200 again - forever.
    The real orphan files behind them were never reached.
  THE FIX:
    Added a "cursor" (bookmark). After each pass, the job
    remembers the last item it judged, and the next pass starts
    AFTER it. The Worker now loops through the whole backlog
    in one run until a pass comes back with less than a full
    batch. Now "deleted nothing today" truly means "nothing
    was orphaned", not "we looked at the same 5 rows again".

BUG C5 - INVESTIGATED, CHANGED TO A DECISION (see below)
  The audit said "purge is registered but never called".
  I searched the whole API: there are NO account-delete or
  sitting-delete features AT ALL. Nothing exists to hook into.
  So this is NOT a bug - it is a MISSING FEATURE that needs
  the product owner's decision (see the message below).

HOW I WORKED (same discipline as before)
----------------------------------------
  1. Wrote the failing tests FIRST
  2. Ran them -> confirmed RED (C1: 3 failed. C2: 1 failed)
  3. Wrote the fix
  4. Ran again -> GREEN (42 Speaking tests + full suite)
  5. Full backend: 1,497+ tests, 0 failures
     (real MongoDB + real MinIO included)

FILES CHANGED
-------------
  backend/src/Vni.Ielts.Application/Exams/ExamHandlers.cs
      - legacy upload now writes the metadata row
  backend/src/Vni.Ielts.Application/Exams/SpeakingRecordingUpload.cs
      - new port method ListOlderThanLinkedAsync (cursor + Linked only)
  backend/src/Vni.Ielts.Application/Exams/RecordingReconciliation.cs
      - SweepAsync takes a cursor, report carries NextCursor
  backend/src/Vni.Ielts.Application/Exams/Ports.cs
      - IRecordingStore.ListOlderThanAsync gains the cursor param
  backend/src/Vni.Ielts.Infrastructure/Persistence/Exams/SpeakingRecordingMetadataStore.cs
      - Mongo query: Linked rows, cursor IN the query, stable order
  backend/src/Vni.Ielts.Infrastructure/Persistence/Exams/RecordingStore.cs
      - same for GridFS
  backend/src/Vni.Ielts.Infrastructure/Storage/S3SpeakingRecordingStore.cs
      - passes cursor through, no longer filters after the limit
  backend/src/Vni.Ielts.Worker/ReconciliationWorker.cs
      - drains with the cursor until a short batch
  + 5 test files (new tests + fake updates)

BUG SCOREBOARD (audit groups A-E, backend only)
-----------------------------------------------
  DONE: A1, A2, A2b, C1, C2           (5 fixed)
  DECISION NEEDED: C5 (purge wiring)  (owner must decide)
  NOT A BACKEND BUG: C3, C4, C7       (client-side / S3 config)
  STILL OPEN (backend): C6 (GridFS playback 503), C8 (content-type
    trust), C9/C10 (sweeper rows never removed), E-group small ones,
    D-group (transcript port - that is Task 4)
  FRONTEND PERSON: A3-A7, B1-B3, B5-B6 (all in apps/web)

==========================================================
MESSAGE TO SEND HIM (copy-paste)
==========================================================

Task 3 done - 400f126 on main: C1 + C2 fixed, and C5 needs your
decision.

C1 - the legacy multipart route now files a Linked metadata row
after the sheet accepts. Playback, purge and the sweep see every
stored recording now. A refused legacy upload still files nothing
(its bytes were deleted). Before this, a >5MB recording was a
voice nobody could play back or ever delete - including after an
account deletion.

C2 - the orphan sweep had no cursor: once one full batch of
sheet-referenced rows sat at the front of the queue, every pass
re-read exactly that batch and never reached the orphans behind
it. The port gains a StoredRecording cursor applied in the store
before the limit (S3: Linked rows ordered (createdAt, recordingId)
with a tiebreak so the order is total; GridFS: uploadedAt +
filename). The report carries NextCursor and the Worker drains
with it until a pass comes back short.

Evidence: both written red-first (3 + 1 failing), green after,
full backend green incl. real Mongo + MinIO.

C5 - needs your call, and it is bigger than a bug: there are NO
account-delete or sitting-delete flows anywhere in the API. The
purge handler is built and tested but nothing can ever invoke it.
Under PDPL a learner (or you, on request) must be able to have
their voice removed. Options: (a) an admin endpoint
"purge this sitting's audio" gated on a permission, (b) a
learner self-service "delete my account and voice" flow, (c)
defer with a written decision. Until one exists, C5 cannot be
"fixed" because there is nothing to wire it into. What does the
owner want?

Also flagging: C3 (x-amz-checksum-sha256) and C4 (signed
Content-Length) are S3-config/SDK work that needs a real bucket
to verify against - say the word and I will do them blind with
tests, or they wait for the staging bucket.

Next on my list: Task 4 - the SpeechTranscript port (the G1 prep
from your Speaking plan: ITranscriptSource.ForAsync from
Task<string?> to text + word timings, verbose_json). Then the
De IELTS.zip import as soon as I get your note on what to run.

==========================================================

WHAT TO TELL HIM IF HE ASKS "WHY IS C5 A DECISION?"
  Because deleting an account is a product feature, not a bug
  fix. It decides: WHO can delete (the learner? only an admin?),
  WHAT gets deleted (voice only? all exam history? the login?),
  and whether it is instant or a 30-day grace period. Those are
  business + legal answers (PDPL), so the owner must pick.
