import { useEffect, useRef, useState } from 'react';
import { apiBase, authedFetch } from '../../lib/api.js';
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
 * Adheres to D-8:
 * - max-height: 40vh on prompt display
 * - Click-to-enlarge modal dialog with Escape dismissal
 */
export function ExamImage({ reference, caption }: { reference: string; caption?: string | null }) {
  const { accessToken } = useAuth();
  const { t } = useI18n();

  const [source, setSource] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [enlarged, setEnlarged] = useState(false);
  const triggerRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    if (!enlarged) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setEnlarged(false);
        triggerRef.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [enlarged]);

  useEffect(() => {
    if (accessToken === null) return;

    let url: string | null = null;
    const controller = new AbortController();

    void (async () => {
      try {
        const base = apiBase();
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
    <>
      <figure className="exam-figure">
        {source === null ? (
          <p className="exam-figure-loading">{t('exam.imageLoading')}</p>
        ) : (
          <div className="exam-figure-wrap">
            <img
              className="exam-figure-image"
              src={source}
              alt=""
              onClick={() => setEnlarged(true)}
            />
            <button
              ref={triggerRef}
              type="button"
              className="exam-figure-enlarge-btn"
              aria-label="Xem lớn hình ảnh"
              title="Xem lớn hình ảnh"
              onClick={() => setEnlarged(true)}
            >
              <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                <path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7" />
              </svg>
              <span>Xem lớn</span>
            </button>
          </div>
        )}

        <figcaption className="exam-figure-caption">
          {caption !== null && caption !== undefined && caption !== '' ? (
            <span>{caption}</span>
          ) : null}
          <span className="exam-figure-note">{t('exam.imageNoDescription')}</span>
        </figcaption>
      </figure>

      {enlarged && source !== null && (
        <div
          className="exam-image-modal-scrim"
          role="dialog"
          aria-modal="true"
          aria-label={caption ?? 'Xem lớn hình ảnh'}
          onClick={(e) => {
            if (e.target === e.currentTarget) {
              setEnlarged(false);
              triggerRef.current?.focus();
            }
          }}
        >
          <div className="exam-image-modal-content">
            <div className="exam-image-modal-head">
              <span className="exam-image-modal-title">{caption ?? 'Hình ảnh bài thi'}</span>
              <button
                type="button"
                className="exam-image-modal-close"
                aria-label="Đóng xem lớn"
                onClick={() => {
                  setEnlarged(false);
                  triggerRef.current?.focus();
                }}
              >
                ✕
              </button>
            </div>
            <img className="exam-image-modal-img" src={source} alt="" />
          </div>
        </div>
      )}
    </>
  );
}
