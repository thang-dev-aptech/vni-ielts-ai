import type { CSSProperties } from 'react';
import { useI18n } from '../../../i18n/index.js';
import { formatDuration, SKILLS } from '../skills.js';
import { variantLabel, type PracticeItem } from './practiceCatalogue.js';

/**
 * One thing a learner can start, as a card.
 *
 * <b>Four questions, in the order a reader asks them.</b> What is this · what
 * is in it · how long will it take · can I start now. The skill badge answers
 * the first before the title is read; the metadata row answers the middle two;
 * the button answers the last and is the only saturated thing on the card.
 *
 * <b>The metadata is small and grey on purpose.</b> On the reference layout
 * this row competes with the title — same weight, same size, a badge each.
 * Everything here except the title and the button is one step quieter, because
 * a grid of these is scanned by title.
 *
 * <b>Nothing on the card is invented.</b> Title, variant, question count and
 * duration are the four fields `ExamCatalogueItem` carries. There is no band
 * level, no difficulty and no topic because the catalogue has none of them —
 * a "Band 6.5 · Medium" line would have to come from somewhere, and the only
 * available somewhere is us. → `practiceCatalogue.ts` § FACET_SEAM
 *
 * <b>The whole card is not a link.</b> Starting a sitting is a POST that opens
 * a server-side session with a deadline on it; that is a button, and it must
 * not be something a stray click on a card body can fire.
 */
