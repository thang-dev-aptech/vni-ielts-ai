import type { BuilderSelection, ExamDocument } from '../../lib/examDocument.js';
import { questionComplete } from '../../lib/examDocument.js';
import { presetOf } from '../../lib/questionPresets.js';

const MODULE_LABEL: Record<string, string> = {
  reading: 'Reading',
  listening: 'Listening',
  writing: 'Writing',
  speaking: 'Speaking',
};

export function StructureTree({
  document,
  selection,
  readOnly = false,
  onSelect,
  onAddSection,
  onAddPart,
  onAddQuestion,
}: {
  document: ExamDocument;
  selection: BuilderSelection;
  readOnly?: boolean;
  onSelect: (selection: BuilderSelection) => void;
  onAddSection: () => void;
  onAddPart: (section: number) => void;
  onAddQuestion: (section: number, part: number) => void;
}) {
  return (
    <nav className="cms-builder-tree" aria-label="Cấu trúc đề">
      <p className="cms-nav-title">Cấu trúc</p>
      {document.sections.length === 0 && (
        <p className="cms-muted">Chưa có phần nào. Thêm Reading, Listening, Writing hoặc Speaking.</p>
      )}
      <ul className="cms-tree">
        {document.sections.map((section, sectionIndex) => (
          <li key={section.module}>
            <button
              type="button"
              className={treeClass(selection.kind === 'section' && selection.section === sectionIndex)}
              aria-current={selection.kind === 'section' && selection.section === sectionIndex ? 'true' : undefined}
              onClick={() => onSelect({ kind: 'section', section: sectionIndex })}
            >
              {MODULE_LABEL[section.module] ?? section.module}
            </button>
            <ul>
              {section.parts.map((part, partIndex) => (
                <li key={`${section.module}-${part.order}`}>
                  <button
                    type="button"
                    className={treeClass(
                      selection.kind === 'part' &&
                        selection.section === sectionIndex &&
                        selection.part === partIndex,
                    )}
                    aria-current={
                      selection.kind === 'part' &&
                      selection.section === sectionIndex &&
                      selection.part === partIndex
                        ? 'true'
                        : undefined
                    }
                    onClick={() => onSelect({ kind: 'part', section: sectionIndex, part: partIndex })}
                  >
                    {part.title?.trim() || `Phần ${part.order}`}
                  </button>
                  <ul>
                    {part.questions.map((question, questionIndex) => (
                      <li key={question.id}>
                        <button
                          type="button"
                          className={treeClass(
                            selection.kind === 'question' &&
                              selection.section === sectionIndex &&
                              selection.part === partIndex &&
                              selection.question === questionIndex,
                          )}
                          aria-current={
                            selection.kind === 'question' &&
                            selection.section === sectionIndex &&
                            selection.part === partIndex &&
                            selection.question === questionIndex
                              ? 'true'
                              : undefined
                          }
                          onClick={() =>
                            onSelect({
                              kind: 'question',
                              section: sectionIndex,
                              part: partIndex,
                              question: questionIndex,
                            })
                          }
                        >
                          {questionComplete(question) ? '✓' : '●'} Câu {question.order}
                          {question.preset !== undefined && (
                            <span className="cms-muted"> {presetOf(question.preset)?.label ?? question.preset}</span>
                          )}
                        </button>
                      </li>
                    ))}
                  </ul>
                  {!readOnly && (
                    <button
                      type="button"
                      className="cms-link-inline"
                      onClick={() => onAddQuestion(sectionIndex, partIndex)}
                    >
                      + Thêm câu hỏi
                    </button>
                  )}
                </li>
              ))}
            </ul>
            {!readOnly && (
              <button type="button" className="cms-link-inline" onClick={() => onAddPart(sectionIndex)}>
                + Thêm phần
              </button>
            )}
          </li>
        ))}
      </ul>
      {!readOnly && (
        <button type="button" className="cms-secondary" onClick={onAddSection}>
          + Thêm kỹ năng
        </button>
      )}
    </nav>
  );
}

function treeClass(active: boolean): string {
  return `cms-tree-item${active ? ' is-active' : ''}`;
}
