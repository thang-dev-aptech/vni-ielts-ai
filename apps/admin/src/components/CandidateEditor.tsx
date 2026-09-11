import type {
  ParsedCandidateClassification,
  ParsedCandidateDetail,
  ParsedCandidateModule,
  ParsedCandidateOption,
  ParsedCandidatePart,
  ParsedCandidateProvenance,
  ParsedCandidateQuestion,
} from '../lib/adminApi.js';
import { formatAdminDate } from '../lib/formatAdminDate.js';

const CLASSIFICATIONS: readonly { value: ParsedCandidateClassification; label: string }[] = [
  { value: 'reading', label: 'Reading' },
  { value: 'listening', label: 'Listening' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
  { value: 'unclassified', label: 'Chưa phân loại' },
  { value: 'needs-review', label: 'Cần rà soát' },
];

const MODULES = ['reading', 'listening', 'writing', 'speaking'] as const;

const QUESTION_TYPES = [
  'multiple-choice',
  'multiple-select',
  'true-false-notgiven',
  'yes-no-notgiven',
  'matching',
  'completion',
  'short-answer',
  'labelling',
  'essay-task',
  'speaking-response',
] as const;

let editorId = 0;

function nextEditorId(prefix: string): string {
  editorId += 1;
  return `${prefix}-review-${editorId}`;
}

function ordered<T extends { order: number }>(items: readonly T[]): T[] {
  return items.map((item, index) => ({ ...item, order: index + 1 }));
}

function moved<T>(items: readonly T[], index: number, offset: -1 | 1): T[] {
  const destination = index + offset;
  if (destination < 0 || destination >= items.length) return [...items];
  const next = [...items];
  [next[index], next[destination]] = [next[destination]!, next[index]!];
  return next;
}

const STATUS_LABEL: Record<ParsedCandidateDetail['status'], string> = {
  'pending-review': 'Chờ rà soát',
  confirmed: 'Đã xác nhận — chưa tạo bản nháp',
  rejected: 'Đã từ chối',
};

export function unresolvedReasons(candidate: ParsedCandidateDetail): string[] {
  const reasons: string[] = [];
  if (candidate.classification === 'unclassified' || candidate.classification === 'needs-review') {
    reasons.push('Chưa có kỹ năng đã resolve.');
  }
  if (candidate.title == null || candidate.title.trim() === '') {
    reasons.push('Thiếu tiêu đề.');
  }
  if (candidate.modules.length === 0) {
    reasons.push('Chưa có module.');
  }
  for (const [moduleIndex, module] of candidate.modules.entries()) {
    if (module.parts.length === 0) {
      reasons.push(`Module ${moduleIndex + 1} chưa có part.`);
    }
    for (const [partIndex, part] of module.parts.entries()) {
      if (part.questions.length === 0) {
        reasons.push(`Part ${partIndex + 1} chưa có câu hỏi.`);
      }
      for (const question of part.questions) {
        if (question.prompt == null || question.prompt.trim() === '') {
          reasons.push(`Câu ${question.order} thiếu đề bài.`);
        }
        if (question.answerKey == null || question.answerKey.accepted.length === 0) {
          reasons.push(`Câu ${question.order} thiếu đáp án.`);
        }
      }
    }
  }
  return reasons;
}

export function provenanceLine(source: ParsedCandidateProvenance): string {
  const bits = [source.fileName];
  if (source.page != null) bits.push(`tr. ${source.page}`);
  if (source.section != null && source.section !== '') bits.push(source.section);
  if (source.reference != null && source.reference !== '') bits.push(source.reference);
  return bits.join(' · ');
}

export interface CandidateEditorProps {
  candidate: ParsedCandidateDetail;
  canReview: boolean;
  busy: boolean;
  error: string | null;
  onChange: (next: ParsedCandidateDetail) => void;
  onSave: () => void;
  onReject: () => void;
  onConfirm: () => void;
  onReload: () => void;
}

/**
 * Structured review of one parsed candidate.
 *
 * Provenance is a caption, never the source document. Confirm does not create
 * a Draft and there is no publish control on this surface.
 */
export function CandidateEditor({
  candidate,
  canReview,
  busy,
  error,
  onChange,
  onSave,
  onReject,
  onConfirm,
  onReload,
}: CandidateEditorProps) {
  const pending = candidate.status === 'pending-review';
  const editable = canReview && pending && !busy;
  const unresolved = unresolvedReasons(candidate);
  const confirmBlocked =
    !pending ||
    !canReview ||
    candidate.classification === 'unclassified' ||
    candidate.classification === 'needs-review' ||
    candidate.modules.length === 0 ||
    candidate.modules.some((module) => module.parts.length === 0);

  function patch(partial: Partial<ParsedCandidateDetail>) {
    onChange({ ...candidate, ...partial });
  }

  function patchModule(index: number, next: ParsedCandidateModule) {
    patch({ modules: candidate.modules.map((module, i) => (i === index ? next : module)) });
  }

  function patchPart(
    moduleIndex: number,
    partIndex: number,
    patcher: (part: ParsedCandidatePart) => ParsedCandidatePart,
  ) {
    const module = candidate.modules[moduleIndex];
    if (module === undefined) return;
    patchModule(moduleIndex, {
      ...module,
      parts: module.parts.map((part, index) => (index === partIndex ? patcher(part) : part)),
    });
  }

  function replaceParts(moduleIndex: number, parts: ParsedCandidatePart[]) {
    const module = candidate.modules[moduleIndex];
    if (module === undefined) return;
    patchModule(moduleIndex, { ...module, parts: ordered(parts) });
  }

  function replaceQuestions(moduleIndex: number, partIndex: number, questions: ParsedCandidateQuestion[]) {
    patchPart(moduleIndex, partIndex, (part) => ({ ...part, questions: ordered(questions) }));
  }

  function patchQuestion(
    moduleIndex: number,
    partIndex: number,
    questionIndex: number,
    patcher: (question: ParsedCandidateQuestion) => ParsedCandidateQuestion,
  ) {
    const module = candidate.modules[moduleIndex];
    if (module === undefined) return;
    const part = module.parts[partIndex];
    if (part === undefined) return;
    const questions = part.questions.map((item, i) => (i === questionIndex ? patcher(item) : item));
    const parts = module.parts.map((item, i) => (i === partIndex ? { ...item, questions } : item));
    patchModule(moduleIndex, { ...module, parts });
  }

  return (
    <section className="cms-panel" aria-label="Rà soát đề đề xuất">
      <p className="cms-sub">{STATUS_LABEL[candidate.status]}</p>

      {!canReview && (
        <p className="cms-alert" role="status">
          Tài khoản thiếu quyền <code>exam.review</code> — xem được, không sửa, không xác nhận.
        </p>
      )}

      {error !== null && (
        <p className="cms-alert is-bad" role="alert">
          {error}
        </p>
      )}

      {error !== null && (
        <div className="cms-version-actions">
          <button type="button" className="cms-primary" onClick={onReload}>
            Tải lại
          </button>
        </div>
      )}

      <label className="cms-field">
        <span>Tiêu đề</span>
        <input
          value={candidate.title ?? ''}
          disabled={!editable}
          onChange={(event) => patch({ title: event.target.value })}
        />
      </label>

      <label className="cms-field">
        <span>Kỹ năng</span>
        <select
          value={candidate.classification}
          disabled={!editable}
          onChange={(event) =>
            patch({ classification: event.target.value as ParsedCandidateClassification })
          }
        >
          {CLASSIFICATIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </label>

      {candidate.confidence != null && (
        <p className="cms-muted">Độ tin cậy (metadata, không quyết định band): {candidate.confidence}</p>
      )}

      <h3>Nguồn</h3>
      {candidate.sources.length === 0 ? (
        <p className="cms-muted">Không có provenance.</p>
      ) : (
        <ul className="cms-notes cms-provenance">
          {candidate.sources.map((source, index) => (
            <li key={`${source.fileName}-${index}`}>{provenanceLine(source)}</li>
          ))}
        </ul>
      )}

      <h3>Nội dung đề xuất</h3>
      {candidate.modules.map((module, moduleIndex) => (
        <article className="cms-candidate-module" key={moduleIndex}>
          <label className="cms-field">
            <span>Module</span>
            <select
              value={module.module ?? ''}
              disabled={!editable}
              onChange={(event) => {
                const value = event.target.value;
                const skill = value === '' ? null : value;
                patchModule(moduleIndex, {
                  ...module,
                  module: skill,
                  classification:
                    skill === null ? 'unclassified' : (skill as ParsedCandidateClassification),
                });
              }}
            >
              <option value="">Chưa gán</option>
              {MODULES.map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>
          <p className="cms-provenance">{provenanceLine(module.provenance)}</p>
          {editable && (
            <div className="cms-version-actions">
              <button
                type="button"
                className="cms-secondary"
                onClick={() =>
                  replaceParts(moduleIndex, [
                    ...module.parts,
                    {
                      id: nextEditorId('part'),
                      order: module.parts.length + 1,
                      title: null,
                      body: null,
                      provenance: module.provenance,
                      questions: [],
                    },
                  ])
                }
              >
                Thêm part
              </button>
            </div>
          )}

          {module.parts.map((part, partIndex) => (
            <div className="cms-candidate-part" key={part.id}>
              <h4>Part {part.order}</h4>
              <p className="cms-provenance">{provenanceLine(part.provenance)}</p>
              <label className="cms-field">
                <span>Tiêu đề part</span>
                <input
                  aria-label={`Tiêu đề part ${part.order}`}
                  value={part.title ?? ''}
                  disabled={!editable}
                  onChange={(event) =>
                    patchPart(moduleIndex, partIndex, (item) => ({ ...item, title: event.target.value }))
                  }
                />
              </label>
              <label className="cms-field">
                <span>Nội dung part</span>
                <textarea
                  aria-label={`Nội dung part ${part.order}`}
                  rows={4}
                  value={part.body ?? ''}
                  disabled={!editable}
                  onChange={(event) =>
                    patchPart(moduleIndex, partIndex, (item) => ({ ...item, body: event.target.value }))
                  }
                />
              </label>
              {editable && (
                <div className="cms-version-actions" aria-label={`Thao tác part ${part.order}`}>
                  <button type="button" className="cms-secondary" disabled={partIndex === 0} onClick={() => replaceParts(moduleIndex, moved(module.parts, partIndex, -1))}>Đưa part lên</button>
                  <button type="button" className="cms-secondary" disabled={partIndex === module.parts.length - 1} onClick={() => replaceParts(moduleIndex, moved(module.parts, partIndex, 1))}>Đưa part xuống</button>
                  <button type="button" className="cms-danger" onClick={() => replaceParts(moduleIndex, module.parts.filter((_, index) => index !== partIndex))}>Xóa part</button>
                  <button
                    type="button"
                    className="cms-secondary"
                    onClick={() =>
                      replaceQuestions(moduleIndex, partIndex, [
                        ...part.questions,
                        {
                          id: nextEditorId('question'),
                          order: part.questions.length + 1,
                          type: null,
                          prompt: null,
                          options: [],
                          answerKey: null,
                          provenance: part.provenance,
                        },
                      ])
                    }
                  >
                    Thêm câu hỏi
                  </button>
                </div>
              )}

              {part.questions.map((question, questionIndex) => (
                <div className="cms-candidate-question" key={question.id}>
                  <label className="cms-field">
                    <span>Loại câu hỏi {question.order}</span>
                    <select
                      aria-label={`Loại câu hỏi ${question.order}`}
                      value={question.type ?? ''}
                      disabled={!editable}
                      onChange={(event) =>
                        patchQuestion(moduleIndex, partIndex, questionIndex, (item) => ({
                          ...item,
                          type: event.target.value === '' ? null : event.target.value,
                        }))
                      }
                    >
                      <option value="">Chưa xác định</option>
                      {QUESTION_TYPES.map((type) => <option key={type} value={type}>{type}</option>)}
                    </select>
                  </label>
                  <label className="cms-field">
                    <span>Câu {question.order}</span>
                    <textarea
                      aria-label={`Câu ${question.order}`}
                      rows={3}
                      value={question.prompt ?? ''}
                      disabled={!editable}
                      onChange={(event) =>
                        patchQuestion(moduleIndex, partIndex, questionIndex, (item) => ({
                          ...item,
                          prompt: event.target.value,
                        }))
                      }
                    />
                  </label>
                  <p className="cms-provenance">{provenanceLine(question.provenance)}</p>
                  {editable && (
                    <div className="cms-version-actions" aria-label={`Thao tác câu ${question.order}`}>
                      <button type="button" className="cms-secondary" disabled={questionIndex === 0} onClick={() => replaceQuestions(moduleIndex, partIndex, moved(part.questions, questionIndex, -1))}>Đưa câu lên</button>
                      <button type="button" className="cms-secondary" disabled={questionIndex === part.questions.length - 1} onClick={() => replaceQuestions(moduleIndex, partIndex, moved(part.questions, questionIndex, 1))}>Đưa câu xuống</button>
                      <button type="button" className="cms-danger" onClick={() => replaceQuestions(moduleIndex, partIndex, part.questions.filter((_, index) => index !== questionIndex))}>Xóa câu hỏi</button>
                      <button
                        type="button"
                        className="cms-secondary"
                        onClick={() =>
                          patchQuestion(moduleIndex, partIndex, questionIndex, (item) => ({
                            ...item,
                            options: [...item.options, { key: '', text: '' }],
                          }))
                        }
                      >
                        Thêm lựa chọn
                      </button>
                    </div>
                  )}

                  {question.options.length > 0 && (
                    <div className="cms-candidate-options">
                      {question.options.map((option, optionIndex) => (
                        <div className="cms-option-row" key={`${question.id}-${optionIndex}`}>
                          <label className="cms-field">
                            <span>Mã</span>
                            <input
                              aria-label={`Mã lựa chọn ${optionIndex + 1} câu ${question.order}`}
                              value={option.key}
                              disabled={!editable}
                              onChange={(event) =>
                                patchQuestion(moduleIndex, partIndex, questionIndex, (item) => ({
                                  ...item,
                                  options: item.options.map((entry, i) =>
                                    i === optionIndex ? { ...entry, key: event.target.value } : entry,
                                  ),
                                }))
                              }
                            />
                          </label>
                          <label className="cms-field">
                            <span>Lựa chọn</span>
                            <input
                              aria-label={`Lựa chọn ${option.key || optionIndex + 1} câu ${question.order}`}
                              value={option.text}
                              disabled={!editable}
                              onChange={(event) =>
                                patchQuestion(moduleIndex, partIndex, questionIndex, (item) => ({
                                  ...item,
                                  options: item.options.map((entry, i) =>
                                    i === optionIndex ? { ...entry, text: event.target.value } : entry,
                                  ),
                                }))
                              }
                            />
                          </label>
                          {editable && (
                            <div className="cms-version-actions" aria-label={`Thao tác lựa chọn ${optionIndex + 1} câu ${question.order}`}>
                              <button
                                type="button"
                                className="cms-secondary"
                                disabled={optionIndex === 0}
                                onClick={() => patchQuestion(moduleIndex, partIndex, questionIndex, (item) => ({ ...item, options: moved(item.options, optionIndex, -1) as ParsedCandidateOption[] }))}
                              >
                                Đưa lên
                              </button>
                              <button
                                type="button"
                                className="cms-secondary"
                                disabled={optionIndex === question.options.length - 1}
                                onClick={() => patchQuestion(moduleIndex, partIndex, questionIndex, (item) => ({ ...item, options: moved(item.options, optionIndex, 1) as ParsedCandidateOption[] }))}
                              >
                                Đưa xuống
                              </button>
                              <button
                                type="button"
                                className="cms-danger"
                                onClick={() => patchQuestion(moduleIndex, partIndex, questionIndex, (item) => ({ ...item, options: item.options.filter((_, index) => index !== optionIndex) }))}
                              >
                                Xóa lựa chọn
                              </button>
                            </div>
                          )}
                        </div>
                      ))}
                    </div>
                  )}

                  <label className="cms-field cms-answer-key">
                    <span>Đáp án</span>
                    <input
                      aria-label={`Đáp án câu ${question.order}`}
                      value={question.answerKey?.accepted.join(', ') ?? ''}
                      disabled={!editable}
                      onChange={(event) => {
                        const accepted = event.target.value
                          .split(',')
                          .map((item) => item.trim())
                          .filter((item) => item !== '');
                        patchQuestion(moduleIndex, partIndex, questionIndex, (item) => ({
                          ...item,
                          answerKey:
                            accepted.length === 0
                              ? null
                              : {
                                  accepted,
                                  matchingRule: item.answerKey?.matchingRule ?? null,
                                },
                        }));
                      }}
                    />
                  </label>
                </div>
              ))}
            </div>
          ))}
        </article>
      ))}

      <h3>Còn chưa rõ</h3>
      {unresolved.length === 0 ? (
        <p className="cms-muted">Không còn mục thiếu trên đề xuất này.</p>
      ) : (
        <ul className="cms-notes">
          {unresolved.map((reason) => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      )}

      <h3>Lịch sử sửa</h3>
      {candidate.corrections.length === 0 ? (
        <p className="cms-muted">Chưa có lần sửa nào.</p>
      ) : (
        <ul className="cms-notes">
          {candidate.corrections.map((correction) => (
            <li key={correction.id}>
              {correction.field}
              {formatAdminDate(correction.at, 'datetime') != null
                ? ` · ${formatAdminDate(correction.at, 'datetime')}`
                : ''}
            </li>
          ))}
        </ul>
      )}

      {editable && (
        <div className="cms-version-actions">
          <button type="button" className="cms-primary" disabled={busy} onClick={onSave}>
            {busy ? 'Đang lưu…' : 'Lưu sửa đổi'}
          </button>
          <button type="button" disabled={busy} onClick={onReject}>
            Từ chối
          </button>
          <button
            type="button"
            className="cms-primary"
            disabled={busy || confirmBlocked}
            onClick={onConfirm}
          >
            Xác nhận đề xuất
          </button>
        </div>
      )}
    </section>
  );
}
