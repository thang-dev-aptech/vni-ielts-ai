import { useCallback, useEffect, useState } from 'react';
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
import { DictationIcon, FullTestIcon } from './StudentIcons.js';
import { SKILLS, SKILL_ORDER } from '../exam/skills.js';
import '../../styles/dashboard.css';
import { usePageTitle } from '../../routes/usePageTitle.js';
import { useAlive } from '../../lib/useAlive.js';

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

/**
 * The four skills, from the one place that defines them.
 *
 * <b>This file used to carry a second, private list.</b> Same four modules,
 * different fields, its own `scoring: 'key' | 'ai'` flag — and `skills.ts`
 * documents that the `writing || speaking` conditional "had been written out
 * at three call sites, and a fourth would have been written the next time a
 * screen needed it". This was the fourth. `DashboardState`, one file over,
 * already imported the shared one.
 *
 * Only the per-card description stays local, because it is copy written for
 * this screen rather than a property of the skill.
 *
 * Skill names stay in English on purpose: they are the names of the IELTS
 * modules, and every learner who has looked at a test already knows them under
 * those names. The interface language around them is still translated.
 */
const SKILL_BLURB: Record<ExamModule, StringKey> = {
  reading: 'dash.skill.reading',
  listening: 'dash.skill.listening',
  writing: 'dash.skill.writing',
  speaking: 'dash.skill.speaking',
};

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
  usePageTitle(t('title.dashboard'));

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
  const [fullTestReady, setFullTestReady] = useState(false);
  const [sittings, setSittings] = useState<SittingSummary[] | null>(null);
  const alive = useAlive();

  const load = useCallback(async () => {
    if (accessToken === null) return;
    try {
      const { exams } = await listExams(accessToken);
      if (!alive.current) return;
      setAvailable(new Set(exams.flatMap((e) => e.modules.map((m) => m.module))));

      /*
       * <b>A Full Test is one paper with four skills in it, not four papers
       * that add up to four skills.</b>
       *
       * The availability set above is a union across the whole catalogue, which
       * answers "can I practise Listening somewhere" — the right question for
       * the per-skill cards. It is the wrong question for Full Test: four
       * single-skill versions would satisfy it while no sitting exists that
       * runs Reading → Listening → Writing → Speaking in one session, and the
       * card would offer a test the engine cannot start. So this asks each
       * version on its own. → `E-11`
       */
      setFullTestReady(
        exams.some((e) => SKILL_ORDER.every((id) => e.modules.some((m) => m.module === id))),
      );
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
  if (user === null) return null;

  return (
    <div className="dash">
      <main className="dash-main">
        <header className="dash-head">
          <p className="dash-eyebrow">{t('dash.eyebrow')}</p>
          <h1 className="dash-greeting">{t('home.greeting', { name: user.displayName })}</h1>
          <p className="dash-lead">{t('dash.lead')}</p>
        </header>

        {/*
          <b>A notice, not a gate — and now it says where to go.</b>

          Registering signs the learner in (`[QUYẾT ĐỊNH]` chủ sản phẩm,
          27/08/2026), so an unverified account is the ordinary state of a
          brand-new one rather than a problem. This used to say some features
          would unlock on verifying, which described a restriction that exists
          nowhere in the product — and what an unverified account may not do is
          still the owner's to decide (`M-45`). It now says what is true and
          links to the one place the learner can act.
        */}
        {!user.emailVerified && (
          <div className="dash-alert" role="status">
            <strong>{t('home.unverifiedTitle')}. </strong>
            {t('home.unverifiedBody')} <Link to={Paths.profile}>{t('home.unverifiedAction')}</Link>
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
        {/*
          ── Two columns, from 1180px up ────────────────────────────────

          Everything on this page used to be one 1080px column, so a card
          holding an exam title and a button was 1080px wide with roughly 800
          of them empty, and the page ran to 2400px for what is really "one
          thing in progress, five sittings, four skills, three modules". A
          learner had to scroll past the whole catalogue to see whether their
          last paper had been marked.

          The split follows what the two halves are for. The left column is
          what you can start — it is browsed, so it wants width. The right is
          what you have done — it is checked, so it wants to be visible while
          you browse, which is why it sticks.
        */}
        <div className="dash-columns">
          <div className="dash-col-main">
            {/*
              <b>In the column, not across the page.</b> Spanning the full
              width put a title and a button at opposite ends of 1580px with
              nothing between them — the panel looked emptier the wider the
              screen got. Here it is the first thing in "what you could be
              doing", directly above the catalogue, which is also the order the
              two are read in: carry on with this, or start something new.
            */}
            {sittings !== null && <InProgressPanel sitting={open} />}

            {/*
              One explanation, said once. The cards below carry a short status
              chip each; repeating the full sentence five times would turn the
              page into an apology.
            */}
            <p className="dash-notice">{anyExam ? t('dash.noticeSome') : t('dash.notice')}</p>

            {/* ── Luyện tập ──────────────────────────────────────────── */}
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
                      /*
                       * <b>The mode travels in the link.</b> `/practice` alone
                       * lands on its default, which is Single Skill Reading —
                       * so the Full Test card opened a single Reading paper,
                       * and the per-skill cards below all opened Reading too,
                       * whichever one was pressed. The workspace already reads
                       * `mode` and `skill` from the query; nothing was passing
                       * them. → CLAUDE.md rule 10
                       */
                      <Link className="dash-go" to={`${Paths.practice}?mode=full`}>
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
                {SKILL_ORDER.map((id) => {
                  const skill = SKILLS[id];
                  const Icon = skill.icon;
                  // `marking` is the skill's own fact, so the "is this AI?" test
                  // lives with it rather than being restated per screen.
                  const byAi = skill.marking.startsWith('AI');

                  return (
                    <article
                      key={id}
                      className="dash-card dash-skill"
                      aria-labelledby={`skill-${id}`}
                    >
                      {/*
                        The skill's own colour, from `skills.ts`.

                        These four cards used one generic blue for all of them,
                        while the exam runner's header, the practice selector
                        and the results rows each paint Reading blue, Listening
                        orange, Writing purple and Speaking pink. A learner
                        recognises those four before they read the words, and
                        the dashboard was the one screen that made them read.
                      */}
                      <span
                        className="dash-card-icon"
                        style={{ background: skill.tint, color: skill.ink }}
                        aria-hidden="true"
                      >
                        <Icon />
                      </span>
                      <h4 id={`skill-${id}`}>{skill.name}</h4>
                      <p>{t(SKILL_BLURB[id])}</p>
                      <div className="dash-skill-foot">
                        <span className={byAi ? 'dash-tag dash-tag-ai' : 'dash-tag'}>
                          {byAi ? t('dash.scoring.ai') : t('dash.scoring.key')}
                        </span>
                        {available.has(id) ? (
                          // The skill and the mode both travel. Without them
                          // every one of these five cards opened Reading.
                          <Link
                            className="dash-go"
                            to={`${Paths.practice}?mode=single&skill=${id}`}
                          >
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

            {/* ── Phần khác ──────────────────────────────────────────── */}
            <section className="dash-block" id="coming" tabIndex={-1}>
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
          </div>

          {/*
            <b>Sticky, because it answers a question you have while browsing.</b>
            "How did I do" and "what shall I do next" are asked in the same
            breath, and a progress panel that scrolls away the moment you start
            reading the catalogue answers it only if you remember to scroll
            back.
          */}
          <aside className="dash-col-side" aria-label={t('dash.progressLabel')}>
            {sittings !== null && sittings.length > 0 && <StatStrip sittings={sittings} />}

            {sittings !== null && (
              <section className="dash-block" id="results" tabIndex={-1}>
                <div className="dash-block-head">
                  <h2>{t('dash.recent.title')}</h2>
                </div>
                <RecentSittings sittings={sittings.slice(0, 5)} />
              </section>
            )}
          </aside>
        </div>
      </main>
    </div>
  );
}
