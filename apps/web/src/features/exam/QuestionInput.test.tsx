import { useState } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it } from 'vitest';
import { I18nProvider } from '../../i18n/index.js';
import { ANSWER_BANK_DRAG_MIME, QuestionInput } from './QuestionInput.js';
import type { QuestionView } from './examApi.js';

const options = [
  { key: 'A', text: 'Alpha' },
  { key: 'B', text: 'Beta' },
  { key: 'C', text: 'Gamma' },
];

function question(type: string, withOptions = options): QuestionView {
  return {
    id: `q-${type}`,
    order: 17,
    type,
    prompt: 'Renderer prompt',
    options: withOptions,
    maxWords: type === 'completion' ? 2 : null,
    group: null,
    slots: [{ id: `slot-${type}`, number: 17 }],
  };
}

function Controlled({ item }: { item: QuestionView }) {
  const [value, setValue] = useState<string | null>(null);
  return (
    <I18nProvider>
      <span id="renderer-name">17 Renderer prompt</span>
      <QuestionInput
        question={item}
        value={value}
        disabled={false}
        labelledBy="renderer-name"
        onChange={setValue}
      />
      <output data-testid="value">{value ?? ''}</output>
    </I18nProvider>
  );
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
});

it.each([
  ['true-false-notgiven', ['TRUE', 'FALSE', 'NOT GIVEN']],
  ['yes-no-notgiven', ['YES', 'NO', 'NOT GIVEN']],
] as const)('renders the canonical %s radio group', (type, labels) => {
  render(<Controlled item={question(type, [])} />);

  const group = screen.getByRole('radiogroup', { name: /Renderer prompt/ });
  expect(within(group).getAllByRole('radio')).toHaveLength(3);
  for (const label of labels)
    expect(within(group).getByRole('radio', { name: label })).toBeVisible();
});

it('renders a multiple-choice question as one radio choice', async () => {
  render(<Controlled item={question('multiple-choice')} />);

  await userEvent.click(screen.getByRole('radio', { name: /B Beta/ }));

  expect(screen.getByTestId('value')).toHaveTextContent('B');
  expect(screen.getByRole('radio', { name: /B Beta/ })).toBeChecked();
});

it('renders multiple-select as checkboxes with deterministic pipe ordering', async () => {
  render(<Controlled item={question('multiple-select')} />);

  await userEvent.click(screen.getByRole('checkbox', { name: /C Gamma/ }));
  await userEvent.click(screen.getByRole('checkbox', { name: /A Alpha/ }));

  expect(screen.getByTestId('value')).toHaveTextContent('A|C');
});

it('locks the remaining options once a multiple-select is full, and unlocks on untick', async () => {
  /*
   * `[QUYẾT ĐỊNH]` chủ sản phẩm 08/09/2026: "chọn 2 trong 5 đáp án thì chọn 2
   * đáp án xong sẽ không cho chọn nữa". The count is the paper's own — the
   * two answer-sheet lines the server publishes as `slots`.
   */
  const twoSlots: QuestionView = {
    ...question('multiple-select'),
    slots: [
      { id: 'slot-17', number: 17 },
      { id: 'slot-18', number: 18 },
    ],
  };
  render(<Controlled item={twoSlots} />);

  expect(screen.getByText('Chọn 2 đáp án · đã chọn 0/2')).toBeVisible();

  await userEvent.click(screen.getByRole('checkbox', { name: /A Alpha/ }));
  await userEvent.click(screen.getByRole('checkbox', { name: /C Gamma/ }));

  expect(screen.getByTestId('value')).toHaveTextContent('A|C');
  expect(screen.getByText('Chọn 2 đáp án · đã chọn 2/2')).toBeVisible();
  expect(screen.getByRole('checkbox', { name: /B Beta/ })).toBeDisabled();

  // A third pick is refused; unticking one re-opens the rest.
  await userEvent.click(screen.getByRole('checkbox', { name: /B Beta/ }));
  expect(screen.getByTestId('value')).toHaveTextContent('A|C');

  await userEvent.click(screen.getByRole('checkbox', { name: /A Alpha/ }));
  expect(screen.getByRole('checkbox', { name: /B Beta/ })).toBeEnabled();
  await userEvent.click(screen.getByRole('checkbox', { name: /B Beta/ }));
  expect(screen.getByTestId('value')).toHaveTextContent('B|C');
});

