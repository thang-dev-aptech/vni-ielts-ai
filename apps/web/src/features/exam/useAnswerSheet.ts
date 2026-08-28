import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '../../lib/api.js';
import { useAlive } from '../../lib/useAlive.js';
import { saveAnswers, type CurrentSectionView } from './examApi.js';
import { acknowledge, forgetSection, remember, restore } from './patchJournal.js';

/**
 * The answer sheet of one open section, and everything that gets it to the
 * server.
 *
 * <b>Extracted from `ExamRunnerPage` on 27/08/2026, unchanged.</b> Every line
 * below fixed a specific data-loss bug and every one of them is covered by a
 * test in `exam-flow.test.tsx` that has been individually verified to go red
 * when its guard is removed. The extraction exists because a second runner —
 * luyện đề — needs the identical machinery, and a copy of this would drift.
 * The copy that drifts is always the one nobody has filed a bug against yet.
 *
 * <b>Nothing here knows what mode the sitting is in.</b> A deadline, a
 * stopwatch, a footer, a submit gate are all the page's business; this owns the
 * sheet, the patch queue and the chip. The one mode-shaped thing it does own is
 * the terminal-refusal set, and that is a list of server codes rather than a
 * policy.
 */

/**
 * <b>`pending` and `queued` are different facts, and were one word.</b>
 *
 * `pending` — typed, inside the debounce window, nothing attempted yet.
 * `queued`  — attempted, the network refused it, it will be retried.
 * `failed`  — the server answered, and the answer was no.
 *
 * The first two used to share the string "Chưa gửi được", so an essay writer
 * saw the alarm state on every keystroke for the length of the paper. A
 * warning shown when nothing is wrong is not a cautious warning; it is a
 * broken one, because it is the same warning shown when something is.
 */
export type SaveState = 'idle' | 'pending' | 'sending' | 'saved' | 'queued' | 'failed';

/**
 * What a flush actually did, so a caller can act on it.
 *
 * <b>`flush` used to swallow every failure and resolve as if nothing
 * happened.</b> The comment above `submit()` said its failure would stop the
 * submit; the code could not, because there was nothing to stop on. A learner
 * who fixed their last answer and pressed "Nộp bài" before the debounce fired
 * had their correction lost to a network blip and the paper marked from the
 * previous snapshot — and every visible signal said the save had worked.
 */
export type FlushResult = 'saved' | 'clean' | 'failed' | 'refused';

/** Autosave delay. Long enough not to fire per keystroke, short enough that a
 *  dropped connection loses seconds of typing rather than minutes. */
export const AUTOSAVE_MS = 1200;

/**
 * How long to wait before offering a refused save again.
 *
 * <b>Exponential, capped, and jittered — and each of the three earns its
 * place.</b>
 *
 * <b>Exponential</b>, because the thing being waited out is usually a network
 * that is down rather than busy, and a fixed one-second retry spends a request
 * a second for the length of a tunnel.
 *
 * <b>Capped</b>, at fifteen seconds, because a section is sixty minutes and an
 * unbounded backoff reaches "try again in nine minutes" while the learner is
 * still sitting there. The cap is the longest a learner should ever be
 * unknowingly unsaved.
 *
 * <b>Jittered</b>, because every device in a classroom loses the same wifi at
 * the same instant and would otherwise come back in step — thirty tabs
 * retrying together, failing together, and doubling together. The spread is
 * what turns a thundering herd back into traffic.
 */
const RETRY_MS = [1_000, 2_000, 4_000, 8_000, 15_000] as const;

function backoffFor(attempt: number): number {
  const base = RETRY_MS[Math.min(attempt, RETRY_MS.length - 1)]!;
  // ±25%. Enough to break lockstep, not enough to make the wait unpredictable.
  return Math.round(base * (0.75 + Math.random() * 0.5));
}

/**
 * Codes the server will never accept this patch under, however many times it
 * is offered.
 *
 * <b>Without this the flush gate traps the learner on a dead page.</b> The
 * gate is right to stop an ending on a save that did not land — that is what
 * it was added for — but it treated "the network dropped one packet" and
 * "this section closed ten seconds ago" as the same refusal. A learner whose
 * deadline passed with one unsaved keystroke got `SESSION_EXPIRED` on every
 * flush, so "Nộp bài" refused, for ever, and `beforeunload` argued with the
 * reload that would have freed them. Two tabs produced the same trap through
 * `SECTION_NOT_OPEN`, with the clock still running.
 *
 * A terminal refusal means the answer is not going to be saved by anybody.
 * Blocking the ending on it protects nothing and costs the learner the rest
 * of the sitting; the ending's own call then meets the same condition and
 * routes it properly — that is what `isOver` is for.
 */
const TERMINAL_REFUSALS = new Set([
  'SESSION_EXPIRED',
  'SESSION_NOT_IN_PROGRESS',
  'SECTION_NOT_OPEN',
]);