export function PracticeCard({
  item,
  busy,
  onStart,
}: {
  item: PracticeItem;
  busy: boolean;
  /**
   * `timing` is the learner's own choice of clock, and it is asked here rather
   * than inferred from the mode bar above the grid.
   *
   * <b>Luyện đề / Thi thử is not the same pair as Full Test / Single Skill.</b>
   * `E-11` confirms the first two, `E-20` confirms the second two, and nothing
   * says how they compose — `B-13`. Reading "Luyện từng kỹ năng" as "luyện đề"
   * would answer that question by fiat, from a control that was built to mean
   * something else, and would leave no way to sit a timed single-skill paper.
   * Two buttons state the choice instead of guessing it. → `X-4`
   */
  onStart: (item: PracticeItem, timing: 'deadline' | 'open') => void;
}) {
  const { t } = useI18n();
  const skill = item.module === null ? null : SKILLS[item.module];
  const full = skill === null;

  return (
    <li
      className={`prac-card${full ? ' is-full' : ''}`}
      // A single-skill card carries its skill's own ink and tint as CSS
      // variables, so the top accent bar and hover glow can be pure CSS. A
      // full-test card gets neither — `.is-full` paints a fixed four-colour
      // bar instead, since no single skill applies.
      style={
        full ? undefined : ({ '--sk-ink': skill.ink, '--sk-tint': skill.tint } as CSSProperties)
      }
    >
      <div className="prac-card-top">
        {/*
          <b>The badge only appears in full-test mode.</b> In single-skill mode
          the grid is one skill by construction — the reader chose it two
          controls ago and the heading above says it — so a "Reading" chip on
          every one of six cards repeated the same word six times and pushed
          the one thing that differed, the title, down a line.
        */}
        {full ? (
          <span className="prac-badge is-full-badge">
            <span aria-hidden="true">◈</span>
            {t('exam.modeFull')}
          </span>
        ) : (
          <span className="prac-card-lead" />
        )}

        {/* The API lower-cases every enum, which is right for a value a client
            compares and wrong for one a person reads — and the spelling has to
            match the filter's, or the reader has to work out whether "General"
            and "General Training" are the same value. */}
        <span className="prac-variant">{variantLabel(item.variant)}</span>
      </div>

      <h3 className="prac-title">{item.title}</h3>

      {/*
        <b>The paper's own sentence, and the reason it exists.</b> Until
        2026-09-03 a card could say a title, a variant and a question count —
        enough to choose between two papers only if their titles already did the
        work. "VOL 9 Test 3" beside "VOL 9 Test 4" is not a choice anyone can
        make, and sixteen more of those are being imported.

        Rendered only when the package supplied one. A missing description is a
        card with one less line, never a placeholder: an invented sentence under
        a real paper reads as editorial the academic team did not write.
      */}
      {item.description !== null && <p className="prac-desc">{item.description}</p>}

      {/*
        <b>Per-module minutes, not a sentence.</b> This slot held "Bốn kỹ năng
        trong một phiên, theo thứ tự Reading → Listening → Writing → Speaking",
        which a comment defended as differing between cards. It cannot:
        `toFullItems` admits an exam only if it has all four modules and then
        sorts them into `SKILL_ORDER`, so every full-test card printed the same
        24 words. The minutes are the part that actually varies between papers.
      */}
      {full && item.parts.length > 0 && (
        <ul className="prac-parts">
          {item.parts.map((part) => (
            <li key={part.module}>
              <span
                className="prac-part-key"
                style={{ background: SKILLS[part.module].tint, color: SKILLS[part.module].ink }}
                aria-hidden="true"
              >
                {SKILLS[part.module].name.slice(0, 1)}
              </span>
              {/* "phút", not the prime symbol. Every other duration on this
                  page says "phút", and `′` is a mathematics glyph rather than a
                  Vietnamese convention for minutes — several screen readers
                  announce it as "prime". */}
              <span className="prac-part-min">
                <span className="num">{part.minutes}</span> phút
              </span>
              <span className="sr-only">
                {SKILLS[part.module].name} {part.minutes} phút
              </span>
            </li>
          ))}
        </ul>
      )}

      {/*
        The digits are monospaced and the words are not.
        `formatDuration` returns "2 giờ 45 phút", and putting the whole phrase
        in `.num` set five Vietnamese words in JetBrains Mono beside "câu" in
        Nunito. Only the numerals belong in the tabular face.
      */}
      <ul className="prac-meta">
        <li>
          <span className="prac-meta-value num">{item.questionCount}</span>&nbsp;câu
        </li>
        <li>
          {durationParts(item.durationSeconds).map((part, at) =>
            part.digits ? (
              <span className="prac-meta-value num" key={at}>
                {part.text}
              </span>
            ) : (
              <span key={at}>{part.text}</span>
            ),
          )}
        </li>
        {/*
          The marking rule is a property of the skill, and in single-skill mode
          the reader picked the skill — so it appeared on six cards and in the
          status line above them, seven times on one screen. It stays on a
          full-test card, where the sitting genuinely mixes both kinds.
        */}
        {full && <li className="prac-meta-marking">Chấm hỗn hợp</li>}
      </ul>

      <div className="prac-starts">
        {/*
          <b>Luyện đề first, and only on a single-skill card.</b> The open-ended
          clock is what most people came for, and a full-test luyện đề sitting
          would need a chaining rule `B-13` has not written — so the offer is
          absent there rather than present and half-built. `G-11`.
        */}
        {!full && (
          <button
            type="button"
            className="prac-start is-practice"
            disabled={busy}
            onClick={() => onStart(item, 'open')}
            aria-label={`${t('practice.startPractice')} ${skill.name} — ${item.title} · ${t('practice.startPracticeHint')}`}
          >
            {busy ? t('exam.starting') : t('practice.startPractice')}
            <span aria-hidden="true">→</span>
          </button>
        )}

        <button
          type="button"
          className="prac-start is-mock"
          disabled={busy}
          onClick={() => onStart(item, 'deadline')}
          aria-label={
            full
              ? `${t('exam.startFull')} — ${item.title}`
              : `${t('exam.start')} ${skill.name} — ${item.title}`
          }
        >
          {busy ? t('exam.starting') : t('exam.start')}
          <span aria-hidden="true">→</span>
        </button>
      </div>
    </li>
  );
}

/**
 * `"2 giờ 45 phút"` split into numerals and words.
 *
 * The tabular face is for digits: it is what keeps a column of durations
 * aligned. Applied to the whole string it sets "giờ" and "phút" in a monospace
 * face too, which is what shipped.
 */
function durationParts(seconds: number): { text: string; digits: boolean }[] {
  return formatDuration(seconds)
    .split(/(\d+)/)
    .filter((piece) => piece !== '')
    .map((piece) => ({ text: piece, digits: /^\d+$/.test(piece) }));
}