it('a single-slot multiple-select applies no cap it cannot know', async () => {
  render(<Controlled item={question('multiple-select')} />);

  await userEvent.click(screen.getByRole('checkbox', { name: /A Alpha/ }));
  await userEvent.click(screen.getByRole('checkbox', { name: /B Beta/ }));
  await userEvent.click(screen.getByRole('checkbox', { name: /C Gamma/ }));

  expect(screen.getByTestId('value')).toHaveTextContent('A|B|C');
  expect(screen.queryByText(/đã chọn/)).toBeNull();
});

it('renders completion as an uncorrected text input with its word limit', async () => {
  render(<Controlled item={question('completion', [])} />);

  const input = screen.getByRole('textbox', { name: /Renderer prompt/ });
  expect(input).toHaveAttribute('spellcheck', 'false');
  expect(input).toHaveAttribute('autocorrect', 'off');
  expect(screen.getByText('Tối đa 2 từ')).toBeVisible();
  await userEvent.type(input, 'map');
  expect(screen.getByTestId('value')).toHaveTextContent('map');
});

it.each(['matching', 'labelling'])('%s offers tap and drop-target paths without a select', async (type) => {
  render(<Controlled item={question(type)} />);

  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });
  await userEvent.click(within(bank).getByRole('button', { name: /B Beta/ }));

  const target = screen.getByRole('button', { name: /Renderer prompt/ });
  expect(target).toHaveClass('is-pending');
  expect(screen.queryByRole('combobox')).toBeNull();

  await userEvent.click(target);

  expect(screen.getByTestId('value')).toHaveTextContent('B');
  // The box says what was dropped in it the way a person reads it — "Beta",
  // not "B — Beta". The key is still what is saved.
  expect(screen.getByRole('button', { name: /Renderer prompt/ })).toHaveTextContent(/^Beta$/);
  expect(screen.getByRole('button', { name: /Renderer prompt/ })).toHaveClass('is-filled');
  expect(screen.queryByRole('combobox')).toBeNull();
});

it.each(['matching', 'labelling'])('%s assigns by drag-and-drop onto the drop target', async (type) => {
  render(<Controlled item={question(type)} />);

  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });
  const option = within(bank).getByRole('button', { name: /C Gamma/ });
  const target = screen.getByRole('button', { name: /Renderer prompt/ });

  const data = new Map<string, string>();
  const dataTransfer = {
    effectAllowed: 'none',
    setData: (typeName: string, value: string) => data.set(typeName, value),
    getData: (typeName: string) => data.get(typeName) ?? '',
  };

  fireEvent.dragStart(option, { dataTransfer });
  fireEvent.dragOver(target, { dataTransfer });
  fireEvent.drop(target, { dataTransfer });

  expect(screen.getByTestId('value')).toHaveTextContent('C');
  expect(target).toHaveTextContent(/^Gamma$/);
  expect(target).toHaveClass('is-filled');
  expect(screen.queryByRole('combobox')).toBeNull();
});

it.each(['matching', 'labelling'])('%s assigns by keyboard only', async (type) => {
  render(<Controlled item={question(type)} />);

  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });
  const option = within(bank).getByRole('button', { name: /A Alpha/ });
  option.focus();
  await userEvent.keyboard('{Enter}');

  const target = screen.getByRole('button', { name: /Renderer prompt/ });
  expect(target).toHaveClass('is-pending');
  target.focus();
  await userEvent.keyboard('{Enter}');

  expect(screen.getByTestId('value')).toHaveTextContent('A');
  expect(target).toHaveTextContent(/^Alpha$/);
  expect(screen.queryByRole('combobox')).toBeNull();
});

