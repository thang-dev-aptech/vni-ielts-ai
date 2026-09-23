import { expect, it } from 'vitest';
import { sharesAnswerBank } from './QuestionList.js';

it('shares a bank only when option keys and labels both match', () => {
  expect(
    sharesAnswerBank([
      { options: [{ key: 'A', text: 'Africa' }] },
      { options: [{ key: 'A', text: 'Africa' }] },
    ]),
  ).toBe(true);

  // Reusing `A` does not make these the same choice. Collapsing the rows onto
  // the first bank would show "Africa" and save the second row's "Apple".
  expect(
    sharesAnswerBank([
      { options: [{ key: 'A', text: 'Africa' }] },
      { options: [{ key: 'A', text: 'Apple' }] },
    ]),
  ).toBe(false);
});
