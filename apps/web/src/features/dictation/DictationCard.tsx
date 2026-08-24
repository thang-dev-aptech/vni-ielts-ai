import { Link } from 'react-router-dom';
import { Paths } from '../../routes/paths.js';
import type { DictationItem } from './dictationCatalogue.js';

/**
 * One dictation set, as a card.
 *
 * <b>Three fields, because the catalogue carries three fields.</b> Title,
 * description, sentence count. The reference layout puts six metadata rows on
 * each card — band, topic, level, duration, question count, skill — and five
 * of those have no source anywhere in this product: not in the API view, not
 * in the domain record, not in the fixture format. A "Band 6.5 · Environment ·
 * 04:00" line would be invented at the point of rendering, which is the one
 * failure this codebase keeps a rule about.
 *
 * <b>No audio preview, and not only because of the missing duration.</b> The
 * per-sentence audio is fetched with the set and played one sentence at a
 * time; a card that streamed it would be handing over the exercise before the
 * learner has typed anything. The card's job is discovery — it says what the
 * set is and sends you to it.
 *
 * <b>No progress and no accuracy.</b> `checkSentence` compares and returns; it
 * stores nothing. There is no dictation attempt anywhere in the backend, so
 * "Đã hoàn thành" or "Accuracy 82%" would be a number with no reader behind
 * it. When attempts are persisted this card gains a state, not a redesign.
 */
export function DictationCard({ item }: { item: DictationItem }) {
  return (
    <li className="dset-card">
      <span className="dset-card-mark" aria-hidden="true">
        <Waveform />
      </span>

      <div className="dset-card-body">
        <h3 className="dset-card-title">
          {/*
            The whole card is not the link, the title is.
            Opening a set is a plain navigation, so a link is right — but a
            card-sized hit area swallows text selection, and someone reading a
            description wants to be able to select a phrase from it.
          */}
          <Link to={Paths.dictationSet(item.id)}>{item.title}</Link>
        </h3>

        {item.description !== '' && <p className="dset-card-lead">{item.description}</p>}

        <p className="dset-card-meta">
          <span className="num">{item.sentenceCount}</span>&nbsp;câu · Nghe lại không giới hạn
        </p>
      </div>

      <span className="dset-card-go" aria-hidden="true">
        Bắt đầu <span>→</span>
      </span>
    </li>
  );
}

/**
 * A sound wave, drawn.
 *
 * `aria-hidden`: the card already says what it is in words, and a screen
 * reader announcing "sound wave" before every title helps nobody. Nine bars at
 * fixed heights — it illustrates "this is audio", it does not chart anything,
 * and there is no scale to misread.
 */
function Waveform() {
  const bars = [10, 18, 28, 20, 34, 24, 30, 16, 11];

  return (
    <svg viewBox="0 0 76 40" role="presentation" focusable="false">
      {bars.map((height, i) => (
        <rect
          key={i}
          x={4 + i * 8}
          y={20 - height / 2}
          width="4"
          height={height}
          rx="2"
          fill="currentColor"
          opacity={i === 4 ? 1 : 0.42}
        />
      ))}
    </svg>
  );
}