/*
 * <b>`VALIDATION_FAILED` is terminal for the questions the server named, and
 * for nothing else.</b>
 *
 * It used to sit in the set above, and the three codes there share a property
 * it does not have: they are facts about the <i>section</i>. The section
 * closed, the sitting ended, the deadline passed — no entry in the patch will
 * ever be accepted, so throwing the patch away costs nothing that was not
 * already lost.
 *
 * A validation failure is a fact about <i>one entry</i>. The server refuses
 * the whole patch, correctly — a partly applied autosave is a sheet nobody can
 * reason about — but a batch is one bad answer and however many good ones. The
 * client, told only "VALIDATION_FAILED", did the only thing a caller with no
 * detail can do:
 *
 *   pendingChanges.current = {};                        // every change, gone
 *   savedGeneration.current = generation;               // the gate opens
 *
 * So a learner who fixed question 3 and typed something the server would not
 * take into question 4 lost <b>both</b>, with a red chip that says a save
 * failed and no way to tell which one, and "Nộp bài" then went through and
 * marked the paper without either.
 *
 * The server now names the questions in `errors`. Those are dropped — they are
 * genuinely unsaveable and keeping them would re-offer the same rejected patch
 * on every flush and hold the ending shut, which is the trap this set was
 * built to avoid. Everything else stays queued, and the generation does not
 * move, so the gate stays shut until the rest lands.
 *
 * <b>A refusal that names nothing still drops the batch.</b> There is nothing
 * to keep and nothing to retry, and the anti-trap property matters more than
 * one more round of guessing.
 */
const VALIDATION_FAILED = 'VALIDATION_FAILED';

/** The section-scoped fields the sheet adopts. A whole `CurrentSectionView`
 *  satisfies it, and so does an `advance` response's `current`. */
export interface SheetSeed {
  answers: Record<string, string | null>;
  answerRevision?: number | undefined;
}

export interface AnswerSheet {
  /** The sheet as this page is showing it. */
  answers: Record<string, string | null>;
  /** What the save chip renders. */
  save: SaveState;
  /**
   * Questions the server refused, and why, keyed by question id.
   *
   * <b>A refusal the learner cannot see is a refusal that costs them the
   * answer.</b> The chip can only say that <i>a</i> save failed; on a forty
   * question paper that is not something anyone can act on. An entry appears
   * here when the server names a question in a `VALIDATION_FAILED`, and it is
   * cleared the moment that question is edited again — because at that point
   * the thing being refused no longer exists.
   */
  refused: Record<string, string>;
  /**
   * Whether there is work the server has not acknowledged.
   *
   * A ref, because a submit handler reads it in the same tick as the keystroke
   * that set it and a render has not happened yet.
   */
  dirty: React.RefObject<boolean>;
  /** An answer the learner composed here. Marks the sheet dirty and debounces. */
  change: (questionId: string, value: string | null) => void;
  /** A recording the server already filed. Local mirror only — see below. */
  recorded: (questionId: string, recordingId: string) => void;
  /** Writes until the newest draft has been acknowledged, one request at a time. */
  flush: () => Promise<FlushResult>;
  /** The same, behind a stable identity, for an effect that must not re-subscribe. */
  flushRef: React.RefObject<() => Promise<FlushResult>>;
  /**
   * Adopt a section's sheet — on load, and again on every `advance`.
   *
   * Every sheet-scoped counter goes back to its opening value here, listed
   * rather than derived: each section has its own sheet, its own revision and
   * its own generations, and carrying any of them across an advance re-sends
   * one section's questions to a section that has never heard of them.
   */
  seed: (section: SheetSeed) => void;
  /** Disarm the debounce without sending. Both endings do this before flushing. */
  cancelPending: () => void;
}

/**
 * The refused questions, by the number the learner sees.
 *
 * A question id is `r-4`; the paper calls it 4. Naming ids in a notice makes
 * the reader translate an internal key back to a position on their own screen,
 * which on a timed paper is a cost with no benefit. The id is the fallback
 * only for a question the open section does not carry — which should not
 * happen and should not be silently blank if it does.
 */
export function refusedNumbers(
  section: CurrentSectionView | null,
  refused: Record<string, string>,
): string {
  const order = new Map(
    (section?.parts ?? []).flatMap((part) =>
      part.questions.map((question) => [question.id, question.order] as const),
    ),
  );

  return Object.keys(refused)
    .map((questionId) => ({ questionId, order: order.get(questionId) }))
    .sort((a, b) => (a.order ?? Number.MAX_SAFE_INTEGER) - (b.order ?? Number.MAX_SAFE_INTEGER))
    .map(({ questionId, order: n }) => (n === undefined ? questionId : String(n)))
    .join(', ');
}

