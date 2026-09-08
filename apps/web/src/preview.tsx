/**
 * A development-only harness for eyeballing the sitting and result screens.
 *
 * <b>Not part of the app.</b> `preview.html` is its own Vite entry and nothing
 * in `src/main.tsx` imports this file, so it never reaches a production bundle.
 * It exists because the two screens it renders were cloned from a supplied
 * reference and the only way to check a clone is to put it beside the original
 * at the same viewport — which needs a signed-in session and a submitted paper,
 * neither of which a developer has on hand.
 *
 * Every response below is a fixture. Nothing here talks to the API.
 */
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App.js';
import '@vni/design-system/index.css';

const NOW = new Date();
const STARTED = new Date(NOW.getTime() - 3492_000).toISOString(); // 58:12 ago

const passage = `For seven decades, Georgia O'Keeffe (1887-1986) was a major figure in American art. Remarkably, she remained independent from shifting art trends and her work continues to be admired around the world.

Born in 1887 near Sun Prairie, Wisconsin to cattle breeders Francis and Ida O'Keeffe, Georgia was raised on their farm (later painted in her work). After attending university and then training college, she became an art teacher and taught in elementary schools.

During this period, O'Keeffe began to experiment with creating abstract compositions in charcoal, and produced a series of radical drawings. Avant-garde artists and photographers were introduced to the American public, and her work attracted attention for its unusual style.

With Stieglitz's encouragement and promise of financial support, O'Keeffe arrived in New York in June 1918 to begin two solo exhibitions and numerous group installations. The two were married in 1924. The ups and downs of their relationship — and the striking black-and-white portraits of O'Keeffe, taken over the course of twenty years (1917-37) — are now well documented.

By the mid-1920s, O'Keeffe was recognized as one of America's most important and successful artists, widely known for the enlarged flower paintings that brought her national fame.

Enlarging the tiniest details to fill an entire metre-wide canvas emphasized their shapes and lines and made them appear abstract. Such daring compositions helped to establish her unique style.

In 1929, O'Keeffe made her first extended trip to the state of New Mexico. It was a visit that had a lasting impact on her work.

There, O'Keeffe found new inspiration: at first, it was the numerous sun-bleached bones she came across in the state's rugged terrain that sparked her imagination. Two of her earliest and most powerful works from this period were animal skulls, rendered in a striking and iconic manner.

However, it was the region's spectacular landscape, with its unusual geological formations, vivid colours, clarity of light and vast skies, that influenced her most profoundly. She began to incorporate these elements into her work, just as she had done with her botanical subjects.

O'Keeffe eventually owned two homes in New Mexico – the first, her summer retreat at Ghost Ranch, was nestled beneath 200-metre cliffs, while the second, used as her winter residence, was in the small town of Abiquiú. While both locales provided inspiration, it was the latter that she photographed and painted most frequently, and it was there that she entertained many artists, writers and friends.`;

const noteGroup = {
  id: 'g-notes',
  title: "The life and work of Georgia O'Keeffe",
  instruction: 'Complete the notes below. Choose ONE WORD ONLY from the passage for each answer.',
  imageKey: null,
  text: null,
  eachLetterOnce: false,
};

const verdictGroup = {
  id: 'g-tfng',
  title: null,
  instruction: 'Do the following statements agree with the information given in Reading Passage 1?',
  imageKey: null,
  text: null,
  eachLetterOnce: false,
};

const notes = [
  'Studied art, then worked as a _____ in various places in the USA',
  'Created drawings using _____ which were exhibited in New York City',
  "Moved to New York and became famous for her paintings of the city's _____",
  'Produced a series of innovative close-up paintings of _____',
  'Went to New Mexico and was initially inspired to paint the many _____ that could be found there',
  'Continued to paint various features that together formed the dramatic _____ of New Mexico for over forty years',
  'Travelled widely by plane in later years, and painted pictures of clouds and _____ seen from above',
].map((prompt, at) => ({
  id: `q-${at + 1}`,
  order: at + 1,
  type: 'note-completion',
  prompt,
  options: [],
  maxWords: 1,
  group: noteGroup,
  slots: [{ id: `s-${at + 1}`, number: at + 1 }],
}));

