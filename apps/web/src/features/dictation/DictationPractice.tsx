import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { SentenceAudio } from './SentenceAudio.js';
import {
  checkSentence,
  getDictationSet,
  type DictationResultView,
  type DictationSetView,
} from './dictationApi.js';
import '../../styles/dictation.css';

/**
 * Nghe chép chính tả, the working part — `M-22`.
 *
 * <b>Split out of the page on 24/08, because the page around it became
 * public.</b> `[QUYẾT ĐỊNH]` chủ sản phẩm: the header lists four modules and
 * each one owns a page. `/dictation` is now the pitch for this module as well
 * as the way into it, so a signed-out visitor reads the whole thing — and this
 * is the one block that needs a token. It handles that itself rather than
 * making the route choose between being reachable and being useful. Same
 * arrangement as `PracticeExamPicker` on `/practice`, deliberately: two
 * modules solving the same problem two different ways is how a shell ends up
 * with a flag on it.
 *
 * <b>Not an exam, and it does not pretend to be one.</b> No timer, no band, no
 * submission: play a sentence as often as you like, type it, see which words
 * you actually heard. Bolting it onto the exam engine would have dragged a
 * server-authoritative deadline onto something that has none.
 *
 * <b>Replay is unlimited here, and that is deliberate.</b> Listening forbids
 * rewinding because it is a test of hearing something once. Dictation is
 * practice at hearing it at all, and a learner who cannot replay simply
 * guesses. Two features, two rules, and the difference is the point.
 *
 * <b>Nothing is marked in the browser.</b> The typed sentence goes to the
 * server and the verdict comes back; the answer arrives only with it.
 */

type State =
  | { kind: 'anonymous' }
  | { kind: 'loading' }
  | { kind: 'ready'; set: DictationSetView }
  | { kind: 'failed' };

