import { useEffect, useState } from 'react';
import { authedFetch } from '../../lib/api.js';
import { useAuth } from '../auth/AuthContext.js';
import { useI18n } from '../../i18n/index.js';
import '../../styles/audio.css';

/**
 * A map, diagram or chart that a question cannot be answered without.
 *
 * <b>`imageKey` has been on `PartView` since the exam engine shipped and was
 * never rendered.</b> That went unnoticed while the fixtures were text-only,
 * and stops being invisible the moment a real paper arrives: IELTS Writing
 * Task 1 *is* a chart, and Listening Part 2 labels rooms on a plan. Without
 * this component the candidate is asked to describe something they were not
 * shown.
 *
 * <b>Fetched with the token, like the audio.</b> Exam media is exam content —
 * served anonymously a map can be collected and published beside its answers —
 * so it comes through the authenticated endpoint as a blob rather than as a
 * plain `src` the browser fetches on its own.
 *
 * <b>The alt text is not generated here.</b> A description of a chart is part
 * of the question: the package this was built against carries an example of
 * exactly how that goes wrong — its Task 1 alt text described a different task
 * entirely, so a screen-reader user was set the wrong exercise. Until an
 * authored description travels in the contract, the image is marked decorative
 * and the failure is stated in words a reader can act on rather than papered
 * over with a caption invented by the renderer.
 */
export function ExamImage({ reference, caption }: { reference: string; caption?: string | null }) {
  const { accessToken } = useAuth();
  const { t } = useI18n();

  const [source, setSource] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (accessToken === null) return;

    let url: string | null = null;
    const controller = new AbortController();

    void (async () => {
      try {
        const base = import.meta.env['VITE_API_BASE'] ?? 'http://localhost:5099';
        const path = reference.replace(/^assets\//, '');
        // Shared transport: a Writing Task 1 chart requested an hour into
        // a sitting was being asked for with an expired token, and the task is
        // unanswerable without the chart it describes.
        const response = await authedFetch(`${base}/api/v1/exams/assets/${path}`, accessToken, {
          signal: controller.signal,
        });

        if (!response.ok) throw new Error(String(response.status));

        url = URL.createObjectURL(await response.blob());
        setSource(url);
      } catch (caught) {
        if (caught instanceof DOMException && caught.name === 'AbortError') return;
        setFailed(true);
      }
    })();

    return () => {
      controller.abort();
      // Not revoking leaks the file for the life of the tab, and a Listening
      // section can hold several.
      if (url !== null) URL.revokeObjectURL(url);
    };
  }, [accessToken, reference]);

  if (failed) {
    return (
      <p className="audio-failed" role="alert">
        {t('exam.imageFailed')}
      </p>
    );
  }

  return (
    <figure className="exam-figure">
      {source === null ? (
        <p className="exam-figure-loading">{t('exam.imageLoading')}</p>
      ) : (
        /*
          `alt=""` and a stated gap, rather than a caption this file made up.
          A generated description of a chart is a different exercise from the
          one the author set — see the note at the top.
        */
        <img className="exam-figure-image" src={source} alt="" />
      )}

      <figcaption className="exam-figure-caption">
        {caption !== null && caption !== undefined && caption !== '' ? (
          <span>{caption}</span>
        ) : null}
        <span className="exam-figure-note">{t('exam.imageNoDescription')}</span>
      </figcaption>
    </figure>
  );
}
