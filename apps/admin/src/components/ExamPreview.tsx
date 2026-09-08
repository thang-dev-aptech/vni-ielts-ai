import { useState } from 'react';
import { getExamPreview, type AdminAcceptedAnswer, type AdminExamPreview } from '../lib/adminApi.js';

/**
 * Content and answer key, for a reviewer.
 *
 * <b>Lazy, and cached once open.</b> A version's full content is a bigger
 * payload than anything else on these screens — nobody reading the workflow
 * timeline needs it until they ask, and once they have asked there is no
 * reason to ask the server again for the same version.
 *
 * <b>The answer key is here on purpose.</b> The learner-facing exam client
 * never receives one at all (threat `T7`) — this is a different, separately
 * permissioned surface, for the person whose job is to check the key is
 * right before the version ships.
 */
export function ExamPreview({
  accessToken,
  examVersionId,
}: {
  accessToken: string;
  examVersionId: string;
}) {
  const [data, setData] = useState<AdminExamPreview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function open() {
    if (data !== null || loading) return;
    setLoading(true);
    setError(null);
    try {
      setData(await getExamPreview(accessToken, examVersionId));
    } catch {
      setError('Không tải được nội dung đề.');
    } finally {
      setLoading(false);
    }
  }

  return (
    <details
      className="cms-panel cms-exam-preview"
      onToggle={(event) => {
        if (event.currentTarget.open) void open();
      }}
    >
      <summary>Nội dung &amp; đáp án</summary>

      <div className="cms-exam-preview-body">
        {loading && <p className="cms-muted">Đang tải…</p>}
        {error !== null && (
          <p className="cms-alert is-bad" role="alert">
            {error}
          </p>
        )}

        {data !== null && data.sections.length === 0 && (
          <p className="cms-muted">Version này chưa có nội dung câu hỏi.</p>
        )}

        {data?.sections.map((section) => (
          <section className="cms-exam-preview-section" key={section.module}>
            <h3>{section.module}</h3>

            {section.parts.map((part, partIndex) => (
              <div className="cms-exam-preview-part" key={partIndex}>
                {part.title !== null && <h4>{part.title}</h4>}
                {part.body !== null && <p>{part.body}</p>}
                {part.transcript !== null && (
                  <details className="cms-exam-preview-transcript">
                    <summary>Bản ghi lời thoại</summary>
                    <p>{part.transcript}</p>
                  </details>
                )}
                {part.cueCard !== null && (
                  <div className="cms-exam-preview-cuecard">
                    <strong>{part.cueCard.topic}</strong>
                    <ul>
                      {part.cueCard.bullets.map((bullet, bulletIndex) => (
                        <li key={bulletIndex}>{bullet}</li>
                      ))}
                    </ul>
                  </div>
                )}

                {part.questions.length > 0 && (
                  <ol className="cms-exam-preview-questions">
                    {part.questions.map((question, questionIndex) => (
                      <li key={question.id} className="cms-question">
                        <div className="cms-question-main">
                          <div className="cms-question-meta">
                            <span className="cms-question-number" aria-hidden="true">
                              {String(questionIndex + 1).padStart(2, '0')}
                            </span>
                            <span className="cms-question-type">{question.type}</span>
                          </div>

                          {question.prompt !== null && (
                            <p className="cms-question-prompt">{question.prompt}</p>
                          )}

                          {question.options.length > 0 && (
                            <ul className="cms-exam-preview-options">
                              {question.options.map((option) => (
                                <li key={option.key}>
                                  <span className="cms-option-key">{option.key}</span>
                                  <span>{option.text}</span>
                                </li>
                              ))}
                            </ul>
                          )}
                        </div>

                        {question.answerKey !== null && (
                          <div className="cms-exam-preview-answer">
                            <span>Đáp án đúng</span>
                            <strong>{formatAnswerKey(question.answerKey)}</strong>
                          </div>
                        )}
                      </li>
                    ))}
                  </ol>
                )}
              </div>
            ))}
          </section>
        ))}
      </div>
    </details>
  );
}

function formatAnswerKey(accepted: AdminAcceptedAnswer[]): string {
  if (accepted.length === 0) return '—';

  return accepted
    .map((answer) => {
      if (answer.single !== null) return answer.single;
      if (answer.all !== null) return answer.all.join(' + ');
      if (answer.pairLeft !== null && answer.pairRight !== null) {
        return `${answer.pairLeft} → ${answer.pairRight}`;
      }
      return '—';
    })
    .join(' hoặc ');
}
