import { useCallback, useEffect, useRef, useState } from 'react';
import type { ComponentType } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import type { StringKey } from '../../i18n/index.js';
import { Paths } from '../../routes/paths.js';
import {
  listExams,
  listMySittings,
  type ExamModule,
  type SittingSummary,
} from '../exam/examApi.js';
import { InProgressPanel, RecentSittings, StatStrip } from './DashboardState.js';
import { ArticleIcon, DocumentIcon } from '../landing/MenuIcons.js';
import {
  DictationIcon,
  FullTestIcon,
  ListeningIcon,
  ReadingIcon,
  SpeakingIcon,
  WritingIcon,
} from './StudentIcons.js';
import '../../styles/dashboard.css';

/**
 * Student home — a dashboard, and only a dashboard.
 *
 * <b>Rebuilt 21/08/2026 at the owner's direction: no side rail, and nothing
 * belonging to the profile.</b> The first version borrowed the reference's
 * collapsible rail, and two of its five entries — "Hồ sơ học sinh" and "Theo
 * dõi" — were links into `/profile`. That put account navigation on a page
 * whose job is the next piece of work, and gave the learner two competing
 * navigations: the product header above and a second one beside it.
 *
 * The rail is gone rather than emptied. Its remaining entries were in-page
 * anchors on a page short enough to see whole, which is navigation for the
 * sake of having some.
 *
 * <b>What decided the contents.</b> Two capabilities are confirmed and in the
 * first release: the four-skill examination (`E-1`, `E-2`, `E-11`…`E-13`) and
 * AI Chat (`F-2`). Everything here serves one of those two or says honestly
 * that it is not ready.
 *
 * <b>Full Test and Single Skill are laid out as two separate things</b>,
 * because they are two separate things (`E-11`). Full Test runs
 * Reading → Listening → Writing → Speaking inside one session and advances by
 * itself (`E-12`); Single Skill never auto-advances and ends on "new test"
 * (`E-13`).
 *
 * <b>Every card carries how it is scored, and the two kinds do not look
 * alike.</b> Reading and Listening come from the answer key (`A-11`); Writing
 * and Speaking are AI-scored (`A-13a`, `F-1`) and are marked *tham khảo* on a
 * dashed outline — product law L4, legible in greyscale rather than by colour.
 *
 * <b>Reordered 22/08/2026 at the owner's direction — it read as a menu, not as
 * an overview.</b> It was one: the two panels that would have carried state,
 * "bài đang làm dở" and "kết quả gần đây", were hard-coded empty states written
 * when there was no exam API to ask. The API arrived, the panels never changed,
 * and a learner with eight sittings was told on every visit that they had done
 * nothing. `GET /api/v1/sessions` was added for this screen; the repository
 * query behind it had existed, unused, since the engine was built.
 *
 * <b>State first, actions second.</b> An overview answers *where was I* before
 * *what can I do*. So: the sitting in progress, then three counts, then recent
 * results — and only then the launcher that used to be the whole page.
 *
 * <b>Nothing invented, still.</b> No streak, no XP, no target-band progress
 * bar, no token chip — a streak needs a rule nobody wrote, a progress bar needs
 * a target nobody asked the learner for, and `T-1` is confirmed while amounts
 * (`B-5b`) and selling (`B-4`) are not. Every number on this page is a count of
 * something that happened.
 */

interface Skill {
  id: string;
  icon: ComponentType<{ size?: number }>;
  name: string;
  desc: StringKey;
  /** Answer key vs AI — the two are visually distinct on purpose. See L4. */
  scoring: 'key' | 'ai';
}

/**
 * Skill names stay in English on purpose: they are the names of the IELTS
 * modules, and every learner who has looked at a test already knows them under
 * those names. The interface language around them is still translated.
 */
const SKILLS: Skill[] = [
  { id: 'reading', icon: ReadingIcon, name: 'Reading', desc: 'dash.skill.reading', scoring: 'key' },
  {
    id: 'listening',
    icon: ListeningIcon,
    name: 'Listening',
    desc: 'dash.skill.listening',
    scoring: 'key',
  },
  { id: 'writing', icon: WritingIcon, name: 'Writing', desc: 'dash.skill.writing', scoring: 'ai' },
  {
    id: 'speaking',
    icon: SpeakingIcon,
    name: 'Speaking',
    desc: 'dash.skill.speaking',
    scoring: 'ai',
  },
];

