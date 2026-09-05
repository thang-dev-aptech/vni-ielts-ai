# ADR-0015 — Answer sheet closure protocol

- **Status:** Accepted
- **Date:** 2026-08-27
- **Deciders:** Backend engineer, solution architect
- **Related:** [ADR-0007](0007-server-authoritative-exam-timer.md) · [ADR-0011](0011-mongodb-single-node-replica-set.md) · [`../development/infrastructure-gate.md`](../development/infrastructure-gate.md) I1.2–I1.4

## Context

**A sitting and its answer sheets are separate documents in separate collections, and nothing joined them.**

Two guards existed, and each was correct about the thing it guarded:

- A **section transition** — advance, submit, expire — is a compare-and-swap on the *session* document. Only one writer wins, so a section is closed once and marked once.
- An **autosave** is a field-level patch on the *answer sheet* document. Two writers touching different questions are both right, so there is deliberately nothing to refuse.

Neither says anything about the other. So this interleaving lost work and reported success:

1. An autosave loads the sitting, finds its section open, and passes every check the handler makes.
2. A submit wins the transition CAS, closes the section, and marks the sheet at revision *R*.
3. The autosave's patch lands — revision *R+1*.

The learner's save chip reads **Đã lưu**. The result was computed without that answer. Nothing throws, nothing is logged, and the only evidence is a band one mark low.

The same window exists on `advance` — and it is worse there, because the sitting continues and nobody looks at the results until the end — and on the expiry sweep.

**Speaking is a second door into the same room.** A recording is filed by the server through `SetAnswerAsync`, not through a patch, so an upload finishing after its section closed would walk past any guard placed on the patch path alone. A spoken answer can be the learner's only copy.

## Options considered

| Option | For | Against |
|---|---|---|
| **Freeze the sheet, before the transition** | One atomic statement; no transaction; covers every write path; a late write is refused rather than silently dropped | A crash between the freeze and the transition leaves a sheet that refuses writes while the section still looks open |
| A Mongo transaction spanning sheet, session and outbox | One commit; the strongest statement of the invariant | Every autosave joins a transaction it does not need. Multi-document transactions on a replica set carry a real cost per write, and autosave is the hottest write in the product. It also makes the closure rule invisible: the invariant lives in the shape of a transaction rather than in a field anyone can read |
| A `closing` state on the session, drained | Models the intent explicitly | Draining needs to know what is in flight, which nothing tracks. In practice this becomes the freeze, with a second state machine on top |
| Compare the revision at marking time | Cheap | Detects the loss after the fact and can do nothing about it. The answer is already written and the learner already told it was saved |
| Nothing — accept it as rare | No work | It is not rare: an autosave fires every 1.2 s and a submit closes a section the learner is still typing in. It is a routine end-of-section event |

The last row is why this is an ADR rather than a bug fix. The failure is invisible, the interface reports success, and its frequency is proportional to how hard the learner works in the final seconds.

## Decision

**A section's answer sheet is frozen by a single atomic write, and the freeze happens before the transition.**

1. `IAnswerSheetStore.CloseAsync` sets `closedAt` on the sheet in one `findOneAndUpdate` filtered on `closedAt` being absent. It upserts, so a section nobody answered still closes.
2. **Every write filters on `closedAt` being absent** — `PatchAsync` and `SetAnswerAsync` both. A frozen sheet matches nothing, the upsert collides on `_id`, and the collision is resolved by reading the document: closed means refuse, open means a first-write race and retry.
3. `CloseAsync` is **idempotent** and returns what is already frozen. Two tabs on Nộp bài, a submit meeting the expiry sweep, and a retried request all reach it; re-freezing at a later revision would change the content marking has already read.
4. **The freeze precedes the transition CAS** in all three closing paths. Closing afterwards leaves exactly the original window, because the losing patch is already in flight while the CAS runs.
5. **Marking is handed the frozen sheet** rather than re-reading it. No patch can land after the freeze, so a second read would return the same bytes — but by argument, and an invariant resting on an argument is one a later change breaks silently.
6. A refused write is reported as `409 SECTION_NOT_OPEN`. That is what the section closing looks like from inside a write already in flight, and every client already treats the code as terminal.
7. `closedRevision` records the revision at the freeze, so "marking read the frozen content" is checkable rather than argued.

**The invariant, stated as one sentence:** a patch either commits before the freeze, or is refused before the client is told anything landed. There is no third outcome.

## Consequences

**A crash between the freeze and the transition leaves a sheet that refuses writes while the session still shows the section open.** The learner's autosave is refused with `SECTION_NOT_OPEN`, the client drops the patch and does not hold the ending shut, and the next advance or submit completes the transition — the freeze is idempotent, so it re-runs harmlessly. This is a real cost and it is accepted: the alternative ordering trades a benign, recoverable, *loud* failure for a silent one that loses work.

**The freeze does not move the revision.** A freeze changes no answer, and bumping it would tell every open tab it was behind and pull the whole sheet back for a section that has just closed.

**Sheets written before this field exists read as open**, which is the correct reading — they were never frozen, and every sitting they belong to has long since ended.

**This does not close the marking hole.** A process that dies between the transition and the marking still leaves Writing and Speaking closed and unmarked. That is a different failure with a different remedy — a durable outbox — and it is `I3.1`.

**It does not order two writes to the same question.** Two patches for one question still resolve to whichever the database applied last, which is not necessarily the one the learner typed last. That is `I1.5`.

## Verification

| Property | Test |
|---|---|
| A frozen sheet refuses a patch, and refuses it wholly | `A_frozen_sheet_refuses_a_patch` |
| A section nobody answered still refuses a later write | `Closing_a_section_nobody_answered_still_refuses_a_later_write` |
| Closing twice returns the same frozen sheet | `Closing_twice_returns_the_same_frozen_sheet` |
| Speaking's write path is behind the same barrier | `A_frozen_sheet_refuses_a_recording_too` |
| **No third outcome, under a real race** | `An_autosave_racing_a_freeze_either_lands_before_it_or_is_refused` — 25 rounds, patch and freeze released from one gate; an accepted write absent from the frozen sheet fails the test |
| The barrier holds over HTTP, on submit and on advance | `An_autosave_after_a_submit_is_refused_and_the_result_does_not_move` · `An_autosave_for_a_section_left_behind_by_an_advance_is_refused` |

Removing the freeze turns four of these red. The two HTTP tests stay green without it, because the handler's own section check catches the sequential case — they cover the contract, not the race, and the race is covered by the store-level test above.