export function useAnswerSheet({
  accessToken,
  sessionId,
  section,
  onAcknowledged,
}: {
  accessToken: string | null;
  sessionId: string;
  /** The open section, or null while the sitting is still loading. */
  section: CurrentSectionView | null;
  /**
   * Called when the newest draft has landed and the chip has gone green.
   *
   * <b>The page's failure notice goes with the chip.</b> `stepFailed` used to
   * be cleared only by pressing the footer button again, so a learner whose
   * final save failed, retyped the answer and watched the chip turn green
   * still had "phần vừa nhập chưa lưu được" in red underneath it — the screen
   * asserting both at once, which is the contradiction the chip's own wording
   * exists to avoid.
   */
  onAcknowledged?: (() => void) | undefined;
}): AnswerSheet {
  const alive = useAlive();

  const [answers, setAnswers] = useState<Record<string, string | null>>({});
  const [save, setSave] = useState<SaveState>('idle');
  const [refused, setRefused] = useState<Record<string, string>>({});

  const dirty = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  /*
   * <b>The sheet version this page believes it is amending.</b>
   *
   * Null until the first save of a section, because nothing has been written
   * and the server treats "no revision" as zero — the same value a first write
   * expects. After that it is whatever the last accepted save returned, or, on
   * a conflict, whatever the server said is actually there.
   *
   * A ref rather than state: it must not re-render anything, and a stale
   * closure over it would send an old number and manufacture the very conflict
   * it exists to detect.
   */
  const revisionRef = useRef<number | null>(null);

  /*
   * ── Draft generations ──────────────────────────────────────────────────
   *
   * <b>`dirty` alone could not tell whose save had been acknowledged.</b>
   *
   * Snapshot A goes out. While it is in flight the learner types B, which sets
   * `dirty = true` and schedules another save. A's response arrives, the
   * handler clears `dirty` — and B, which the server has never seen, is now
   * indistinguishable from saved work. Press submit and the paper is marked
   * without it. The chip said "Đã lưu", and it was telling the truth about a
   * snapshot nobody was looking at any more.
   *
   * A counter fixes the question the boolean could not answer: not *is there
   * unsaved work* but *which draft was acknowledged*. Only the response that
   * carries the newest generation may clear anything.
   */
  const draftGeneration = useRef(0);
  const savedGeneration = useRef(0);

  /**
   * The sheet as this page is showing it — every answer, not only the new ones.
   *
   * A mirror of `answers` held in a ref, because the merge below runs inside an
   * async handler where the state closure is already stale. It is no longer
   * what gets sent; it is what a returned sheet is merged into.
   */
  const latestSheet = useRef<Record<string, string | null>>({});

  /**
   * The questions edited since the last save the server acknowledged.
   *
   * <b>This, and not the sheet, is what an autosave carries.</b> Sending the
   * whole sheet meant sending blanks, and a blank cannot say whether the
   * learner rubbed an answer out or whether this tab has simply never heard of
   * the question — so a copy a few seconds old deleted whatever another tab had
   * typed. Adding a revision made that visible and left it just as fatal: the
   * server refused, this page took the new revision and re-sent the same whole
   * sheet, and the overwrite happened one beat later with a green chip over it.
   *
   * A patch cannot express the erase. What is absent is untouched, and a `null`
   * is here only because someone cleared that answer.
   *
   * Emptied by the response that acknowledged it — but only of the entries that
   * still hold the value that went out. Anything re-typed while the request was
   * in flight stays, because the server has not seen that one.
   */
  const pendingChanges = useRef<Record<string, string | null>>({});

  /*
   * ── Per-question ordering tokens ───────────────────────────────────────
   *
   * <b>The write that arrives last is not the edit that came last.</b>
   *
   * Two writes for one question can be reordered by anything between the
   * keyboard and the database — a retry on a changed network, a proxy, a
   * request that stalled while its successor went straight through, a second
   * tab. Without an order the server keeps whichever it applied last, which is
   * the older answer as often as the newer one: the learner types `dog` over
   * `cat`, the `cat` request lands second, and the correction reverts with a
   * green chip over it.
   *
   * The revision cannot answer this. It is one number for the whole sheet, so
   * it says whether this page was behind — not which of two edits to one
   * question came second.
   *
   * <b>A counter, not a clock.</b> Two tabs on one machine disagree about the
   * time, and a client running behind would have every edit ignored for as long
   * as the skew lasted. This is raised past whatever the server last reported,
   * which orders this tab's own edits absolutely and orders them against
   * another tab's as soon as either has seen the other's — a Lamport clock, and
   * the weakest thing that is actually correct.
   */
  const sequences = useRef<Record<string, number>>({});
  const nextSequence = useRef(0);

  /** Raises the counter past `seen`, so the next token this page issues wins. */
  const observe = useCallback((seen: number) => {
    if (seen >= nextSequence.current) nextSequence.current = seen + 1;
  }, []);

  /** The drain in progress, so a second caller joins it instead of racing it. */
  const draining = useRef<Promise<FlushResult> | null>(null);

  /*
   * The callback behind a ref, so `sendOnce` does not take a new identity every
   * time the page re-renders with a fresh closure. `sendOnce`'s identity is
   * what `flush` depends on, and `flush` is what the debounce closes over.
   */
  const acknowledged = useRef(onAcknowledged);
  acknowledged.current = onAcknowledged;

  /*
   * ── The retry the drain deliberately does not do ───────────────────────
   *
   * <b>A refused save used to wait for the next keystroke.</b> `flush` stops
   * its drain on a failure — correctly, because spinning there would re-offer
   * the same rejected patch as fast as the network could refuse it — and
   * nothing else was scheduled. So a learner whose connection dropped for
   * twenty seconds mid-essay had their work sitting unsent until they happened
   * to type again, and a learner who had *finished* typing had it sitting
   * unsent until they pressed Nộp bài, which is the worst possible moment to
   * discover the connection is gone.
   *
   * This is the other half of the journal: the journal means an unsent answer
   * survives the tab going away, and this means it does not have to wait for
   * the learner to do something before it is offered again.
   *
   * <b>Only for a failure worth retrying.</b> A terminal refusal clears the
   * queue and never reaches here; a validation refusal drops the named
   * questions and keeps the rest, which is worth another attempt. The counter
   * resets on any acknowledged save, so a connection that comes back does not
   * inherit the backoff of the one that went away.
   */
  const retryTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const retryAttempt = useRef(0);

  const scheduleRetry = useCallback(() => {
    if (retryTimer.current !== null) clearTimeout(retryTimer.current);

    const wait = backoffFor(retryAttempt.current++);
    retryTimer.current = setTimeout(() => {
      retryTimer.current = null;
      void flushRef.current();
    }, wait);
  }, []);

  const sendOnce = useCallback(
    async (generation: number): Promise<FlushResult> => {
      if (accessToken === null || section == null) return 'clean';

      /*
       * <b>Speaking's sheet is not this client's to send.</b>
       *
       * The server refuses `module: "speaking"` on this endpoint outright, and
       * it is right to: that sheet holds recording ids the *server* wrote, and
       * a caller who can replace them can point their own marking at any id
       * they can name — which is a way to be marked on somebody else's
       * performance. The upload endpoint files the id itself; there is nothing
       * left for an autosave to carry.
       *
       * Guarded here as well as at the call site, because a flush also comes
       * from the expiry path and from both endings, and one of those growing a
       * Speaking sheet again would show up only as a red chip over a recording
       * the server already had. → `SubmitSpeakingRecording`
       */
      if (section.module === 'speaking') {
        /*
         * <b>The generation moves too, and that is not bookkeeping.</b>
         *
         * Leaving it behind meant `flush` re-entered for the same draft on
         * every call and never converged: the chip stayed on "đang chờ lưu"
         * for the rest of the section while nothing was ever sent. Reachable
         * whenever a Speaking part carries a question that is not a recording,
         * because those are wired to `change()` like any other.
         */
        if (generation > savedGeneration.current) savedGeneration.current = generation;
        pendingChanges.current = {};
        dirty.current = false;
        return 'clean';
      }

      const sending = { ...pendingChanges.current };

      /*
       * Nothing outstanding. The generation still has to move, or the drain
       * loop below would call this again for the same draft, for ever.
       */
      if (Object.keys(sending).length === 0) {
        if (generation > savedGeneration.current) savedGeneration.current = generation;
        dirty.current = false;
        // <b>`clean`, not `saved`.</b> Nothing was sent, so nothing was
        // acknowledged, and a caller about to submit is owed the difference
        // even though both outcomes let it through.
        return 'clean';
      }

      setSave('sending');
      try {
        // Only the tokens for what is being sent. A map of every question the
        // page has ever touched would grow for the length of the section and
        // put the whole history on the wire every 1.2 seconds.
        const sendingSequences: Record<string, number> = {};
        for (const questionId of Object.keys(sending)) {
          const seq = sequences.current[questionId];
          if (seq !== undefined) sendingSequences[questionId] = seq;
        }

        const saved = await saveAnswers(
          accessToken,
          sessionId,
          section.module,
          sending,
          revisionRef.current,
          sendingSequences,
        );

        // Unmounted mid-flight. The write may well have landed, but this page
        // cannot claim it did, and a caller about to submit must not proceed on
        // an outcome nobody observed.
        if (!alive.current) return 'failed';

        /*
         * <b>Only the entries that are still what was sent are cleared.</b>
         *
         * An answer re-typed while the request was in flight has a value the
         * server has never seen, so dropping it here would lose exactly one
         * keystroke's worth of work — invisibly, and most often on the last
         * answer before "Nộp bài", which is the one a learner goes back to fix.
         */
        const outstanding: Record<string, string | null> = {};
        for (const [questionId, value] of Object.entries(pendingChanges.current)) {
          if (!(questionId in sending) || !Object.is(sending[questionId], value)) {
            outstanding[questionId] = value;
          }
        }
        pendingChanges.current = outstanding;

        /*
         * <b>And the journal forgets the same entries, by their own token.</b>
         *
         * Not "a save succeeded, clear the journal": the learner can type again
         * while a request is in flight, and the journal then holds a newer
         * token than the one being acknowledged. `acknowledge` deletes only
         * when what it holds is not newer, so the re-typed answer survives —
         * which is the same rule the loop above applies to `pendingChanges`,
         * and it has to be, or the two disagree about what is unsent.
         */
        if (journalKey.current !== null) {
          const key = journalKey.current;
          for (const questionId of Object.keys(sendingSequences)) {
            void acknowledge(key.sessionId, key.module, questionId, sendingSequences[questionId]!);
          }
        }

        /*
         * <b>Forward only.</b> Two responses can arrive out of order, and a
         * lower revision landing last would roll this page back and make it
         * ask, for ever, for a sheet it already has.
         */
        const returned = saved?.revision;
        if (
          typeof returned === 'number' &&
          (revisionRef.current === null || returned > revisionRef.current)
        ) {
          revisionRef.current = returned;
        } else if (returned === undefined) {
          // A server that answers with no body at all. Null means "I did not
          // look", which is the permissive path rather than a stale number.
          revisionRef.current = null;
        }

        /*
         * <b>Another tab's answers, taken in rather than deleted.</b>
         *
         * The sheet comes back only when this page's base revision was behind,
         * which means somebody wrote between its last save and this one. Their
         * answers are on the server and not on this screen, so the learner
         * would otherwise sit in front of a section that is quietly missing
         * work — and, before the patch, would have deleted it on their next
         * keystroke.
         *
         * A question with an unacknowledged local edit is skipped. What this
         * learner typed a moment ago is newer than anything the response can
         * carry, and taking the words out from under someone mid-sentence is a
         * worse outcome than being briefly out of date.
         */
        /*
         * <b>Their tokens are taken in with their answers, and the two must not
         * be separated.</b>
         *
         * Absorbing another tab's answer without its token leaves this page's
         * counter below theirs for that question, so the learner's next edit to
         * it carries a token the server ignores — and they watch their own
         * correction do nothing at all, repeatedly, with no error anywhere.
         */
        const theirs = saved?.sequences;
        if (theirs !== undefined) {
          for (const [questionId, seq] of Object.entries(theirs)) {
            observe(seq);
            if (questionId in pendingChanges.current) continue;
            sequences.current[questionId] = seq;
          }
        }

        const merged = saved?.answers;
        if (merged !== undefined) {
          const next = { ...latestSheet.current };
          let absorbed = false;

          const mine = new Set((section.parts ?? []).flatMap((p) => p.questions.map((q) => q.id)));

          for (const [questionId, value] of Object.entries(merged)) {
            // Only this section's own questions. A sheet carrying anything else
            // is a server that has gone wrong, and taking it in would quietly
            // stop `answers` being a map of what is on screen — and force a
            // re-render on every autosave for a value nothing draws.
            if (!mine.has(questionId)) continue;
            if (questionId in pendingChanges.current) continue;
            if (Object.is(next[questionId], value)) continue;
            next[questionId] = value;
            absorbed = true;
          }

          if (absorbed) {
            latestSheet.current = next;
            setAnswers(next);
          }
        }

        if (generation > savedGeneration.current) savedGeneration.current = generation;

        // The connection is back. A later failure starts its backoff from the
        // beginning rather than inheriting the wait of the one before it.
        retryAttempt.current = 0;
        if (retryTimer.current !== null) {
          clearTimeout(retryTimer.current);
          retryTimer.current = null;
        }

        /*
         * <b>Only the newest draft may clear the flag.</b> An acknowledgement
         * for A says nothing about B, and treating it as if it did is exactly
         * how an answer gets left behind at submit.
         */
        if (savedGeneration.current >= draftGeneration.current) {
          dirty.current = false;
          setSave('saved');
          acknowledged.current?.();
        } else {
          dirty.current = true;
          setSave('pending');
        }

        return 'saved';
      } catch (caught) {
        if (!alive.current) return 'failed';

        if (caught instanceof ApiError && TERMINAL_REFUSALS.has(caught.problem.code)) {
          // A fact about the section, not about an entry. Nothing in this patch
          // will land, and keeping it queued would re-offer the same rejected
          // bytes on every flush and hold the ending shut.
          //
          // The journal goes with it. A closed section takes no more writes
          // (ADR-0015), so anything still journalled for it is work that can
          // never be sent — and restoring it on the next load would put an
          // answer on screen the learner can neither save nor remove.
          if (journalKey.current !== null) {
            void forgetSection(journalKey.current.sessionId, journalKey.current.module);
          }

          pendingChanges.current = {};
          if (generation > savedGeneration.current) savedGeneration.current = generation;
          dirty.current = false;
          setSave('failed');
          return 'refused';
        }

        if (caught instanceof ApiError && caught.problem.code === VALIDATION_FAILED) {
          /*
           * <b>Drop what the server named. Keep everything else.</b>
           *
           * `errors` carries one row per refused question id. Only the entries
           * still holding the value that went out are removed: an answer
           * re-typed while the request was in flight is a different answer, and
           * the server has never seen it.
           */
          const named = (caught.problem.errors ?? [])
            .map((error) => error.path)
            .filter((questionId) => questionId in sending);

          if (named.length === 0) {
            // Nothing to keep and nothing to retry. Same shape as a terminal
            // refusal, deliberately: guessing which entry was at fault would
            // re-offer a rejected patch for ever and shut the ending.
            pendingChanges.current = {};
            if (generation > savedGeneration.current) savedGeneration.current = generation;
            dirty.current = false;
            setSave('failed');
            return 'refused';
          }

          const kept = { ...pendingChanges.current };
          const reasons: Record<string, string> = {};

          for (const questionId of named) {
            reasons[questionId] =
              caught.problem.errors?.find((error) => error.path === questionId)?.message ??
              caught.problem.detail;

            if (Object.is(kept[questionId], sending[questionId])) delete kept[questionId];
          }

          pendingChanges.current = kept;
          setRefused((current) => ({ ...current, ...reasons }));

          // A refused answer is never going to be saved, so journalling it
          // would restore it on the next load into a section that will refuse
          // it again — a permanent 4xx retried for ever, which is exactly what
          // the journal must not become.
          if (journalKey.current !== null) {
            const key = journalKey.current;
            for (const questionId of named) {
              void acknowledge(key.sessionId, key.module, questionId, Number.MAX_SAFE_INTEGER);
            }
          }

          /*
           * <b>The generation moves only when nothing is left.</b>
           *
           * It is what the submit and advance gates read. Advancing it with
           * good answers still queued is precisely the bug being fixed: the
           * gate opens, the paper is marked, and the work is not in it.
           */
          if (Object.keys(kept).length === 0) {
            if (generation > savedGeneration.current) savedGeneration.current = generation;
            dirty.current = false;
            setSave('failed');
            return 'refused';
          }

          dirty.current = true;
          setSave('failed');

          // <b>`failed`, not `refused`.</b> The drain stops either way, but the
          // ending must not be let through — there is still work the server has
          // not taken, and `refused` is the word that opens the gate.
          return 'failed';
        }

        /*
         * `queued` is entered here and nowhere else.
         *
         * `change()` used to set it too, 1200ms before anything was attempted,
         * so the debounce window and a dead connection rendered as the same
         * chip with the same words — "Chưa gửi được". An essay writer saw that
         * on every keystroke, learned to ignore it, and then it meant nothing
         * on the one occasion it was true. Typing is now `pending` (neutral,
         * "đang chờ lưu"); `queued` means the server did not take it.
         *
         * <b>`pendingChanges` is deliberately not cleared.</b> The next flush
         * sends the same entries again, which is safe because a patch applied
         * twice is the same patch — and because the alternative is dropping
         * work the server never received.
         */
        setSave(caught instanceof ApiError ? 'failed' : 'queued');
        dirty.current = true;
        scheduleRetry();
        return 'failed';
      }
    },
    [accessToken, section, sessionId, alive, scheduleRetry],
  );

  /**
   * Writes until the newest draft has been acknowledged, one request at a time.
   *
   * <b>Single-flight, and the loop is what makes it safe rather than merely
   * tidy.</b> Two concurrent PUTs from one tab race each other into exactly the
   * conflicts the revision was added to detect — a tab manufacturing its own
   * contention. Serialising them and always reading `pendingChanges` at send
   * time means an edit made mid-flight is picked up by the next turn of this
   * loop rather than by a second request.
   *
   * A caller that arrives while a drain is running joins it instead of starting
   * another; the outer loop then re-checks, because an edit can land between
   * the drain's last check and its resolution.
   */
  const flush = useCallback(async (): Promise<FlushResult> => {
    let result: FlushResult = 'clean';

    while (savedGeneration.current < draftGeneration.current) {
      draining.current ??= (async () => {
        let last: FlushResult = 'clean';

        while (savedGeneration.current < draftGeneration.current) {
          const generation = draftGeneration.current;
          last = await sendOnce(generation);

          // A failure is not retried inside the drain. It needs a decision —
          // back off, or tell the learner — and spinning here would send the
          // same rejected patch as fast as the network could refuse it.
          if (last !== 'saved') break;
        }

        return last;
      })().finally(() => {
        draining.current = null;
      });

      result = await draining.current;
      if (result !== 'saved') return result;
    }

    return result;
  }, [sendOnce]);

  const flushRef = useRef(flush);
  flushRef.current = flush;

  /*
   * <b>The debounce does not outlive the component.</b>
   *
   * It used to. The timer stayed armed through unmount, fired against a page
   * that no longer existed, and started a write nothing could observe —
   * `sendOnce` checks `alive` after its await and declines to touch state with
   * no component behind it, so the response went nowhere by design while the
   * request went out anyway.
   *
   * That is a contradiction rather than a feature, and it had a cost: in the
   * test suite one sitting's autosave arrived during the *next* test, where it
   * made an assertion about single-flight pass while measuring nothing. A
   * stray request is exactly as invisible in production, and there it lands on
   * a live sitting.
   */
  useEffect(
    () => () => {
      if (timer.current !== null) clearTimeout(timer.current);
      if (retryTimer.current !== null) clearTimeout(retryTimer.current);
    },
    [],
  );

  const cancelPending = useCallback(() => {
    if (timer.current !== null) clearTimeout(timer.current);

    // The retry too. Both endings call this and then flush themselves, so a
    // backoff still armed would fire a second write into a section that is
    // closing — and meet the refusal the closure protocol exists to give it.
    if (retryTimer.current !== null) {
      clearTimeout(retryTimer.current);
      retryTimer.current = null;
    }
  }, []);

  /**
   * A recording the server has already filed against its question.
   *
   * <b>Deliberately not `change()`.</b> `change` is the handler for an answer
   * the learner composed here, so it marks the sheet dirty and schedules an
   * autosave — and the autosave for Speaking is a request the server refuses,
   * because the Speaking sheet is the server's own index of what was uploaded.
   *
   * The visible cost of routing a recording through `change` was a learner
   * watching the save chip turn to "Gửi thất bại" a second after their spoken
   * answer had, in fact, been stored — in the middle of a timed section, with
   * nothing to press. Nothing was lost, and the interface said otherwise.
   *
   * What this does keep is the local copy of the id, so the recorder stays in
   * its stored state and the "đã trả lời" count is right without a reload.
   */
  const recorded = useCallback((questionId: string, recordingId: string) => {
    /*
     * The mirror is written here, outside the setter, and not from inside an
     * updater. An updater runs during render — twice under StrictMode, and on
     * renders React then discards — so a ref written there is a side effect in
     * the one place React guarantees purity. It is also unordered against
     * `change`'s non-functional setter, which would leave the state and the
     * mirror disagreeing in opposite directions: the recorder reverting to
     * "chưa ghi" for audio the server already has.
     */
    const next = { ...latestSheet.current, [questionId]: recordingId };
    latestSheet.current = next;
    setAnswers(next);
  }, []);

  /*
   * <b>The updater does nothing but compute the next sheet.</b>
   *
   * It used to also set a ref, call `setSave`, clear a timeout and start a new
   * one — all inside `setAnswers`. React requires updaters to be pure and
   * double-invokes them under StrictMode, so the first `setTimeout` handle was
   * overwritten before it could be cleared and both fired: one radio click
   * measured **two** PUTs. Under concurrent rendering the same shape can
   * re-run in production, which is the version nobody would find.
   */
  const change = useCallback((questionId: string, value: string | null) => {
    /*
     * <b>Built from the mirror, not from the `answers` closure.</b>
     *
     * The merge in `sendOnce` calls `setAnswers` from a promise continuation,
     * and React does not commit that synchronously. A keystroke handled by the
     * render that came *before* the commit would compute its next sheet without
     * the other tab's answer and write that over both the state and the mirror
     * — and because the revision has already moved on, the server will never
     * offer the merged sheet again. The learner would watch the other device's
     * answer appear and then vanish on their next keypress, for good.
     */
    const next = { ...latestSheet.current, [questionId]: value };
    setAnswers(next);

    /*
     * <b>The refs, not the state, are what the save queue reads.</b>
     *
     * `setAnswers` is asynchronous, so a save fired from this same tick would
     * send the sheet as it was before this keystroke. More importantly the
     * generation has to move *now*: it is what tells an in-flight response for
     * the previous draft that it no longer speaks for the current one.
     */
    latestSheet.current = next;
    pendingChanges.current = { ...pendingChanges.current, [questionId]: value };

    // One token per edit, monotonic across the section. Re-typing the same
    // question issues a higher one, which is what makes the later keystroke win
    // however the two requests are ordered on the way out.
    const sequence = nextSequence.current++;
    sequences.current = { ...sequences.current, [questionId]: sequence };

    /*
     * <b>To disk before it is on the wire.</b> The 1.2 s debounce is 1.2 s in
     * which the only copy of this keystroke is in memory, and the whole point
     * of the journal is that a tab which goes away in that window does not take
     * the answer with it.
     *
     * Not awaited, and the failure is not reported: a keystroke must not wait
     * on a disk write, and a journal that cannot be written is a journal that
     * is not there — the exam carries on exactly as it did before this existed.
     */
    if (journalKey.current !== null) {
      void remember({
        sessionId: journalKey.current.sessionId,
        module: journalKey.current.module,
        questionId,
        value,
        sequence,
        savedAt: Date.now(),
      });
    }

    draftGeneration.current += 1;

    /*
     * A refusal is about the answer that was refused, and this is a different
     * answer. Leaving the notice up would tell a learner who has just fixed
     * the problem that it is still there.
     */
    setRefused((current) => {
      if (!(questionId in current)) return current;
      const { [questionId]: _gone, ...rest } = current;
      return rest;
    });

    dirty.current = true;
    setSave('pending');

    if (timer.current !== null) clearTimeout(timer.current);
    timer.current = setTimeout(() => void flushRef.current(), AUTOSAVE_MS);
  }, []);

  /**
   * The sitting this sheet belongs to, for the journal's key.
   *
   * A ref rather than a closure: `seed` runs from a load and from every
   * advance, and it must key the journal by the section it is seeding rather
   * than by whichever render happened to create the callback.
   */
  const journalKey = useRef<{ sessionId: string; module: string } | null>(null);

  const seed = useCallback((next: SheetSeed) => {
    const sheet = next.answers;

    // The refs are what the save queue and the merge read, and neither can
    // see state set in this same tick. A page that loaded with a sheet but
    // an empty mirror would merge a server response into `{}` and blank the
    // section on the first save from another tab.
    latestSheet.current = sheet;
    pendingChanges.current = {};

    /*
     * Section-scoped like every other counter here. Each section has its own
     * sheet and its own tokens, and carrying one section's numbers into another
     * would order a question against a value that was never about it.
     */
    sequences.current = {};
    nextSequence.current = 0;

    /*
     * <b>Seeded from the response, never assumed.</b>
     *
     * This used to stay null until the first save, and null tells the
     * server "I did not look". The write lands either way now — a patch has
     * nothing to refuse — but a caller that never states a base revision is
     * never told it is behind, so a second tab's answers would stay
     * invisible on this screen until the learner reloaded the page.
     *
     * `?? null` covers a server that does not report the field. Being told
     * nothing is worse than being told the truth and better than inventing
     * a number, which would suppress the very merge it needs.
     */
    revisionRef.current = next.answerRevision ?? null;

    /*
     * The counters are section-scoped like everything else in this list. They
     * are equal whenever an advance's flush gate let us through, so resetting
     * them is harmless today — and the list is presented as exhaustive, which
     * is the property worth keeping true rather than nearly true.
     */
    draftGeneration.current = 0;
    savedGeneration.current = 0;
    dirty.current = false;

    setAnswers(sheet);
    setSave('idle');
    setRefused({});
  }, []);

  /*
   * ── Bring back what the last load never sent ───────────────────────────
   *
   * <b>The window this closes.</b> A learner types, the autosave has not fired
   * or is in flight, and the tab goes away — a crash, a WebView the OS
   * reclaimed, a phone that lost signal and was pulled to refresh. Everything
   * the page held was in memory, so the sitting comes back looking exactly as
   * it did before they typed. On a timed paper that is minutes of work and
   * nothing on screen ever admitted it was at risk.
   *
   * <b>Only entries the server has not already surpassed.</b> The section view
   * carries the sheet as stored; a journal entry whose token is not greater
   * than the stored one describes a write that already landed, and restoring it
   * would put an old answer back on screen over a newer one.
   *
   * <b>It does not send from the load itself — it schedules the ordinary
   * autosave, exactly as a keystroke would.</b>
   *
   * Sending inside this effect would race the section's own seeding and, on a
   * section that has since closed, would spend the learner's first seconds on a
   * request that can only be refused. Going through the same 1.2 s debounce
   * avoids both: by the time it fires the section has settled, and the flush
   * gate is the one that already knows what a closed section means.
   *
   * <b>Scheduling it is not optional, and leaving it out was a real defect —
   * found by the browser suite on 2026-08-28, not by review.</b> Until then this
   * effect set `pending` and stopped. Nothing else schedules a flush, so a
   * restored answer sat "waiting to save" for as long as the tab was open and
   * was only ever sent if the learner happened to type something else. The
   * learner who reloads is the learner whose connection just dropped — and the
   * one most likely to close the tab next, which is when the answer went for
   * good. Nothing on screen ever stopped saying it was waiting.
   * → `e2e/tests/offline.spec.ts`
   */
  useEffect(() => {
    if (section === null) return;

    const key = { sessionId, module: section.module };
    journalKey.current = key;

    let cancelled = false;

    void (async () => {
      const held = await restore(key.sessionId, key.module);
      if (cancelled || held.length === 0) return;

      const stored = section.answerSequences ?? {};
      const next = { ...latestSheet.current };
      let restoredAny = false;

      for (const entry of held) {
        if (entry.sequence <= (stored[entry.questionId] ?? -1)) continue;

        next[entry.questionId] = entry.value;
        pendingChanges.current[entry.questionId] = entry.value;
        sequences.current[entry.questionId] = entry.sequence;
        observe(entry.sequence);
        restoredAny = true;
      }

      if (!restoredAny) return;

      latestSheet.current = next;
      setAnswers(next);

      // The generation moves so the flush gate sees outstanding work, and so
      // the submit gate holds until it has landed.
      draftGeneration.current += 1;
      dirty.current = true;
      setSave('pending');

      // And the same debounce a keystroke uses, so "waiting to save" ends.
      if (timer.current !== null) clearTimeout(timer.current);
      timer.current = setTimeout(() => void flushRef.current(), AUTOSAVE_MS);
    })();

    return () => {
      cancelled = true;
    };
  }, [sessionId, section, observe]);

  return { answers, save, refused, dirty, change, recorded, flush, flushRef, seed, cancelPending };
}
