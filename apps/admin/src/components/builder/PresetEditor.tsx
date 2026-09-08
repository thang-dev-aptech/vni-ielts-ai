import { MediaRefField } from './MediaRefField.js';
import type { ExamPart, ExamQuestion } from '../../lib/examDocument.js';
import { acceptedText, setAcceptedText } from '../../lib/examDocument.js';
import { presetOf } from '../../lib/questionPresets.js';
import type { MediaAsset } from '../../lib/media.js';

export function PresetEditor({
  question,
  part,
  media,
  readOnly = false,
  onQuestion,
  onPart,
}: {
  question: ExamQuestion;
  part: ExamPart;
  media: MediaAsset[];
  readOnly?: boolean;
  onQuestion: (patch: Partial<ExamQuestion>) => void;
  onPart: (patch: Partial<ExamPart>) => void;
}) {
  const preset = question.preset !== undefined ? presetOf(question.preset) : undefined;
  const showAnswer = question.type !== 'essay-task' && question.type !== 'speaking-response';
  const showCue = question.preset === 'part-2';

  return (
    <fieldset className="cms-builder-editor" disabled={readOnly}>
      <p className="cms-nav-title">
        Câu {question.order}
        {preset !== undefined ? ` · ${preset.label}` : ` · ${question.type}`}
      </p>

      {part.kind === 'recording' && (
        <>
          <MediaRefField
            label="Audio phần nghe"
            value={part.audio}
            kind="audio"
            media={media}
            onChange={(audio) => onPart({ audio })}
          />
          <label className="cms-field">
            <span>Transcript</span>
            <textarea
              rows={4}
              value={part.transcript ?? ''}
              onChange={(event) => onPart({ transcript: event.target.value })}
            />
          </label>
        </>
      )}

      {showCue && (
        <>
          <label className="cms-field">
            <span>Cue card — chủ đề</span>
            <input
              value={part.cueCard?.topic ?? ''}
              onChange={(event) =>
                onPart({
                  cueCard: { topic: event.target.value, bullets: part.cueCard?.bullets ?? [''] },
                })
              }
            />
          </label>
          <label className="cms-field">
            <span>Cue card — gợi ý (mỗi dòng một ý)</span>
            <textarea
              rows={3}
              value={(part.cueCard?.bullets ?? []).join('\n')}
              onChange={(event) =>
                onPart({
                  cueCard: {
                    topic: part.cueCard?.topic ?? '',
                    bullets: event.target.value.split('\n'),
                  },
                })
              }
            />
          </label>
        </>
      )}

      <label className="cms-field">
        <span>Câu hỏi / lệnh</span>
        <textarea
          rows={3}
          value={question.prompt ?? ''}
          onChange={(event) => onQuestion({ prompt: event.target.value })}
        />
      </label>

      {(question.options ?? []).length > 0 && (
        <fieldset className="cms-field">
          <legend>Lựa chọn</legend>
          {(question.options ?? []).map((option, index) => (
            <label className="cms-field-inline" key={option.key}>
              <span>{option.key}</span>
              <input
                value={option.text}
                onChange={(event) => {
                  const options = (question.options ?? []).map((item, i) =>
                    i === index ? { ...item, text: event.target.value } : item,
                  );
                  onQuestion({ options });
                }}
              />
            </label>
          ))}
        </fieldset>
      )}

      {question.group !== undefined && (
        <>
          <label className="cms-field">
            <span>Khung chung — hướng dẫn</span>
            <input
              value={question.group.instruction ?? ''}
              onChange={(event) =>
                onQuestion({ group: { ...question.group!, instruction: event.target.value } })
              }
            />
          </label>
          {question.preset === 'summary-completion' || question.preset === 'note-table-completion' ? (
            <label className="cms-field">
              <span>Đoạn tóm tắt / bảng (dùng [n] cho chỗ trống)</span>
              <textarea
                rows={4}
                value={question.group.text ?? ''}
                onChange={(event) =>
                  onQuestion({ group: { ...question.group!, text: event.target.value } })
                }
              />
            </label>
          ) : null}
          {question.preset === 'diagram-labelling' && (
            <MediaRefField
              label="Hình diagram / map"
              value={question.group.image}
              kind="image"
              media={media}
              onChange={(image) => onQuestion({ group: { ...question.group!, image: image ?? '' } })}
            />
          )}
        </>
      )}

      {showAnswer && (
        <label className="cms-field">
          <span>Đáp án đúng</span>
          <input
            value={acceptedText(question)}
            onChange={(event) => onQuestion(setAcceptedText(question, event.target.value))}
          />
          {question.type === 'multiple-select' && (
            <span className="cms-muted">Nhiều đáp án: cách nhau bằng dấu phẩy.</span>
          )}
        </label>
      )}

      {question.type === 'essay-task' && (
        <label className="cms-field">
          <span>Số từ tối thiểu trên đề bài (không tự bịa 150/250)</span>
          <input
            type="number"
            min={1}
            value={part.constraints?.minWords ?? ''}
            onChange={(event) => {
              const parsed = Number(event.target.value);
              onPart({
                constraints:
                  event.target.value === '' || !Number.isFinite(parsed)
                    ? undefined
                    : { minWords: parsed },
              });
            }}
          />
        </label>
      )}

      <label className="cms-field">
        <span>Giải thích (tuỳ chọn)</span>
        <textarea
          rows={3}
          value={typeof question.explanation === 'string' ? question.explanation : ''}
          onChange={(event) => onQuestion({ explanation: event.target.value })}
        />
      </label>
    </fieldset>
  );
}