const verdicts = [
  "Georgia O'Keeffe's style was greatly influenced by the changing fashions in art over the seven decades of her career.",
  'She received financial support from Stieglitz before they got married.',
  "O'Keeffe's flower paintings made her known throughout the United States.",
  'Her first visit to New Mexico inspired her to paint animal skulls.',
  'She spent more time in Ghost Ranch than in Abiquiú.',
  "In her later years, O'Keeffe travelled mainly by car to paint the sky.",
].map((prompt, at) => ({
  id: `q-${at + 8}`,
  order: at + 8,
  type: 'true-false-notgiven',
  prompt,
  options: [],
  maxWords: null,
  group: verdictGroup,
  slots: [{ id: `s-${at + 8}`, number: at + 8 }],
}));

const questions = [...notes, ...verdicts];

const part = (order: number, title: string) => ({
  order,
  kind: 'passage',
  title,
  body: passage,
  audioKey: null,
  imageKey: null,
  taskNumber: null,
  partNumber: order,
  cueCard: null,
  minWords: null,
  questions: order === 1 ? questions : [],
});

const session = {
  sessionId: 'preview',
  examVersionId: 'exam-preview',
  examTitle: 'Đề 8',
  practiceUnitId: null,
  scope: null,
  completedPartIds: [],
  mode: 'full',
  status: 'inprogress',
  startedAt: NOW.toISOString(),
  serverNow: NOW.toISOString(),
  completedModules: [],
  moduleSequence: ['reading', 'listening', 'writing', 'speaking'],
  current: {
    module: 'reading',
    partId: null,
    startedAt: NOW.toISOString(),
    deadlineAt: new Date(NOW.getTime() + 3576_000).toISOString(), // 59:36
    remainingSeconds: 3576,
    elapsedSeconds: 24,
    running: true,
    targetSeconds: null,
    parts: [
      part(1, "Georgia O'Keeffe"),
      part(2, 'The Step Pyramid'),
      part(3, 'Attitudes to language'),
    ],
    answers: {},
    answerRevision: 1,
    speakingTiming: [],
    transferSeconds: null,
    audioPlayback: null,
  },
};

const answers = ['teacher', 'charcoal', 'buildings', 'flowers', 'bones', 'landscape', 'sky'];

const results = {
  sessionId: 'preview',
  examTitle: 'Cambridge IELTS 17 – Test 1 – Reading',
  mode: 'single',
  status: 'submitted',
  submittedAt: NOW.toISOString(),
  sections: [
    {
      module: 'reading',
      rawScore: 31,
      maxScore: 40,
      band: 6.5,
      bandVerified: true,
      questions: Array.from({ length: 40 }, (_, at) => ({
        questionId: `q-${at + 1}`,
        submitted: at % 6 === 1 ? 'B' : 'A',
        isCorrect: at % 6 !== 1,
        correctAnswer: at % 6 === 1 ? 'C' : 'A',
        canonicalExplanation: null,
      })),
    },
  ],
  markings: [],
  markingStatuses: [],
  explanationStatuses: [],
  overallBand: null,
  writingBand: null,
  writingBandReason: null,
  content: [
    {
      module: 'reading',
      submissions: {},
      parts: [
        {
          ...part(1, "Georgia O'Keeffe"),
          questions: Array.from({ length: 40 }, (_, at) => ({
            id: `q-${at + 1}`,
            order: at + 1,
            type:
              at < 10
                ? 'note-completion'
                : at < 22
                  ? 'true-false-notgiven'
                  : at < 32
                    ? 'matching'
                    : 'multiple-choice',
            prompt:
              [
                "The life and work of Georgia O'Keeffe",
                'Created drawings using …',
                'Moved to New York and became …',
                'Produced a series of innovative …',
                'Went to New Mexico and was …',
              ][at % 5] ?? `Câu ${at + 1}`,
            options: [],
            maxWords: null,
            group: null,
            slots: [{ id: `s-${at + 1}`, number: at + 1 }],
          })),
        },
      ],
    },
  ],
};

