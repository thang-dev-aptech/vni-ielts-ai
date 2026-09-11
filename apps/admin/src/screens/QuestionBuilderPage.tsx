import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { ChecklistPanel } from '../components/builder/ChecklistPanel.js';
import { PresetEditor } from '../components/builder/PresetEditor.js';
import { StructureTree } from '../components/builder/StructureTree.js';
import { ExamPreview } from '../components/ExamPreview.js';
import { StatusBadge } from '../components/StatusBadge.js';
import { useAdminAuth } from '../lib/AdminAuth.js';
import {
  getExam,
  getExamContent,
  listMedia,
  saveExamContent,
  submitExam,
  validateExamContent,
  type AdminExam,
  type ExamContentFinding,
} from '../lib/adminApi.js';
import {
  addPart,
  addQuestion,
  addSection,
  hydrateDocument,
  setListeningTransfer,
  setSectionDuration,
  setSpeakingPartTiming,
  updatePart,
  updateQuestion,
  type BuilderSelection,
  type ExamDocument,
} from '../lib/examDocument.js';
import { formatAdminDate } from '../lib/formatAdminDate.js';
import type { MediaAsset } from '../lib/media.js';
import { useOperator } from '../lib/operator.js';
import { presetsFor, type ExamModule, type QuestionPresetId } from '../lib/questionPresets.js';
import { selectionFromPointer } from '../lib/selectionFromPointer.js';
import { AdminPaths } from '../routes/paths.js';

const MODULES: ExamModule[] = ['reading', 'listening', 'writing', 'speaking'];

/**
 * Màn A2 — the in-place authoring workspace.
 *
 * Three columns, one document, three existing endpoints. Autosave only on
 * Draft, and only when the schema gate accepts it. There is no Publish here.
 */
