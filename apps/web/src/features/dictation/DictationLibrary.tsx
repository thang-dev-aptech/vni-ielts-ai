import { useCallback, useDeferredValue, useEffect, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useI18n } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import { useAuth } from '../auth/AuthContext.js';
import { Pagination } from '../chrome/Pagination.js';
import { DictationCard } from './DictationCard.js';
import { DictationFilters } from './DictationFilters.js';
import { listDictationSets, type DictationSetSummary } from './dictationApi.js';
import { buildFacets, matchesFacets, matchesQuery, toItems } from './dictationCatalogue.js';

const PER_PAGE = 12;

type State =
  | { kind: 'anonymous' }
  | { kind: 'loading' }
  | { kind: 'ready'; sets: DictationSetSummary[] }
  | { kind: 'failed' };

/**
 * The dictation library — search, filter, and a grid of sets.
 *
 * <b>This is the page's primary interaction, and it sits above every word of
 * education content.</b> The brief is explicit: a reader must not have to
 * scroll past an explanation of what dictation is to reach the sets. What is
 * below the grid is for someone who scrolled past the thing they came for.
 *
 * <b>It gates itself.</b> `/dictation` is public — it is the page that argues
 * for the feature — but `GET /api/v1/dictation` needs a token, deliberately:
 * a corpus that can be scraped anonymously can be republished with its
 * answers. So a visitor gets the sign-in card where the grid would be, and the
 * route never has to choose between reachable and useful.
 *
 * <b>Search is deferred, filters are not.</b> Typing re-runs a `fold` over
 * every title; `useDeferredValue` keeps the field responsive while the list
 * catches up. A checkbox is one discrete change and wants to feel instant.
 */
