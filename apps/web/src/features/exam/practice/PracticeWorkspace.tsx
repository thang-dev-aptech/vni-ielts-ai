import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { ApiError } from '../../../lib/api.js';
import { useI18n } from '../../../i18n/index.js';
import { Paths } from '../../../routes/paths.js';
import { useAuth } from '../../auth/AuthContext.js';
import { listExams, startSession, type ExamCatalogueItem, type ExamModule } from '../examApi.js';
import { SKILLS, SKILL_ORDER } from '../skills.js';
import { FilterPanel } from './FilterPanel.js';
import { Pagination } from '../../chrome/Pagination.js';
import { PracticeCard } from './PracticeCard.js';
import { SkillSelector } from './SkillSelector.js';
import {
  buildFacets,
  matchesFacets,
  toFullItems,
  toSingleItems,
  type PracticeItem,
  type PracticeMode,
} from './practiceCatalogue.js';

const PER_PAGE = 6;

type State =
  | { kind: 'anonymous' }
  | { kind: 'loading' }
  | { kind: 'ready'; exams: ExamCatalogueItem[] }
  | { kind: 'failed' };

/**
 * The practice workspace — the part of `/practice` the page exists for.
 *
 * <b>Replaces `PracticeExamPicker`, which was a list and not a workspace.</b>
 * That component rendered four inert skill labels above a flat list of every
 * exam, with no way to narrow it and no way to page through it. It worked
 * while the catalogue held three papers; it is the wrong shape for a catalogue
 * that grows, which is the one thing a catalogue is certain to do.
 *
 * <b>Mode is chosen before an exam is, and it changes what a card means.</b>
 * `E-11`…`E-13`: a full test runs all four modules in one sitting and advances
 * itself; single-skill practice ends when the skill does. They are two offers,
 * not a filter over one list, so they are two derivations and the grid never
 * mixes them. Choosing a skill therefore implies single-skill practice and
 * switches the mode — a skill choice inside "thi thử full" would be a control
 * that quietly does nothing.
 *
 * <b>Skill and mode live in the URL.</b> `/practice?skill=writing` is a link
 * someone can send, and it is what the four cards in "Bạn có thể luyện gì"
 * further down the page point at. The facet choices deliberately do not: they
 * are a scratch narrowing, and putting six of them in the address bar makes
 * every shared link carry someone else's filter state.
 *
 * <b>The block gates itself.</b> `/practice` is public — it is the page that
 * argues for the product — but the catalogue needs a token. So this renders
 * the sign-in card for a visitor and the workspace for a learner, and the
 * route never has to choose between being reachable and being useful.
 */