export function QuestionBuilderPage() {
  const { versionId } = useParams<{ versionId: string }>();
  const { accessToken } = useAdminAuth();
  const operator = useOperator();
  const navigate = useNavigate();

  const [exam, setExam] = useState<AdminExam | null>(null);
  const [document, setDocument] = useState<ExamDocument | null>(null);
  const [media, setMedia] = useState<MediaAsset[]>([]);
  const [selection, setSelection] = useState<BuilderSelection>({ kind: 'exam' });
  const [findings, setFindings] = useState<ExamContentFinding[]>([]);
  const [valid, setValid] = useState(false);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'unsaved' | 'blocked'>('idle');
  const [error, setError] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [pickModule, setPickModule] = useState(false);
  const [pickPreset, setPickPreset] = useState<{ section: number; part: number } | null>(null);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [pane, setPane] = useState<'structure' | 'editor' | 'check'>('editor');

  const canEdit = operator.can('exam.update.own') || operator.can('exam.update.any');
  const draft = exam?.status === 'draft';

  const load = useCallback(async () => {
    if (accessToken === null || versionId === undefined) return;
    try {
      const [detail, content, library] = await Promise.all([
        getExam(accessToken, versionId),
        getExamContent(accessToken, versionId),
        listMedia(accessToken).catch(() => [] as MediaAsset[]),
      ]);
      const hydrated = hydrateDocument(content, detail.title, detail.variant);
      setExam(detail);
      setDocument(hydrated);
      setMedia(library);
      setLoadError(null);
      try {
        const result = await validateExamContent(accessToken, versionId, hydrated);
        setFindings(result.findings);
        setValid(result.valid);
      } catch {
        setFindings([]);
        setValid(false);
      }
    } catch {
      setLoadError('Không tải được bản nháp này.');
    }
  }, [accessToken, versionId]);

  useEffect(() => {
    void load();
  }, [load]);

  const runValidate = useCallback(
    async (next: ExamDocument) => {
      if (accessToken === null || versionId === undefined) return;
      try {
        const result = await validateExamContent(accessToken, versionId, next);
        setFindings(result.findings);
        setValid(result.valid);
        return result.valid;
      } catch {
        setFindings([]);
        setValid(false);
        return false;
      }
    },
    [accessToken, versionId],
  );

  useEffect(() => {
    if (document === null || !draft || !canEdit) return;
    setSaveState((current) => (current === 'saving' ? current : 'unsaved'));
    const handle = window.setTimeout(() => {
      void (async () => {
        if (accessToken === null || versionId === undefined) return;
        setSaveState('saving');
        const ok = await runValidate(document);
        if (!ok) {
          setSaveState('blocked');
          return;
        }
        try {
          const result = await saveExamContent(accessToken, versionId, document);
          setFindings(result.findings);
          setValid(result.valid);
          if (result.valid) {
            setSavedAt(new Date().toISOString());
            setSaveState('idle');
            const detail = await getExam(accessToken, versionId);
            setExam(detail);
          } else {
            setSaveState('blocked');
          }
        } catch {
          setSaveState('blocked');
          setError('Không lưu được bản nháp.');
        }
      })();
    }, 1200);
    return () => window.clearTimeout(handle);
  }, [document, draft, canEdit, accessToken, versionId, runValidate]);

  const selectedQuestion = useMemo(() => {
    if (document === null || selection.kind !== 'question') return null;
    return document.sections[selection.section]?.parts[selection.part]?.questions[selection.question] ?? null;
  }, [document, selection]);

  const selectedPart = useMemo(() => {
    if (document === null || (selection.kind !== 'question' && selection.kind !== 'part')) return null;
    return document.sections[selection.section]?.parts[selection.part] ?? null;
  }, [document, selection]);

  if (loadError !== null || versionId === undefined) {
    return (
      <header className="cms-head">
        <h1>Không mở được trình soạn</h1>
        <p>{loadError ?? 'Thiếu mã đề.'}</p>
        <Link to={AdminPaths.myExams}>Về đề của tôi</Link>
      </header>
    );
  }

  if (exam === null || document === null) {
    return <p className="cms-muted">Đang tải bản nháp…</p>;
  }

  const notesForQuestion =
    selection.kind === 'question'
      ? (exam.reviewNotes ?? []).filter((note) => note.anchor === selectedQuestion?.id)
      : [];

  const readOnly = !draft || !canEdit;
  const saveLabel =
    saveState === 'saving'
      ? 'Đang lưu…'
      : saveState === 'blocked'
        ? error !== null
          ? 'Không lưu được — thử lại'
          : 'Chưa lưu — bảng kiểm còn lỗi'
        : saveState === 'unsaved'
          ? 'Chưa lưu'
          : savedAt !== null
            ? `Đã lưu ${formatAdminDate(savedAt, 'datetime') ?? ''}`
            : 'Tự lưu khi schema hợp lệ';

  return (
    <div className="cms-builder" data-pane={pane}>
      <header className="cms-builder-head">
        <div>
          <nav className="cms-crumbs" aria-label="Đường dẫn">
            <Link to={AdminPaths.myExams}>Đề của tôi</Link>
            <span aria-hidden="true">›</span>
            <span>{exam.title}</span>
          </nav>
          <h1>{exam.title}</h1>
          <p>
            <StatusBadge status={exam.status} /> v{exam.versionNumber}
            {exam.createdByName !== undefined && exam.createdByName !== null && (
              <> · Người soạn: {exam.createdByName}</>
            )}
          </p>
          <p className="cms-builder-save" aria-live="polite" aria-atomic="true">
            {saveLabel}
          </p>
        </div>
        <div className="cms-builder-actions">
          <button
            type="button"
            className="cms-secondary"
            disabled={!draft || !canEdit || saveState === 'saving'}
            onClick={() => {
              if (document === null || accessToken === null || versionId === undefined) return;
              void saveExamContent(accessToken, versionId, document).then(async (result) => {
                setFindings(result.findings);
                setValid(result.valid);
                if (result.valid) {
                  setSavedAt(new Date().toISOString());
                  setSaveState('idle');
                } else {
                  setSaveState('blocked');
                }
              });
            }}
          >
            Lưu nháp
          </button>
          <button
            type="button"
            className="cms-secondary"
            disabled={!operator.can('exam.preview')}
            onClick={() => setPreviewOpen((open) => !open)}
          >
            Xem thử như học viên
          </button>
          <button
            type="button"
            className="cms-primary"
            disabled={!draft || !canEdit || !valid || submitting || operator.previewing}
            onClick={() => {
              if (accessToken === null || versionId === undefined) return;
              setSubmitting(true);
              void submitExam(accessToken, versionId)
                .then(() => navigate(AdminPaths.myExams))
                .catch(() => setError('Không nộp duyệt được.'))
                .finally(() => setSubmitting(false));
            }}
          >
            Nộp duyệt
          </button>
        </div>
      </header>

      {error !== null && (
        <p className="cms-alert is-bad" role="alert">
          {error}
        </p>
      )}

      {!draft && (
        <p className="cms-alert" role="status">
          Chỉ bản nháp mới sửa được. Mở vòng đời đề nếu cần rút về nháp.
        </p>
      )}

      <div className="cms-builder-panes" role="tablist" aria-label="Khu vực trình soạn">
        <button
          type="button"
          role="tab"
          aria-selected={pane === 'structure'}
          className={pane === 'structure' ? 'cms-secondary is-active' : 'cms-secondary'}
          onClick={() => setPane('structure')}
        >
          Cấu trúc
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={pane === 'editor'}
          className={pane === 'editor' ? 'cms-secondary is-active' : 'cms-secondary'}
          onClick={() => setPane('editor')}
        >
          Soạn
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={pane === 'check'}
          className={pane === 'check' ? 'cms-secondary is-active' : 'cms-secondary'}
          onClick={() => setPane('check')}
        >
          Kiểm tra
        </button>
      </div>

      <div className="cms-builder-grid">
        <StructureTree
          document={document}
          selection={selection}
          readOnly={readOnly}
          onSelect={(next) => {
            setSelection(next);
            setPane('editor');
          }}
          onAddSection={() => setPickModule(true)}
          onAddPart={(section) =>
            setDocument((current) => {
              if (current === null) return current;
              const next = addPart(current, section);
              const partIndex = (next.sections[section]?.parts.length ?? 1) - 1;
              setSelection({ kind: 'part', section, part: partIndex });
              setPane('editor');
              return next;
            })
          }
          onAddQuestion={(section, part) => setPickPreset({ section, part })}
        />

        <section className="cms-builder-main">
          {selection.kind === 'section' && (
            <SectionMeta
              document={document}
              sectionIndex={selection.section}
              readOnly={readOnly}
              onDuration={(seconds) => {
                const module = document.sections[selection.section]?.module;
                if (module === undefined) return;
                setDocument((current) =>
                  current === null ? current : setSectionDuration(current, module, seconds),
                );
              }}
              onTransfer={(seconds) =>
                setDocument((current) =>
                  current === null ? current : setListeningTransfer(current, seconds),
                )
              }
              onSpeaking={(part, response, prep) =>
                setDocument((current) =>
                  current === null ? current : setSpeakingPartTiming(current, part, response, prep),
                )
              }
            />
          )}

          {selection.kind === 'part' && selectedPart !== null && (
            <label className="cms-field">
              <span>Tiêu đề phần / đoạn</span>
              <input
                value={selectedPart.title ?? ''}
                disabled={readOnly}
                onChange={(event) =>
                  setDocument((current) =>
                    current === null
                      ? current
                      : updatePart(current, selection.section, selection.part, {
                          title: event.target.value,
                          body: selectedPart.body,
                        }),
                  )
                }
              />
              <span className="cms-field">
                <span>Nội dung đoạn / đề bài</span>
                <textarea
                  rows={8}
                  value={selectedPart.body ?? ''}
                  disabled={readOnly}
                  onChange={(event) =>
                    setDocument((current) =>
                      current === null
                        ? current
                        : updatePart(current, selection.section, selection.part, {
                            body: event.target.value,
                          }),
                    )
                  }
                />
              </span>
            </label>
          )}

          {selection.kind === 'question' && selectedQuestion !== null && selectedPart !== null && (
            <>
              {notesForQuestion.length > 0 && (
                <ul className="cms-review-notes">
                  {notesForQuestion.map((note) => (
                    <li key={note.id}>
                      <strong>{note.authorName}</strong>
                      <p>{note.body}</p>
                    </li>
                  ))}
                </ul>
              )}
              <PresetEditor
                question={selectedQuestion}
                part={selectedPart}
                media={media}
                readOnly={readOnly}
                onQuestion={(patch) =>
                  setDocument((current) =>
                    current === null
                      ? current
                      : updateQuestion(current, selection.section, selection.part, selection.question, patch),
                  )
                }
                onPart={(patch) =>
                  setDocument((current) =>
                    current === null
                      ? current
                      : updatePart(current, selection.section, selection.part, patch),
                  )
                }
              />
            </>
          )}

          {selection.kind === 'exam' && (
            <p className="cms-muted">Chọn một phần hoặc một câu trong Cấu trúc để soạn.</p>
          )}
        </section>

        <ChecklistPanel
          findings={findings}
          validating={saveState === 'saving'}
          assets={exam.assets ?? []}
          document={document}
          onSelectFinding={(pointer) => {
            setSelection(selectionFromPointer(pointer));
            setPane('editor');
          }}
        />
      </div>

      {previewOpen && accessToken !== null && (
        <section className="cms-panel">
          <h2>Xem thử như học viên</h2>
          <ExamPreview accessToken={accessToken} examVersionId={exam.examVersionId} />
        </section>
      )}

      {pickModule && (
        <>
          <div className="cms-scrim" onClick={() => setPickModule(false)} aria-hidden="true" />
          <div className="cms-dialog" role="dialog" aria-labelledby="pick-module">
            <h2 id="pick-module">Thêm kỹ năng</h2>
            <p className="cms-muted">Mỗi kỹ năng một lần. Thời lượng không có mặc định IELTS — nhập số giây.</p>
            <div className="cms-actions">
              {MODULES.filter((module) => !document.sections.some((section) => section.module === module)).map(
                (module) => (
                  <button
                    key={module}
                    type="button"
                    className="cms-secondary"
                    onClick={() => {
                      setDocument((current) => {
                        if (current === null) return current;
                        const next = addSection(current, module);
                        setSelection({ kind: 'section', section: next.sections.length - 1 });
                        setPane('editor');
                        return next;
                      });
                      setPickModule(false);
                    }}
                  >
                    {module}
                  </button>
                ),
              )}
            </div>
            <button type="button" className="cms-link-inline" onClick={() => setPickModule(false)}>
              Huỷ
            </button>
          </div>
        </>
      )}

      {pickPreset !== null && (
        <>
          <div className="cms-scrim" onClick={() => setPickPreset(null)} aria-hidden="true" />
          <div className="cms-dialog cms-dialog-wide" role="dialog" aria-labelledby="pick-preset">
            <h2 id="pick-preset">Chọn khuôn soạn</h2>
            <ul className="cms-preset-list">
              {presetsFor(document.sections[pickPreset.section]?.module ?? 'reading').map((preset) => (
                <li key={preset.id}>
                  <button
                    type="button"
                    className="cms-secondary"
                    onClick={() => {
                      setDocument((current) =>
                        current === null
                          ? current
                          : addQuestion(current, pickPreset.section, pickPreset.part, preset.id as QuestionPresetId),
                      );
                      const part = document.sections[pickPreset.section]?.parts[pickPreset.part];
                      setSelection({
                        kind: 'question',
                        section: pickPreset.section,
                        part: pickPreset.part,
                        question: part?.questions.length ?? 0,
                      });
                      setPane('editor');
                      setPickPreset(null);
                    }}
                  >
                    {preset.label}
                  </button>
                </li>
              ))}
            </ul>
            <button type="button" className="cms-link-inline" onClick={() => setPickPreset(null)}>
              Huỷ
            </button>
          </div>
        </>
      )}
    </div>
  );
}

