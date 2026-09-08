import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { ContentText } from '../components/ContentText.js';
import { useAdminAuth } from '../lib/AdminAuth.js';
import { AdminPaths } from '../routes/paths.js';
import {
  getExamPreview,
  MODULE_LABEL,
  type AdminAcceptedAnswer,
  type AdminExamPreview,
  type AdminExamPreviewPart,
} from '../lib/adminApi.js';
import { acceptedAnswerLines } from '../lib/formatAnswerKey.js';

const QUESTION_TYPE_LABELS: Record<string, string> = {
  'multiple-choice': 'Trắc nghiệm một đáp án',
  'multiple-select': 'Trắc nghiệm nhiều đáp án',
  'true-false-notgiven': 'Đúng / Sai / Không có thông tin',
  'yes-no-notgiven': 'Có / Không / Không có thông tin',
  matching: 'Nối ghép',
  completion: 'Điền từ',
  'short-answer': 'Trả lời ngắn',
  labelling: 'Gán nhãn biểu đồ',
  'essay-task': 'Bài viết',
  'speaking-response': 'Phần thi nói',
};

export function formatQuestionType(type: string): string {
  if (QUESTION_TYPE_LABELS[type]) return QUESTION_TYPE_LABELS[type];
  return type
    .split('-')
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ');
}

export function formatAnswerKey(accepted: AdminAcceptedAnswer[]): string {
  return acceptedAnswerLines(accepted).join(' hoặc ');
}

function isOptionCorrect(optionKey: string, accepted: AdminAcceptedAnswer[] | null): boolean {
  if (!accepted || accepted.length === 0) return false;
  const key = optionKey.trim().toLowerCase();
  return accepted.some((ans) => {
    if (ans.single !== null && ans.single.trim().toLowerCase() === key) return true;
    if (ans.all !== null && ans.all.some((item) => item.trim().toLowerCase() === key)) {
      return true;
    }
    return false;
  });
}

function hasSourceMaterial(part: AdminExamPreviewPart): boolean {
  return Boolean(
    (part.title && part.title.trim().length > 0) ||
      (part.body && part.body.trim().length > 0) ||
      (part.transcript && part.transcript.trim().length > 0) ||
      part.cueCard !== null,
  );
}

