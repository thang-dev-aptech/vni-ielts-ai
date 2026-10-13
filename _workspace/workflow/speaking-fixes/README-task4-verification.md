==========================================================
TASK 4 - SPEAKING VERIFICATION + CRASH-WINDOW SWEEP
(readme for Hisha)
Done 27/09/2026 (overnight run) - commit c3f63ae, pushed
==========================================================

THIS TASK FIXED 5 AUDIT BUGS AT ONCE
------------------------------------

BUG C3 - "the checksum check was fake"
  WHAT WAS WRONG:
    When a learner uploads a recording, the system checks the
    file with a "checksum" (like a fingerprint). But that
    fingerprint was written by the LEARNER'S BROWSER itself,
    and the check just compared the browser's claim against
    the browser's claim. A corrupted or swapped file passed.
  THE FIX:
    The server now OPENS the stored file, computes the
    fingerprint ITSELF from the actual bytes, and compares.
    A lying file is caught and deleted immediately.

BUG C7 - "any file could pretend to be audio"
  WHAT WAS WRONG:
    The server only checked that the upload SAID "audio/...".
    A 12MB program file renamed to .webm was stored and marked
    as a valid spoken answer.
  THE FIX:
    The server reads the first bytes of every upload (both
    routes) and checks the "magic bytes" - the file's real
    binary signature, like we did for the media library.
    No signature = refused, nothing stored.

BUG C10 - "crashed uploads were lost forever"
  WHAT WAS WRONG:
    If the server crashed between "verify the recording" and
    "save it to the answer sheet", the recording was stuck in
    the middle state forever. No cleanup could see it, and
    the learner's audio just sat there unused.
  THE FIX:
    The cleanup robot now sees those middle-state rows and
    either:
      - finishes the job (if the answer was actually saved),
        or
      - deletes the bytes + row (if nothing saved them).

BUG E - "two things could grab the same upload at once"
  WHAT WAS WRONG:
    The cleanup robot and the "save" action could both touch
    the same upload row at the same time - last writer wins,
    which could file an upload the cleanup had already refused.
  THE FIX:
    Every status change now only works IF the row is still in
    the expected state (like a seat-belt check). If somebody
    else changed it first, the save cleanly fails with 404 and
    deletes the orphan bytes.

BUG C4 - "refused uploads left garbage behind"
  WHAT WAS WRONG:
    When an upload was refused (bad checksum, not audio, etc.)
    the server said no but the bytes sometimes stayed in
    storage.
  THE FIX:
    Every refusal now deletes the bytes right away, in every
    path. Nothing refused is ever left behind.

ALSO DONE
---------
  * New endpoint error code: RECORDING_NOT_AUDIO (clients can
    branch on it - added to the stable codes file)
  * The blob-storage port gained a "read the bytes back"
    method - which is EXACTLY the hook the AI speech-to-text
    (ASR) adapter will need later. So Task 4 quietly advanced
    the Speaking-marking prep too.
  * All test fixtures upgraded to real audio bytes - because
    the new sniff correctly REFUSED the old fake zeros. That
    refusal is the fix working as designed.

TEST EVIDENCE
-------------
  Written red-first where behavior changed:
  - swapped-bytes test (was: passed; now: caught + deleted)
  - non-audio test (was: stored; now: refused + deleted)
  - crashed-upload tests (was: invisible forever; now:
    recovered or removed)
  - racer test (was: could file a refused upload; now: 404)

  Final counts, everything green:
    Domain        250 passed
    Application   559 passed
    Infrastructure 405 passed (real MongoDB + real MinIO)
    Architecture   10 passed
    Worker         25 passed
    Integration   262 passed (real Kestrel HTTP transport)
    ---------------------------------
    TOTAL        ~1,511 tests, 0 failures

BUG SCOREBOARD (backend items from the audit)
---------------------------------------------
  FIXED:  A1, A2, A2b (task 2)  |  C1, C2 (task 3)
          C3, C7, C10, E-races, C4 (task 4)
  = 10 of the audit's backend items done.
  STILL OPEN:
    C6  (GridFS playback - only matters if GridFS ever ships;
         production is S3. Recommendation: document, don't build)
    C5  (purge wiring - WAITING ON OWNER DECISION, asked already)
    D/W9 (Speaking AI marking - WAITING ON OWNER DECISIONS:
         P-02, ASR choice, PDPL filing)
  FRONTEND PERSON: A3-A7, B1-B3, B5-B6 (not ours)

==========================================================
MESSAGE TO SEND HIM (copy-paste)
==========================================================

Task 4 done - c3f63ae on main. This closes out everything
fixable in the backend audit: C3 + C7 + C10 + E-races + C4,
10 of the audit items now fixed total (A1/A2/A2b/C1/C2 before).

C3 - the checksum is computed by the server now: complete opens
the object and hashes the bytes themselves, instead of comparing
the client's metadata claim against the client's init claim. A
swapped payload whose metadata header still carries the declared
value is caught and its bytes deleted immediately.

C7 - both upload routes sniff the head (magic bytes, same rule
as the media library): a 12MB non-audio payload declaring
audio/webm is refused with RECORDING_NOT_AUDIO before a byte is
stored. The legacy multipart path sniffs its stream too, and the
new code is mapped to a proper 400, not a 500.

C10+E - the crash window is swept: the reconciliation now
enumerates from the metadata store (Linked AND Uploaded rows,
cursor in the query). An Uploaded row the sheet names is
recovered (link finished); one nothing names is removed (bytes
via the blob store, row abandoned). Every status write in the
Mongo store is conditional on the current status, so a
stale-abort racer can no longer flip a row under a complete -
the loser sees MatchedCount 0, deletes the unfiled bytes, and
answers 404.

C4 - every refusal path deletes the take's bytes at once, so a
refused upload leaves nothing behind for the sweep to find later.

Also: ISpeakingRecordingBlobStore gained OpenAsync - the read
the server-side hash flows through, and incidentally the exact
seam the ASR adapter will need for W9.

Evidence: the new tests were red against the old code (swapped
bytes passed, non-audio stored, crash rows invisible, racer
could file a refused upload) and green after. Full backend:
~1,511 tests, 0 failures, including real Kestrel transport and
real MinIO round trips.

Remaining backend audit items are all decision-blocked now:
C5 (purge wiring - needs the owner's deletion decision),
C6 (GridFS playback - recommend documenting instead of building,
production is S3), D/W9 (Speaking marking - needs P-02 + ASR +
PDPL). The frontend items (A3-A7, B1-B3, B5-B6) are with the
frontend person.

Next from me: the De IELTS.zip content import through the CLI
importer as soon as you send me the note on what's in it, and
the deadline estimate for the web + CMS.

==========================================================

DEADLINE DRAFT (if he still needs it)
-------------------------------------
  "Backend code: audit-closed as of today. What remains is
  content import (2-4 days once I get the zip note), the owner
  decisions (pricing, purge, ASR), and the UI pass whenever the
  owner describes the interface. Realistic launch-ready for web
  + CMS: 2-3 weeks. Mobile after that: 1-2 weeks (Capacitor
  wrap - the logic already lives on the web)."
