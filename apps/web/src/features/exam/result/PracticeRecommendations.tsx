import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useI18n } from '../../../i18n/index.js';
import { useAlive } from '../../../lib/useAlive.js';
import { Paths } from '../../../routes/paths.js';
import { listDocuments } from '../../library/documentsApi.js';
import type { DocumentSkill, LibraryDocument } from '../../library/documents.js';
import type { ExamModule } from '../examApi.js';
import type { Suggestion } from './resultModel.js';
import { ChevronRightGlyph, DocGlyph, SparkGlyph, TargetRingGlyph } from './ResultIcons.js';

/**
 * "Gợi ý luyện tập tiếp theo" — read off this sitting, not off a recommender.
 *
 * <b>Each line names its own evidence.</b> The reference lists three generic
 * tips; these are the same three shapes filled from what actually happened —
 * the weakest question type by name and count, the pace when it is slower than
 * ninety seconds a question, the questions left blank when there were any. A
 * learner can check every one of them against the review below.
 *
 * <b>A sitting that produced none of them gets none.</b> Padding the list to
 * three with advice nobody measured is exactly the fabrication DESIGN.md
 * anti-pattern #12 forbids, and it is also what teaches a reader to skip the
 * panel.
 */
export function PracticeRecommendations({ suggestions }: { suggestions: Suggestion[] }) {
  const { t } = useI18n();

  return (
    <section className="exs-panel">
      <div className="exs-panel-head">
        <span className="exs-panel-head-icon" aria-hidden="true">
          <TargetRingGlyph size={18} />
        </span>
        <div>
          <h2>{t('exam.nextStepsTitle')}</h2>
          <p>{t('exam.nextStepsLead')}</p>
        </div>
      </div>

      {suggestions.length === 0 ? (
        <p className="exs-empty">{t('exam.nextStepsEmpty')}</p>
      ) : (
        <ul className="exs-list">
          {suggestions.map((suggestion, at) => (
            <li key={suggestion.id}>
              <div className="exs-list-item">
                <span className="exs-list-num" aria-hidden="true">
                  {at + 1}
                </span>
                <span className="exs-list-text">
                  <span className="exs-list-title">{suggestion.title}</span>
                  <span className="exs-list-body">{suggestion.body}</span>
                </span>
                <span />
              </div>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/**
 * "Tài liệu gợi ý cho bạn" — real rows from the published library.
 *
 * <b>Filtered by the skill just sat, and nothing else.</b> The library API is
 * public and returns published documents only, so this is the same shelf
 * `/documents` shows; asking it for the skill in hand is the whole of the
 * "recommendation". Anything cleverer would need a model of the learner that
 * this product does not have, and a made-up ranking is worse than an honest
 * filter.
 *
 * A failed or empty fetch renders the panel with a line saying so, not three
 * grey rectangles: a skeleton that never resolves is indistinguishable from a
 * page still loading.
 */
export function SuggestedDocuments({ module: moduleId }: { module: ExamModule | null }) {
  const { t } = useI18n();
  const alive = useAlive();

  const [documents, setDocuments] = useState<LibraryDocument[] | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();

    void (async () => {
      try {
        const items = await listDocuments(
          moduleId === null ? undefined : { skill: moduleId as DocumentSkill },
          { signal: controller.signal },
        );
        if (alive.current) setDocuments(items.slice(0, 3));
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === 'AbortError') return;
        if (alive.current) setFailed(true);
      }
    })();

    return () => controller.abort();
  }, [moduleId, alive]);

  return (
    <section className="exs-panel">
      <div className="exs-panel-head">
        <span className="exs-panel-head-icon" aria-hidden="true">
          <SparkGlyph size={18} />
        </span>
        <div>
          <h2>{t('exam.docsTitle')}</h2>
          <p>{t('exam.docsLead')}</p>
        </div>
      </div>

      {failed ? (
        <p className="exs-empty">{t('exam.docsFailed')}</p>
      ) : documents === null ? (
        <p className="exs-empty">{t('exam.loading')}</p>
      ) : documents.length === 0 ? (
        <p className="exs-empty">{t('exam.docsEmpty')}</p>
      ) : (
        <ul className="exs-list">
          {documents.map((document) => (
            <li key={document.id}>
              <Link className="exs-list-item" to={Paths.documents}>
                <span className="exs-list-icon" data-tone="blue" aria-hidden="true">
                  <DocGlyph size={16} />
                </span>
                <span className="exs-list-text">
                  <span className="exs-list-title">{document.title}</span>
                  <span className="exs-list-body">
                    {[document.format, document.targetBand]
                      .filter((piece) => piece !== undefined)
                      .join(' · ')}
                  </span>
                </span>
                <span className="exs-list-caret" aria-hidden="true">
                  <ChevronRightGlyph />
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