const sittings = {
  sittings: [
    {
      sessionId: 'preview',
      examVersionId: 'exam-preview',
      examTitle: 'Cambridge IELTS 17 – Test 1 – Reading',
      variant: 'academic',
      mode: 'single',
      status: 'submitted',
      startedAt: STARTED,
      submittedAt: NOW.toISOString(),
      currentModule: null,
      deadlineAt: null,
      sections: [{ module: 'reading', band: 6.5 }],
      overallBand: null,
    },
  ],
};

const documents = {
  items: [
    {
      id: 'd1',
      slug: 'tfng-strategy',
      title: 'Chiến lược làm dạng TRUE/FALSE/NOT GIVEN',
      description: '',
      skill: 'reading',
      category: 'Reading',
      type: 'guide',
      format: 'PDF',
      targetBand: '6.0',
      size: '1.2 MB',
      updatedAt: NOW.toISOString(),
      access: 'free',
    },
    {
      id: 'd2',
      slug: 'academic-vocab-art',
      title: 'Từ vựng học thuật theo chủ đề Nghệ thuật',
      description: '',
      skill: 'reading',
      category: 'Reading',
      type: 'pdf',
      format: 'PDF',
      targetBand: '6.5',
      size: '2.0 MB',
      updatedAt: NOW.toISOString(),
      access: 'free',
    },
    {
      id: 'd3',
      slug: 'advanced-reading',
      title: 'Luyện đọc hiểu nâng cao (Level 6.0 – 7.0)',
      description: '',
      skill: 'reading',
      category: 'Reading',
      type: 'practice',
      format: 'PDF',
      targetBand: '7.0+',
      size: '3.4 MB',
      updatedAt: NOW.toISOString(),
      access: 'free',
    },
  ],
};

const me = {
  userId: 'preview',
  displayName: 'Nguyễn Thị An',
  email: 'an@example.com',
  emailVerified: true,
  phone: null,
  permissions: ['exam.read'],
  providers: ['email'],
  hasPassword: true,
};

function reply(body: unknown) {
  return new Response(JSON.stringify(body), {
    status: 200,
    headers: { 'Content-Type': 'application/json', 'X-Server-Time': NOW.toISOString() },
  });
}

window.fetch = (async (input: RequestInfo | URL) => {
  const url = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url;

  if (url.includes('/me/usage')) return reply({ entries: [] });
  if (url.includes('/library/documents')) return reply(documents);
  if (url.includes('/sessions/preview/results')) return reply(results);
  if (url.includes('/sessions/preview')) {
    /* `/students/practice/preview` is luyện đề — an open stopwatch, no
       deadline. `/exam/preview` is the timed sitting. One fixture, two
       timings, chosen by the address being previewed. */
    const open = location.pathname.startsWith('/students/practice/');
    return reply(
      open
        ? {
            ...session,
            mode: 'single',
            current: { ...session.current, deadlineAt: null, remainingSeconds: null },
          }
        : session,
    );
  }
  if (url.includes('/api/v1/sessions')) return reply(sittings);
  if (url.includes('/api/v1/me')) return reply(me);
  return reply({});
}) as typeof fetch;

localStorage.setItem(
  'vni.session',
  JSON.stringify({
    accessToken: 'preview',
    accessTokenExpiresAt: new Date(NOW.getTime() + 3600_000).toISOString(),
    refreshToken: 'preview',
    refreshTokenExpiresAt: new Date(NOW.getTime() + 86_400_000).toISOString(),
    userId: 'preview',
    displayName: 'Nguyễn Thị An',
  }),
);

/* Seed a few answers so the footer map shows both states. */
void answers;

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
