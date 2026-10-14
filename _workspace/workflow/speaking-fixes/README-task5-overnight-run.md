==========================================================
OVERNIGHT RUN - FULL SYNOPSIS (readme for Hisha)
Done 27/09/2026 morning - 4 commits pushed
==========================================================

WHAT I DID WHILE YOU SLEPT
--------------------------
Task 4 (the "hardening" task) + the Speaking-marking prep his
plan said to start immediately. Everything red-first, everything
green, everything pushed to origin/main.

COMMITS (in order)
------------------
  c3f63ae  fix(speaking): verify stored bytes, sweep the crash
           window, guard every status write
  be15a13  docs(speaking): task 4 readme
  d4caf9d  feat(assessment): SpeechTranscript port (word timings)

WHAT EACH BUG WAS, IN PLAIN WORDS
---------------------------------

C3 - THE FAKE FINGERPRINT
  The old check compared the browser's claim with the browser's
  claim. Now the server opens the file and fingerprints the
  actual bytes itself. A swapped/corrupted upload is caught
  instantly and deleted.

C7 - FILES THAT LIED ABOUT WHAT THEY ARE
  Before: any 12MB file that SAID "audio/webm" was accepted.
  Now: the server reads the file's first bytes (magic bytes -
  every file type has its own binary signature) and refuses
  anything that isn't really audio. Same protection the media
  library already had. New error code: RECORDING_NOT_AUDIO.

C10 - UPLOADS STRANDED BY A CRASH
  If the server died between "verify recording" and "save to
  answer sheet", that recording was stuck in limbo forever -
  no cleanup could see it. Now the cleanup robot sees those
  rows and either finishes the job (answer was saved) or
  removes the bytes + row (nothing was saved).

E - THE RACE CONDITION
  The cleanup robot and the save action could both grab the
  same upload row simultaneously - who wins was luck, and the
  loser could resurrect a refused upload. Now every change
  only works if the row is still in the expected state. The
  loser cleanly reports 404 and deletes the orphan bytes.

C4 - REFUSED = GONE
  Every refusal path now deletes the bytes immediately. A
  refused upload leaves zero garbage.

BONUS - THE ASR HOOK (his "start immediately" item)
---------------------------------------------------
  The transcript interface (ITranscriptSource) used to return
  just text - throwing away the word TIMINGS. The AI fluency
  scoring NEEDS timings (pauses, speed, run-length are all
  math on timings). I changed the port to return
  SpeechTranscript = text + per-word timings + confidence.
  This is vendor-neutral (works with whisper/Azure/anything)
  and it's the exact hook the AI marking will plug into.
  Marking behaviour unchanged - still says "waiting for voice
  provider" since no ASR is chosen yet.

TEST EVIDENCE
-------------
  All new tests were RED first (proving the bugs were real),
  then GREEN after the fix.
  FINAL: ~1,511 backend tests, 0 failures
  (including real MongoDB + real MinIO + real HTTP transport)

AUDIT SCOREBOARD - BACKEND
--------------------------
  FIXED (10): A1, A2, A2b, C1, C2, C3, C4, C7, C10, E-races
  DECISION-BLOCKED (3):
    C5   - purge wiring: needs owner's deletion decision
    C6   - GridFS playback: recommend documenting, not building
           (production is S3)
    D/W9 - Speaking AI marking: needs P-02 + ASR choice + PDPL
  FRONTEND PERSON (9): A3-A7, B1-B3, B5-B6

==========================================================
MESSAGE TO SEND HIM (copy-paste)
==========================================================

Overnight run done - backend audit is now CLOSED for everything
fixable without an owner decision. Commits: c3f63ae (hardening)
+ d4caf9d (your G1 prep).

C3 - the server hash is real now: complete opens the object and
hashes the bytes (OpenAsync on the blob port), instead of
comparing metadata against the declaration. A payload swapped
under a presigned URL is caught and deleted on the spot.

C7 - both routes sniff the head now (magic bytes, the media
library's rule): a non-audio payload declaring audio/webm is
refused with the new RECORDING_NOT_AUDIO code before a byte is
stored. The legacy multipart path sniffs too, and it maps to a
400 instead of a 500.

C10+E - the crash window is swept: reconciliation enumerates
from the metadata store (Linked AND Uploaded), recovers an
Uploaded row the sheet names, removes one nothing names (bytes
through the blob store, row retired), and every Mongo status
write is conditional on the current status - the stale-abort
racer can't flip a row under a complete anymore; the loser
deletes the unfiled bytes and answers 404.

C4 - every refusal path deletes the take's bytes immediately.

And d4caf9d is your W9 start-immediately item: ITranscriptSource
returns SpeechTranscript per recording now - question id, text,
word timings, confidence - instead of Task<string?>. Vendor
neutral, no AI calls, marking behaviour unchanged. The ASR
adapter (when B-1 is picked) maps its verbose_json onto this
once and everything downstream is ready for fluency arithmetic.

Evidence: new tests red-then-green; backend ~1,511 green incl.
real Kestrel + MinIO + Mongo.

What I need from you now (all owner decisions):
1. The De IELTS.zip note - what do you want me to import via the
   CLI importer? (the Cambridge/Vol 9 content can't be published
   under P-21 without a rights position - internal-use only?)
2. C5: account/sitting deletion - admin endpoint, self-service,
   or defer? What gets purged with it?
3. W2/W3 pricing (B-5a/B-5b) whenever ready.
4. ASR pick for B-1 (whisper-1 via reseller needs one real test
   call - waiting on your go).

==========================================================