export function DictationLibrary() {
  const { accessToken, status } = useAuth();
  const { t } = useI18n();

  const [state, setState] = useState<State>({ kind: 'loading' });
  const [query, setQuery] = useState('');
  const [chosen, setChosen] = useState<Record<string, string[]>>({});
  const [page, setPage] = useState(1);

  const deferredQuery = useDeferredValue(query);

  /*
   * Set true on the way IN, not just false on the way out. StrictMode
   * double-invokes a mount effect — run, clean up, run again — and a flag only
   * cleared on the way out stays false for the second run, which is how a
   * screen sits on "Đang tải…" against an API that already answered 200.
   */
  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (status === 'loading') return;

    if (accessToken === null) {
      setState({ kind: 'anonymous' });
      return;
    }

    try {
      const { sets } = await listDictationSets(accessToken);
      if (alive.current) setState({ kind: 'ready', sets });
    } catch {
      if (alive.current) setState({ kind: 'failed' });
    }
  }, [accessToken, status]);

  useEffect(() => void load(), [load]);

  const items = useMemo(() => (state.kind === 'ready' ? toItems(state.sets) : []), [state]);

  /** Narrowed by the search box, before the facets count anything. */
  const searched = useMemo(
    () => items.filter((item) => matchesQuery(item, deferredQuery)),
    [items, deferredQuery],
  );

  const facets = useMemo(() => buildFacets(searched, chosen), [searched, chosen]);
  const shown = useMemo(
    () => searched.filter((item) => matchesFacets(item, chosen)),
    [searched, chosen],
  );

  const pages = Math.max(1, Math.ceil(shown.length / PER_PAGE));
  const safePage = Math.min(page, pages);
  const slice = shown.slice((safePage - 1) * PER_PAGE, safePage * PER_PAGE);

  // Narrowing has to put the reader back on page one, or a filter that leaves
  // four results while they are on page three shows an empty grid and reads as
  // "no results".
  useEffect(() => setPage(1), [deferredQuery, chosen]);

  const activeFilters = Object.values(chosen).reduce((sum, values) => sum + values.length, 0);
  const filtered = activeFilters > 0 || deferredQuery.trim() !== '';

  function toggle(facetId: string, value: string) {
    setChosen((was) => {
      const values = was[facetId] ?? [];
      return {
        ...was,
        [facetId]: values.includes(value)
          ? values.filter((one) => one !== value)
          : [...values, value],
      };
    });
  }

  function clearAll() {
    setChosen({});
    setQuery('');
  }

  return (
    <div className="dict-lib">
      {/*
        The search field renders in every state, including signed out.
        It is the control the page is organised around; hiding it until a fetch
        lands would make the layout jump under the reader's cursor.
      */}
      <div className="dict-search">
        <label className="dict-search-field">
          <span className="sr-only">Tìm bài nghe chép chính tả</span>
          <SearchIcon />
          <input
            type="search"
            value={query}
            placeholder="Tìm bài nghe theo tên hoặc mô tả…"
            disabled={state.kind !== 'ready'}
            onChange={(event) => setQuery(event.target.value)}
          />
          {query !== '' && (
            <button
              type="button"
              className="dict-search-clear"
              onClick={() => setQuery('')}
              aria-label="Xoá từ khoá tìm kiếm"
            >
              ✕
            </button>
          )}
        </label>
      </div>

      {state.kind === 'ready' && (
        <DictationFilters
          facets={facets}
          chosen={chosen}
          onToggle={toggle}
          onClear={() => setChosen({})}
        />
      )}

      <div className="dict-lib-head">
        <h2 className="dict-lib-title">Bài nghe chép chính tả</h2>
        {/*
          Heading and count in one live region: changing the search silently
          rewrote the count, and a screen reader heard a number with no list
          attached to it.
        */}
        <p className="dict-lib-count" role="status">
          {countLine()}
        </p>
      </div>

      {state.kind === 'anonymous' && (
        <div className="dict-gate">
          <h3>Đăng nhập để mở kho bài nghe</h3>
          <p>
            Câu nghe và câu đúng đều nằm trên máy chủ — trình duyệt không giữ bản nào, nên phần chấm
            từng từ chỉ chạy được khi có tài khoản. Tạo tài khoản mất chưa tới một phút.
          </p>
          <div className="dict-gate-actions">
            <Link className="btn btn-primary" to={Paths.signUp}>
              Tạo tài khoản miễn phí <span aria-hidden="true">→</span>
            </Link>
            <Link className="btn btn-secondary" to={Paths.signIn}>
              Tôi đã có tài khoản
            </Link>
          </div>
        </div>
      )}

      {state.kind === 'loading' && <p className="dict-note">{t('exam.loading')}</p>}

      {state.kind === 'failed' && (
        /* One sentence and a button. The reader's only other recourse is a
           browser reload, and `load` is a `useCallback` one line away. */
        <div className="dict-gate" role="alert">
          <h3>{t('common.notConnected')}</h3>
          <p>{t('exam.loadFailed')}</p>
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
          </div>
        </div>
      )}

      {state.kind === 'ready' &&
        (slice.length === 0 ? (
          /*
            Two empty states, and the difference matters. Nothing in the
            catalogue is not the reader's doing and no filter will fix it;
            nothing matched is one press from being undone, and the press has
            to be here rather than in a control they have to go find.
          */
          <div className="dict-gate">
            <h3>{filtered ? 'Không tìm thấy bài nghe phù hợp' : t('dict.emptyTitle')}</h3>
            <p>{filtered ? 'Thử một từ khoá khác, hoặc bỏ bớt bộ lọc.' : t('dict.emptyBody')}</p>
            <div className="dict-gate-actions">
              {filtered ? (
                <button type="button" className="btn btn-primary" onClick={clearAll}>
                  Xoá bộ lọc và tìm kiếm
                </button>
              ) : (
                <>
                  <Link className="btn btn-secondary" to={Paths.practice}>
                    Luyện 4 kỹ năng
                  </Link>
                  <Link className="btn btn-secondary" to={Paths.documents}>
                    Vào kho tài liệu
                  </Link>
                </>
              )}
            </div>
          </div>
        ) : (
          <>
            <ul className="dict-grid">
              {slice.map((item) => (
                <DictationCard key={item.id} item={item} />
              ))}
            </ul>

            <Pagination page={safePage} pages={pages} onGo={setPage} />
          </>
        ))}
    </div>
  );

  function countLine(): string {
    if (state.kind === 'anonymous') return 'Đăng nhập để xem danh sách bài nghe.';
    if (state.kind === 'loading') return t('exam.loading');
    if (state.kind === 'failed') return t('exam.loadFailed');

    // "Nothing here" and "nothing matched" are different sentences, and saying
    // the wrong one implies a filter the reader never set.
    if (items.length === 0) return t('dict.emptyBody');
    if (shown.length === 0) return 'Không có bài nào khớp với tìm kiếm hiện tại.';

    const total = items.length;
    return shown.length === total
      ? `${total} bài · nghe lại không giới hạn, không tính giờ`
      : `${shown.length} / ${total} bài khớp`;
  }
}

function SearchIcon() {
  return (
    <svg
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.9"
      strokeLinecap="round"
      aria-hidden="true"
      focusable="false"
    >
      <circle cx="11" cy="11" r="6.5" />
      <path d="m16 16 4.5 4.5" />
    </svg>
  );
}