it('a bank of bare letters keeps the letter in the box', async () => {
  const letters = [
    { key: 'A', text: 'A' },
    { key: 'B', text: 'B' },
  ];
  render(<Controlled item={question('labelling', letters)} />);

  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });
  await userEvent.click(within(bank).getByRole('button', { name: /B/ }));
  await userEvent.click(screen.getByRole('button', { name: /Renderer prompt/ }));

  expect(screen.getByRole('button', { name: /Renderer prompt/ })).toHaveTextContent(/^B$/);
});

it('clears a filled drop target when activated with no bank selection', async () => {
  render(<Controlled item={question('matching')} />);

  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });
  const target = screen.getByRole('button', { name: /Renderer prompt/ });
  await userEvent.click(within(bank).getByRole('button', { name: /A Alpha/ }));
  await userEvent.click(target);
  expect(screen.getByTestId('value')).toHaveTextContent('A');

  await userEvent.click(target);
  expect(screen.getByTestId('value')).toHaveTextContent('');
  expect(target).not.toHaveClass('is-filled');
});

it('ignores a drop whose key is not in the question bank', async () => {
  render(<Controlled item={question('matching')} />);

  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });
  const target = screen.getByRole('button', { name: /Renderer prompt/ });
  const option = within(bank).getByRole('button', { name: /A Alpha/ });

  const data = new Map<string, string>();
  const dataTransfer = {
    effectAllowed: 'none',
    setData: (type: string, value: string) => data.set(type, value),
    getData: (type: string) => data.get(type) ?? '',
  };

  fireEvent.dragStart(option, { dataTransfer });
  fireEvent.dragOver(target, { dataTransfer });
  fireEvent.drop(target, { dataTransfer });
  expect(screen.getByTestId('value')).toHaveTextContent('A');
  expect(target).toHaveTextContent(/^Alpha$/);

  data.set(
    ANSWER_BANK_DRAG_MIME,
    JSON.stringify({ scopeId: 'question:q-matching', key: 'not-in-bank' }),
  );
  fireEvent.drop(target, { dataTransfer });
  expect(screen.getByTestId('value')).toHaveTextContent('A');
  expect(target).toHaveTextContent(/^Alpha$/);
});

it('ignores a valid key dragged from a different answer bank', () => {
  render(<Controlled item={question('matching')} />);

  const target = screen.getByRole('button', { name: /Renderer prompt/ });
  const data = new Map<string, string>([
    [
      ANSWER_BANK_DRAG_MIME,
      JSON.stringify({ scopeId: 'question:some-other-question', key: 'A' }),
    ],
    ['text/plain', 'A'],
  ]);
  const dataTransfer = {
    effectAllowed: 'copy',
    setData: (type: string, value: string) => data.set(type, value),
    getData: (type: string) => data.get(type) ?? '',
  };

  fireEvent.dragOver(target, { dataTransfer });
  fireEvent.drop(target, { dataTransfer });

  expect(screen.getByTestId('value')).toHaveTextContent('');
  expect(target).not.toHaveClass('is-filled');
});

it('keeps essay spellcheck off and surfaces under-min as text, not colour alone', () => {
  render(
    <I18nProvider>
      <span id="essay-name">Task 2</span>
      <QuestionInput
        question={{
          ...question('essay-task', []),
          maxWords: null,
        }}
        value="short draft"
        disabled={false}
        labelledBy="essay-name"
        onChange={() => {}}
      />
      <p className="word-count is-short">
        <span className="num">2 từ</span>
        <span>Còn thiếu 148 từ</span>
      </p>
    </I18nProvider>,
  );

  expect(screen.getByRole('textbox', { name: 'Task 2' })).toHaveAttribute('spellcheck', 'false');
  expect(screen.getByText('Còn thiếu 148 từ')).toBeVisible();
  expect(screen.getByText('2 từ')).toBeVisible();
});