function SectionMeta({
  document,
  sectionIndex,
  readOnly,
  onDuration,
  onTransfer,
  onSpeaking,
}: {
  document: ExamDocument;
  sectionIndex: number;
  readOnly: boolean;
  onDuration: (seconds: number | undefined) => void;
  onTransfer: (seconds: number | undefined) => void;
  onSpeaking: (part: number, response: number | undefined, prep: number | undefined) => void;
}) {
  const section = document.sections[sectionIndex];
  if (section === undefined) return null;
  const timing = document.timingProfile.sections[section.module];

  if (section.module === 'speaking') {
    return (
      <div>
        <p>Thời gian Speaking — nhập từng phần, không dùng số IELTS có sẵn.</p>
        {[1, 2, 3].map((part) => {
          const row = timing?.parts?.find((item) => item.part === part);
          return (
            <label className="cms-field" key={part}>
              <span>Part {part} — giây trả lời</span>
              <input
                type="number"
                min={1}
                disabled={readOnly}
                value={row?.responseSeconds ?? ''}
                onChange={(event) => {
                  const parsed = Number(event.target.value);
                  onSpeaking(
                    part,
                    event.target.value === '' || !Number.isFinite(parsed) ? undefined : parsed,
                    row?.prepSeconds,
                  );
                }}
              />
            </label>
          );
        })}
      </div>
    );
  }

  return (
    <div>
      <label className="cms-field">
        <span>Thời lượng (giây) — không có mặc định 60 phút</span>
        <input
          type="number"
          min={1}
          disabled={readOnly}
          value={timing?.durationSeconds ?? ''}
          onChange={(event) => {
            const parsed = Number(event.target.value);
            onDuration(event.target.value === '' || !Number.isFinite(parsed) ? undefined : parsed);
          }}
        />
      </label>
      {section.module === 'listening' && (
        <label className="cms-field">
          <span>Thời gian chuyển đáp án (giây), nếu đề có</span>
          <input
            type="number"
            min={0}
            disabled={readOnly}
            value={timing?.transferTimeSeconds ?? ''}
            onChange={(event) => {
              const parsed = Number(event.target.value);
              onTransfer(event.target.value === '' || !Number.isFinite(parsed) ? undefined : parsed);
            }}
          />
        </label>
      )}
    </div>
  );
}
