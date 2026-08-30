import { useState } from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, expect, it } from 'vitest';
import { I18nProvider } from '../../i18n/index.js';
import { QuestionInput } from './QuestionInput.js';
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

it('renders completion as an uncorrected text input with its word limit', async () => {
  render(<Controlled item={question('completion', [])} />);

  const input = screen.getByRole('textbox', { name: /Renderer prompt/ });
  expect(input).toHaveAttribute('spellcheck', 'false');
  expect(input).toHaveAttribute('autocorrect', 'off');
  expect(screen.getByText('Tối đa 2 từ')).toBeVisible();
  await userEvent.type(input, 'map');
  expect(screen.getByTestId('value')).toHaveTextContent('map');
});

it.each(['matching', 'labelling'])('%s offers tap and native-select paths', async (type) => {
  render(<Controlled item={question(type)} />);

  const bank = screen.getByRole('list', { name: 'Ngân hàng đáp án' });
  await userEvent.click(within(bank).getByRole('button', { name: /B Beta/ }));
  await userEvent.click(screen.getByRole('button', { name: /Renderer prompt/ }));

  expect(screen.getByTestId('value')).toHaveTextContent('B');
  expect(screen.getByRole('combobox', { name: /Renderer prompt/ })).toHaveValue('B');
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