export function ExamModulePreviewScreen() {
  const { examVersionId = '', module = '' } = useParams<{
    examVersionId: string;
    module: string;
  }>();
  const { accessToken } = useAdminAuth();

  const [data, setData] = useState<AdminExamPreview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!accessToken || !examVersionId) {
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const preview = await getExamPreview(accessToken, examVersionId);
      setData(preview);
    } catch {
      setError('Không tải được nội dung đề.');
    } finally {
      setLoading(false);
    }
  }, [accessToken, examVersionId]);

  useEffect(() => {
    void load();
  }, [load]);

  const targetModule = module.toLowerCase();
  const moduleLabel = MODULE_LABEL[targetModule] ?? module;
  const section = data?.sections.find((s) => s.module.toLowerCase() === targetModule) ?? null;


  return (
    <div className="cms-module-preview-screen">
      <nav className="cms-crumbs" aria-label="Đường dẫn">
        <Link to={AdminPaths.exams}>Đề thi</Link>
        {data !== null && (
          <>
            <span aria-hidden="true">›</span>
            <span>{data.title}</span>
          </>
        )}
        <span aria-hidden="true">›</span>
        <span>{moduleLabel}</span>
      </nav>

      <header className="cms-head">
        <div className="cms-head-title">
          <h1>
            {data ? data.title : 'Xem trước đề thi'} · Kỹ năng {moduleLabel}
          </h1>
          <p className="cms-sub">
            Xem trước nội dung chi tiết bài thi và đáp án chấm của kỹ năng {moduleLabel}.
          </p>
        </div>
      </header>

      {loading && <p className="cms-muted">Đang tải nội dung đề…</p>}

      {error !== null && (
        <div className="cms-alert is-bad" role="alert">
          {error}
        </div>
      )}

      {!loading && error === null && section === null && (
        <section className="cms-panel">
          <p className="cms-empty">Không tìm thấy nội dung kỹ năng này.</p>
        </section>
      )}

      {!loading && error === null && section !== null && (
        <>
          {section.parts.length > 1 && (
            <nav className="cms-module-preview-nav" aria-label="Danh sách phần thi">
              {section.parts.map((_, index) => {
                const partNum = index + 1;
                return (
                  <button
                    key={index}
                    type="button"
                    className="cms-module-preview-nav-btn"
                    onClick={() => {
                      document
                        .getElementById(`part-${partNum}`)
                        ?.scrollIntoView({ behavior: 'smooth' });
                    }}
                  >
                    Part {partNum}
                  </button>
                );
              })}
            </nav>
          )}

          {section.parts.length === 0 ? (
            <section className="cms-panel">
              <p className="cms-empty">Kỹ năng này chưa có phần thi nào.</p>
            </section>
          ) : (
            <div className="cms-module-preview-parts">
              {section.parts.map((part, partIndex) => {
                const partNum = indexOrder(part, partIndex);
                const hasSource = hasSourceMaterial(part);

                return (
                  <section
                    key={partIndex}
                    id={`part-${partNum}`}
                    className={`cms-module-preview-part ${hasSource ? '' : 'is-full-width'}`}
                  >
                    <div className="cms-module-preview-part-header">
                      <h2>Part {partNum}</h2>
                      {part.title && <span className="cms-part-subtitle">{part.title}</span>}
                    </div>

                    <div className="cms-module-preview-part-content">
                      {hasSource && (
                        <div className="cms-module-preview-source">
                          {part.body !== null && part.body.trim().length > 0 && (
                            <div className="cms-module-preview-passage">
                              <h3>Nội dung bài đọc / Ngữ liệu</h3>
                              <ContentText variant="passage" className="cms-module-preview-passage-body">
                                {part.body}
                              </ContentText>
                            </div>
                          )}

                          {part.transcript !== null && part.transcript.trim().length > 0 && (
                            <details className="cms-module-preview-transcript">
                              <summary>Bản ghi lời thoại (Transcript)</summary>
                              <ContentText variant="transcript" className="cms-module-preview-transcript-body">
                                {part.transcript}
                              </ContentText>
                            </details>
                          )}

                          {part.cueCard !== null && (
                            <div className="cms-module-preview-cuecard">
                              <h3>Chủ đề nói (Cue Card)</h3>
                              <strong>{part.cueCard.topic}</strong>
                              <ul>
                                {part.cueCard.bullets.map((bullet, bulletIdx) => (
                                  <li key={bulletIdx}>{bullet}</li>
                                ))}
                              </ul>
                            </div>
                          )}
                        </div>
                      )}

                      <div className="cms-module-preview-questions">
                        {part.questions.length === 0 ? (
                          <p className="cms-muted">Phần này không có câu hỏi trắc nghiệm.</p>
                        ) : (
                          <div className="cms-questions-list">
                            {part.questions.map((question, questionIndex) => (
                              <div key={question.id} className="cms-question-card">
                                <div className="cms-question-card-head">
                                  <span className="cms-question-number" aria-hidden="true">
                                    {String(question.order || questionIndex + 1).padStart(2, '0')}
                                  </span>
                                  <span className="cms-question-type">
                                    {formatQuestionType(question.type)}
                                  </span>
                                </div>

                                {question.prompt !== null && (
                                  <ContentText as="p" variant="prompt" className="cms-question-prompt">
                                    {question.prompt}
                                  </ContentText>
                                )}

                                {question.options.length > 0 && (
                                  <ul className="cms-question-options">
                                    {question.options.map((option) => {
                                      const isCorrect = isOptionCorrect(
                                        option.key,
                                        question.answerKey,
                                      );
                                      return (
                                        <li
                                          key={option.key}
                                          className={`cms-question-option ${isCorrect ? 'is-correct' : ''}`}
                                        >
                                          <span className="cms-option-key">{option.key}</span>
                                          <ContentText as="span" variant="option" className="cms-option-text">
                                            {option.text}
                                          </ContentText>
                                          {isCorrect && (
                                            <span className="cms-option-correct-badge">Đáp án đúng</span>
                                          )}
                                        </li>
                                      );
                                    })}
                                  </ul>
                                )}

                                {question.answerKey !== null && (
                                  <div className="cms-answer-panel">
                                    <span className="cms-answer-panel-label">Đáp án đúng:</span>
                                    <ul className="cms-answer-panel-list">
                                      {acceptedAnswerLines(question.answerKey).map((line, lineIndex) => (
                                        <li key={`${question.id}-ans-${lineIndex}`}>
                                          <ContentText as="span" variant="answer">
                                            {line}
                                          </ContentText>
                                        </li>
                                      ))}
                                    </ul>
                                  </div>
                                )}
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    </div>
                  </section>
                );
              })}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function indexOrder(part: AdminExamPreviewPart, fallbackIndex: number): number {
  if (typeof part.order === 'number' && part.order > 0) return part.order;
  return fallbackIndex + 1;
}