export function DictationPractice({ setId }: { setId: string }) {
  const { accessToken, status } = useAuth();
  const { t } = useI18n();

  const [state, setState] = useState<State>({ kind: 'loading' });
  const [index, setIndex] = useState(0);
  const [typed, setTyped] = useState('');
  const [result, setResult] = useState<DictationResultView | null>(null);
  const [checking, setChecking] = useState(false);

  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    // `status` is read as well as the token: during a session restore the
    // token is still null, and answering that with the sign-in gate would
    // flash a wall at someone who is already signed in.
    if (status === 'loading') return;

    if (accessToken === null) {
      setState({ kind: 'anonymous' });
      return;
    }

    try {
      /*
       * <b>The set is named by the route now, not picked from the list.</b>
       * This used to `listDictationSets` and take `sets[0]` — which worked
       * while the catalogue held one set and silently became "whichever the
       * server happened to sort first" at two. It also fetched the whole
       * catalogue to throw all but one row away.
       */
      const set = await getDictationSet(accessToken, setId);
      if (alive.current) setState({ kind: 'ready', set });
    } catch {
      // A 404 lands here too — an id that is not in the catalogue is a stale
      // link, and `DictationSetPage` renders it as one rather than as an
      // outage. → `missing`
      if (alive.current) setState({ kind: 'failed' });
    }
  }, [accessToken, status, setId]);

  useEffect(() => void load(), [load]);

  async function check() {
    if (accessToken === null || state.kind !== 'ready') return;
    const sentence = state.set.sentences[index];
    if (sentence === undefined) return;

    setChecking(true);
    try {
      const verdict = await checkSentence(accessToken, state.set.id, sentence.order, typed);
      if (alive.current) setResult(verdict);
    } catch {
      if (alive.current) setResult(null);
    } finally {
      if (alive.current) setChecking(false);
    }
  }

  function move(to: number) {
    setIndex(to);
    setTyped('');
    setResult(null);
  }

  if (state.kind === 'anonymous') {
    return (
      <div className="dict-gate">
        <h3>Đăng nhập để mở bộ câu</h3>
        <p>
          Câu nghe và câu đúng đều nằm trên máy chủ — trình duyệt không giữ bản nào, nên phần chấm
          từng từ chỉ chạy được khi có tài khoản. Tạo tài khoản mất chưa tới một phút.
        </p>
        <div className="dict-gate-actions">
          <Link className="btn btn-primary" to={Paths.signUp}>
            Tạo tài khoản miễn phí <span>→</span>
          </Link>
          <Link className="btn btn-secondary" to={Paths.signIn}>
            Tôi đã có tài khoản
          </Link>
        </div>
      </div>
    );
  }

  if (state.kind === 'loading') {
    return <p className="dict-note">{t('exam.loading')}</p>;
  }

  if (state.kind !== 'ready') {
    return (
      <div className="dict-gate" role="alert">
        <h3>{t('common.notConnected')}</h3>
        <p>
          Không mở được bộ câu này. Có thể đường dẫn đã cũ, hoặc kết nối đang có vấn đề — thử lại,
          hoặc quay lại kho bài nghe để chọn bộ khác.
        </p>
        <div className="dict-gate-actions">
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => {
              setState({ kind: 'loading' });
              void load();
            }}
          >
            {t('exam.tryAgain')}
          </button>
          <Link className="btn btn-secondary" to={Paths.dictation}>
            Về kho bài nghe
          </Link>
        </div>
      </div>
    );
  }

  const { set } = state;
  const sentence = set.sentences[index];
  const last = index === set.sentences.length - 1;

  return (
    <>
      <header className="dict-set-head">
        <h3>{set.title}</h3>
        <p>{set.description}</p>
      </header>

      <section className="dict-card">
        <div className="dict-head">
          <h4>{t('dict.sentenceOf', { index: index + 1, total: set.sentences.length })}</h4>
          <span className="dict-chip">{t('dict.replayable')}</span>
        </div>

        {sentence !== undefined && <SentenceAudio reference={sentence.audioKey} />}

        <label className="dict-field">
          <span>{t('dict.typeWhatYouHear')}</span>
          <textarea
            rows={3}
            value={typed}
            disabled={checking}
            onChange={(event) => setTyped(event.target.value)}
            onKeyDown={(event) => {
              // Enter checks; Shift+Enter is a newline. A dictation sentence
              // is one line, and reaching for the mouse after every attempt
              // is most of the friction in this exercise.
              if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault();
                void check();
              }
            }}
          />
        </label>

        <div className="dict-actions">
          <button
            type="button"
            className="dict-check"
            disabled={checking || typed.trim() === ''}
            onClick={() => void check()}
          >
            {checking ? t('dict.checking') : t('dict.check')}
          </button>

          {result !== null && !last && (
            <button type="button" className="dict-next" onClick={() => move(index + 1)}>
              {t('dict.next')}
            </button>
          )}

          {index > 0 && (
            <button type="button" className="dict-next" onClick={() => move(index - 1)}>
              {t('dict.previous')}
            </button>
          )}
        </div>

        {result !== null && <Verdict result={result} />}
      </section>
    </>
  );
}

/**
 * The word-by-word verdict.
 *
 * <b>Four states, and colour is never the only difference.</b> A wrong word is
 * struck through, a missing one is a dashed placeholder, an extra one is
 * struck and greyed. That is what keeps it readable for someone who cannot
 * separate the hues — and this is the one screen in the product where `--bad`
 * is right, because a wrong word IS the thing that went wrong rather than a
 * clock that is merely running.
 */
function Verdict({ result }: { result: DictationResultView }) {
  const { t } = useI18n();

  return (
    <div className="dict-result" role="status">
      <p className="dict-score">
        <span className="num">
          {t('dict.score', { correct: result.correct, total: result.total })}
        </span>
        {result.isPerfect && <span className="dict-perfect">{t('dict.perfect')}</span>}
      </p>

      <p className="dict-words">
        {result.words.map((word, position) => {
          const key = `${position}-${word.expected ?? word.typed ?? ''}`;

          if (word.verdict === 'correct') {
            return (
              <span className="w w-correct" key={key}>
                {word.expected}
              </span>
            );
          }

          if (word.verdict === 'missing') {
            return (
              <span className="w w-missing" key={key} title={t('dict.missing')}>
                {word.expected}
              </span>
            );
          }

          if (word.verdict === 'extra') {
            return (
              <span className="w w-extra" key={key} title={t('dict.extra')}>
                {word.typed}
              </span>
            );
          }

          return (
            <span className="w w-wrong" key={key}>
              <s>{word.typed}</s> {word.expected}
            </span>
          );
        })}
      </p>

      <p className="dict-answer">
        <span>{t('dict.actual')}</span> {result.text}
      </p>
    </div>
  );
}