/**
 * The three modules that are not the four-skill exam.
 *
 * <b>They were labelled "sắp mở" and all three have shipped.</b> Dictation runs
 * end to end, and documents and articles became pages of their own on 21/08.
 * A "coming soon" chip on a working feature is worse than no chip: it tells the
 * learner not to click the thing that works.
 */
const OTHER: {
  id: string;
  icon: ComponentType<{ size?: number }>;
  label: StringKey;
  desc: StringKey;
  to: string;
}[] = [
  {
    id: 'dictation',
    icon: DictationIcon,
    label: 'dash.more.dictation',
    desc: 'dash.more.dictationBody',
    to: Paths.dictation,
  },
  {
    id: 'documents',
    icon: DocumentIcon,
    label: 'dash.more.documents',
    desc: 'dash.more.documentsBody',
    to: Paths.documents,
  },
  {
    id: 'articles',
    icon: ArticleIcon,
    label: 'dash.more.articles',
    desc: 'dash.more.articlesBody',
    to: Paths.articles,
  },
];

export function StudentDashboardPage() {
  const { user, accessToken } = useAuth();
  const { t } = useI18n();

  /*
   * Which skills actually have an exam behind them.
   *
   * The alternative was a hard-coded "chưa có đề" on every card, which was
   * honest yesterday and became a lie the moment the catalogue had content.
   * Asking the catalogue means the card can never disagree with the library it
   * links to — and a failed load leaves the set empty, which degrades to the
   * old honest state rather than to a card that leads nowhere.
   */
  const [available, setAvailable] = useState<Set<ExamModule>>(new Set());
  const [sittings, setSittings] = useState<SittingSummary[] | null>(null);
  const alive = useRef(true);
  /*
   * Set true on the way IN, not just false on the way out.
   *
   * StrictMode double-invokes a mount effect: run, clean up, run again. The
   * cleanup flips this to false and the second run never flipped it back, so
   * every later `setState` guarded by it was skipped and the screen sat on
   * "Đang tải…" forever — against an API that had already answered 200.
   */
  useEffect(() => {
    alive.current = true;
    return () => void (alive.current = false);
  }, []);

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const { exams } = await listExams(accessToken);
      if (!alive.current) return;
      setAvailable(new Set(exams.flatMap((e) => e.modules.map((m) => m.module))));
    } catch {
      // Left empty on purpose. See above.
    }

    /*
     * Fetched separately, and a failure here does not take the catalogue with
     * it. The two answer different questions — what can I start, and what was
     * I doing — and a learner whose history fails to load should still be able
     * to start something.
     */
    try {
      const { sittings: mine } = await listMySittings(accessToken);
      if (alive.current) setSittings(mine);
    } catch {
      if (alive.current) setSittings([]);
    }
  }, [accessToken]);

  useEffect(() => void load(), [load]);

  /*
   * The one to return to: the most recent sitting still open. `ListForUserAsync`
   * sorts newest first, so this is a find rather than a sort.
   */
  const open = sittings?.find((sitting) => sitting.status === 'inprogress') ?? null;
  const anyExam = available.size > 0;
  const fullTestReady = SKILLS.every((skill) => available.has(skill.id as ExamModule));
  if (user === null) return null;

  return (
    <div className="dash">
      <main className="dash-main">
        <header className="dash-head">
          <p className="dash-eyebrow">{t('dash.eyebrow')}</p>
          <h1 className="dash-greeting">{t('home.greeting', { name: user.displayName })}</h1>
          <p className="dash-lead">{t('dash.lead')}</p>
        </header>

        {!user.emailVerified && (
          <div className="dash-alert" role="status">
            <strong>{t('home.unverifiedTitle')}. </strong>
            {t('home.unverifiedBody')}
          </div>
        )}

        {/*
          ── Where the learner was ──────────────────────────────────────
          First, and largest. Everything below is a way to start something
          new; this is the only block that knows what they already began.
          It renders nothing at all until the history has loaded, rather than
          flashing "nothing in progress" at someone who has something in
          progress.
        */}
        {sittings !== null && <InProgressPanel sitting={open} />}
        {sittings !== null && sittings.length > 0 && <StatStrip sittings={sittings} />}

        {sittings !== null && (
          <section className="dash-block" id="results">
            <div className="dash-block-head">
              <h2>{t('dash.recent.title')}</h2>
            </div>
            <RecentSittings sittings={sittings.slice(0, 5)} />
          </section>
        )}

        {/*
          One explanation, said once. The cards below carry a short status chip
          each; repeating the full sentence five times would turn the page into
          an apology.
        */}
        <p className="dash-notice">{anyExam ? t('dash.noticeSome') : t('dash.notice')}</p>

        {/* ── Luyện tập ──────────────────────────────────────────────── */}
        <section className="dash-block" id="practice">
          <div className="dash-block-head">
            <h2>{t('dash.practice.title')}</h2>
            <p>{t('dash.practice.lead')}</p>
          </div>

          {/* Full Test — one session, four skills, advances by itself. */}
          <article className="dash-card dash-full" aria-labelledby="dash-full-title">
            <span className="dash-card-icon is-green" aria-hidden="true">
              <FullTestIcon size={24} />
            </span>

            <div className="dash-full-body">
              <div className="dash-full-top">
                <h3 id="dash-full-title">{t('dash.full.title')}</h3>
                {fullTestReady ? (
                  <Link className="dash-go" to={Paths.practice}>
                    {t('dash.open')}
                  </Link>
                ) : (
                  <span className="dash-chip">{t('dash.status.noExam')}</span>
                )}
              </div>
              <p>{t('dash.full.body')}</p>
              {/* The order is the requirement (E-12), so it is an ordered list
                and not four words in a sentence. */}
              <ol className="dash-order">
                <li>Reading</li>
                <li>Listening</li>
                <li>Writing</li>
                <li>Speaking</li>
              </ol>
            </div>
          </article>

          <h3 className="dash-subhead">{t('dash.skills.title')}</h3>
          <p className="dash-subnote">{t('dash.skills.lead')}</p>

          <div className="dash-skill-grid">
            {SKILLS.map((skill) => {
              const Icon = skill.icon;
              return (
                <article
                  key={skill.id}
                  className="dash-card dash-skill"
                  aria-labelledby={`skill-${skill.id}`}
                >
                  <span className="dash-card-icon" aria-hidden="true">
                    <Icon />
                  </span>
                  <h4 id={`skill-${skill.id}`}>{skill.name}</h4>
                  <p>{t(skill.desc)}</p>
                  <div className="dash-skill-foot">
                    <span className={skill.scoring === 'ai' ? 'dash-tag dash-tag-ai' : 'dash-tag'}>
                      {skill.scoring === 'ai' ? t('dash.scoring.ai') : t('dash.scoring.key')}
                    </span>
                    {available.has(skill.id as ExamModule) ? (
                      <Link className="dash-go" to={Paths.practice}>
                        {t('dash.open')}
                      </Link>
                    ) : (
                      <span className="dash-chip">{t('dash.status.noExam')}</span>
                    )}
                  </div>
                </article>
              );
            })}
          </div>
        </section>

        {/* ── Phần khác ──────────────────────────────────────────────── */}
        <section className="dash-block" id="coming">
          <div className="dash-block-head">
            <h2>{t('dash.other.title')}</h2>
          </div>
          <ul className="dash-more">
            {OTHER.map((item) => {
              const Icon = item.icon;
              return (
                <li key={item.id} className="dash-more-row">
                  <span className="dash-more-icon" aria-hidden="true">
                    <Icon />
                  </span>
                  <span className="dash-more-text">
                    <strong>{t(item.label)}</strong>
                    <span>{t(item.desc)}</span>
                  </span>
                  <Link className="dash-go" to={item.to}>
                    {t('dash.open')}
                  </Link>
                </li>
              );
            })}
          </ul>
        </section>
      </main>
    </div>
  );
}