export function PracticeWorkspace() {
  const { accessToken, status } = useAuth();
  const { t } = useI18n();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();

  const [state, setState] = useState<State>({ kind: 'loading' });
  const [chosen, setChosen] = useState<Record<string, string[]>>({});
  const [page, setPage] = useState(1);
  const [starting, setStarting] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [filtersOpen, setFiltersOpen] = useState(false);
  const filtersId = useId();

  const skill = readSkill(params.get('skill'));
  const mode: PracticeMode = params.get('mode') === 'full' ? 'full' : 'single';

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
      const { exams } = await listExams(accessToken);
      if (alive.current) setState({ kind: 'ready', exams });
    } catch {
      if (alive.current) setState({ kind: 'failed' });
    }
  }, [accessToken, status]);

  useEffect(() => void load(), [load]);

  const exams = state.kind === 'ready' ? state.exams : [];

  /* ── Derivations ─────────────────────────────────────────────────────── */

  const single = useMemo(() => toSingleItems(exams), [exams]);
  const full = useMemo(() => toFullItems(exams), [exams]);

  /** Papers per skill, for the selector. Null while there is no catalogue. */
  const counts = useMemo(() => {
    if (state.kind !== 'ready') return null;
    const tally = { reading: 0, listening: 0, writing: 0, speaking: 0 } as Record<
      ExamModule,
      number
    >;
    for (const item of single) if (item.module) tally[item.module] += 1;
    return tally;
  }, [single, state.kind]);

  /** Everything in the current mode and skill, before the facets narrow it. */
  const scoped = useMemo(
    () => (mode === 'full' ? full.items : single.filter((item) => item.module === skill)),
    [mode, full.items, single, skill],
  );

  // Counts are measured against the *other* groups' choices, so a facet can
  // never advertise results it will not return. → `buildFacets`
  const facets = useMemo(() => buildFacets(scoped, chosen), [scoped, chosen]);
  const shown = useMemo(
    () => scoped.filter((item) => matchesFacets(item, chosen)),
    [scoped, chosen],
  );

  const activeFilters = Object.values(chosen).reduce((sum, values) => sum + values.length, 0);
  const pages = Math.max(1, Math.ceil(shown.length / PER_PAGE));
  const safePage = Math.min(page, pages);
  const slice = shown.slice((safePage - 1) * PER_PAGE, safePage * PER_PAGE);

  // Narrowing the list has to put the reader back on page one, or a filter
  // that leaves four results while they are on page three shows an empty grid
  // and reads as "no results".
  useEffect(() => setPage(1), [skill, mode, chosen]);

  /* ── Actions ─────────────────────────────────────────────────────────── */

  /*
   * <b>Merged into the existing query, not written over it.</b> `setParams`
   * with a plain object replaces the whole string: `/practice?utm_source=fb`
   * became `/practice?skill=listening&mode=single` on the first arrow-key
   * press, and every campaign or referral parameter died at the first
   * interaction.
   */
  function writeUrl(next: { skill?: ExamModule; mode?: PracticeMode }) {
    const merged = new URLSearchParams(params);
    if (next.skill !== undefined) merged.set('skill', next.skill);
    if (next.mode !== undefined) merged.set('mode', next.mode);
    setParams(merged, { replace: true });
    setChosen({});
  }

  function choose(next: ExamModule) {
    // A skill choice means single-skill practice. `E-13`.
    writeUrl({ skill: next, mode: 'single' });
  }

  function switchMode(next: PracticeMode) {
    writeUrl({ mode: next });
  }

  function toggle(facetId: string, value: string) {
    setChosen((was) => {
      const values = was[facetId] ?? [];
      const next = values.includes(value)
        ? values.filter((one) => one !== value)
        : [...values, value];
      return { ...was, [facetId]: next };
    });
  }

  async function start(item: PracticeItem) {
    if (accessToken === null) return;

    setStarting(item.key);
    setError(null);

    try {
      // Generated once for this press. Regenerating it on a retry defeats the
      // whole mechanism, and starting an exam twice is not a harmless
      // duplicate once entitlement lands.
      const key = crypto.randomUUID();

      const session = await startSession(
        accessToken,
        {
          examVersionId: item.examVersionId,
          mode: item.mode,
          ...(item.mode === 'single' && item.module ? { module: item.module } : {}),
        },
        key,
      );

      // Straight into the paper. Not the dashboard, not a confirmation step —
      // the learner pressed "bắt đầu" and the next thing they should see is
      // the first question.
      navigate(Paths.examSession(session.sessionId));
    } catch (caught) {
      if (!alive.current) return;
      setError(caught instanceof ApiError ? t('exam.startFailed') : t('common.notConnected'));
      setStarting(null);
    }
  }

  /* ── Render ──────────────────────────────────────────────────────────── */

  /*
   * The selector renders in every state, including signed out and failed.
   *
   * It is what the section is about, and it does not depend on the catalogue.
   * A visitor deciding whether to register needs to see the four skills before
   * anything else has loaded — and someone whose connection dropped should not
   * lose the page's primary control along with the list.
   */
  /*
   * <b>Nothing is selected in full mode, and that is the point.</b> The first
   * version kept the chosen skill tinted, ticked, `aria-checked` and
   * auto-scrolled into view while the grid showed full tests — a control
   * announcing a current value that governed nothing on screen. The counts go
   * with it: "8 bài luyện" describes single-skill practice, and printing it
   * over a list of full tests is a number about a different list.
   *
   * Pressing a card still works and still means "luyện kỹ năng này", which is
   * what switches the mode back.
   */
  const selector = (
    <SkillSelector
      selected={mode === 'full' ? null : skill}
      counts={mode === 'full' ? null : counts}
      onSelect={choose}
    />
  );

  const heading = mode === 'full' ? t('exam.modeFull') : `Luyện ${SKILLS[skill].name}`;

  /*
   * <b>"Nothing matches" and "nothing exists" are different sentences.</b> The
   * first version said *Chưa có bài luyện nào khớp* — "nothing matches" — over
   * an empty catalogue, implying a filter the reader had never set, 100px above
   * a box that correctly said there were no papers at all.
   */
  function countLine(): string {
    if (state.kind !== 'ready') {
      return mode === 'full' ? t('exam.modeFullHint') : t('exam.modeSingleHint');
    }
    if (scoped.length === 0) {
      return mode === 'full'
        ? 'Chưa có đề nào thi full được'
        : `Chưa có đề ${SKILLS[skill].name} nào`;
    }
    if (shown.length === 0) return 'Không có bài nào khớp bộ lọc hiện tại';

    const how = mode === 'full' ? 'đủ bốn kỹ năng trong một phiên' : SKILLS[skill].marking;
    return `${shown.length} bài luyện · ${how}`;
  }

  return (
    <div className="work">
      {selector}

      <div className="work-bar">
        {/*
          <b>The heading and the count are one live region.</b> Only the count
          carried `role="status"`, so changing skill silently rewrote the `<h2>`
          and announced "6 bài luyện · Chấm theo đáp án" — a number with no
          shelf attached to it.
        */}
        <div className="work-bar-copy" role="status">
          <h2 className="work-title">{heading}</h2>
          <p className="work-count">{countLine()}</p>
        </div>

        {/*
          The mode choice comes before the exam choice, because it changes what
          "bắt đầu" means. → `E-11`

          <b>Not a tablist.</b> It was `role="tablist"` with two `role="tab"`
          children and no `tabpanel` anywhere on the page — so a screen reader
          announced "tab, 1 of 2, selected" and sent the reader looking for a
          panel that does not exist, and the APG contract also promises
          arrow-key navigation that was never implemented. Two pressed-state
          buttons in a labelled group say exactly what this is.
        */}
        <div className="work-modes" role="group" aria-label={t('exam.modeLabel')}>
          <button
            type="button"
            aria-pressed={mode === 'single'}
            className={`work-mode${mode === 'single' ? ' is-active' : ''}`}
            onClick={() => switchMode('single')}
          >
            {/* "Luyện từng kỹ năng" wraps to two lines inside a 144px pill at
                390px, and a wrapped segmented control is a defect the brief
                names. The narrow width gets a shorter label rather than a
                smaller one — the 14px floor holds either way. */}
            <span className="work-mode-long">{t('exam.modeSingle')}</span>
            <span className="work-mode-short">Từng kỹ năng</span>
          </button>
          <button
            type="button"
            aria-pressed={mode === 'full'}
            className={`work-mode${mode === 'full' ? ' is-active' : ''}`}
            onClick={() => switchMode('full')}
          >
            {t('exam.modeFull')}
          </button>
        </div>
      </div>

      {state.kind === 'anonymous' && (
        <div className="work-gate">
          <h3>Đăng nhập để mở kho đề</h3>
          <p>
            Bài làm cần một tài khoản để lưu — đồng hồ, câu trả lời và điểm đều nằm trên máy chủ chứ
            không nằm trong trình duyệt. Tạo tài khoản mất chưa tới một phút.
          </p>
          <div className="work-gate-actions">
            <Link className="btn btn-primary" to={Paths.signUp}>
              Tạo tài khoản miễn phí <span aria-hidden="true">→</span>
            </Link>
            <Link className="btn btn-secondary" to={Paths.signIn}>
              Tôi đã có tài khoản
            </Link>
          </div>
        </div>
      )}

      {state.kind === 'loading' && <p className="work-note">{t('exam.loading')}</p>}

      {state.kind === 'failed' && (
        /*
          <b>One sentence and a button, not two sentences and nothing.</b> This
          block used to print "Không kết nối được tới máy chủ. Kiểm tra mạng rồi
          thử lại." as its heading and "Không tải được danh sách đề. Kiểm tra
          kết nối rồi thử lại." underneath — the same instruction twice, with no
          control. The reader's only recourse was a browser reload, on a page
          whose loader is already a `useCallback` sitting one line away.

          `role="alert"` because a list that failed to arrive is not something a
          screen-reader user finds by scrolling.
        */
        <div className="work-gate" role="alert">
          <h3>{t('common.notConnected')}</h3>
          <p>{t('exam.loadFailed')}</p>
          <div className="work-gate-actions">
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

      {state.kind === 'ready' && (
        <>
          {error !== null && (
            <p className="work-error" role="alert">
              {error}
            </p>
          )}

          <div className="work-split">
            {/* Below the sidebar's breakpoint this collapses behind a button
                rather than pushing the grid a screen and a half down. */}
            {/*
              <b>Three things were wrong with this control.</b> `inline-flex` on
              a grid item in a `1fr` column stretches, so it rendered as a
              936px-wide white rounded rectangle with six centred characters in
              it — an empty search field, not a button. `aria-expanded` had no
              visual counterpart, because neither the label nor any caret
              changed. And the active-filter signal was an 8px green dot —
              colour alone — whose `aria-label` concatenated into the button's
              name as "Bộ lọc đang có bộ lọc", which is not a sentence and does
              not say how many.
            */}
            <button
              type="button"
              className="work-filter-toggle"
              aria-expanded={filtersOpen}
              aria-controls={filtersId}
              onClick={() => setFiltersOpen((was) => !was)}
            >
              Bộ lọc{activeFilters > 0 ? ` (${activeFilters})` : ''}
              <span className="work-filter-caret" aria-hidden="true">
                ⌄
              </span>
            </button>

            <aside className={`work-side${filtersOpen ? ' is-open' : ''}`}>
              <FilterPanel
                id={filtersId}
                facets={facets}
                chosen={chosen}
                onToggle={toggle}
                onClear={() => setChosen({})}
              />
            </aside>

            <div className="work-main">
              {mode === 'full' && full.incomplete > 0 && (
                <p className="work-note">
                  {full.incomplete} đề chưa đủ bốn kỹ năng nên không thi full được. Chúng vẫn luyện
                  được ở chế độ từng kỹ năng.
                </p>
              )}

              {slice.length === 0 ? (
                /*
                  Two different empty states, and the difference matters.

                  <b>Nothing in the catalogue</b> is not the reader's doing and
                  no filter will fix it, so it offers the two modules that do
                  have content — both of which were otherwise only linked 3000px
                  further down the page.

                  <b>Nothing matched</b> is the reader's doing and is one press
                  from being undone. The press has to be *here*: below 1080px
                  the sidebar is collapsed behind a toggle, so "thử bỏ bớt một
                  bộ lọc" was an instruction pointing at a control that was not
                  on screen.
                */
                <div className="work-gate">
                  <h3>{scoped.length === 0 ? t('exam.emptyTitle') : 'Không có bài nào khớp'}</h3>
                  <p>
                    {scoped.length === 0
                      ? t('exam.emptyBody')
                      : 'Bỏ bớt một bộ lọc, hoặc chọn kỹ năng khác ở hàng trên.'}
                  </p>

                  <div className="work-gate-actions">
                    {scoped.length === 0 ? (
                      <>
                        <Link className="btn btn-secondary" to={Paths.dictation}>
                          Luyện nghe chép chính tả
                        </Link>
                        <Link className="btn btn-secondary" to={Paths.documents}>
                          Vào kho tài liệu
                        </Link>
                      </>
                    ) : (
                      <button
                        type="button"
                        className="btn btn-primary"
                        onClick={() => setChosen({})}
                      >
                        Xoá bộ lọc
                      </button>
                    )}
                  </div>
                </div>
              ) : (
                <ul className="prac-grid">
                  {slice.map((item) => (
                    <PracticeCard
                      key={item.key}
                      item={item}
                      busy={starting === item.key}
                      onStart={(one) => void start(one)}
                    />
                  ))}
                </ul>
              )}

              <Pagination page={safePage} pages={pages} onGo={setPage} />
            </div>
          </div>
        </>
      )}
    </div>
  );
}

/** A skill from the address bar, defaulting to the first of the four. */
function readSkill(raw: string | null): ExamModule {
  return SKILL_ORDER.includes(raw as ExamModule) ? (raw as ExamModule) : 'reading';
}
