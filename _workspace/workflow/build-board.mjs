import fs from 'node:fs';

const now = new Date().toISOString();

const t = (id, title, owner, dependsOn, phase, files = []) => ({
  id,
  title,
  owner,
  phase,
  status: 'todo',
  dependsOn,
  startedAt: null,
  lastHeartbeatAt: null,
  completedAt: null,
  files,
  tests: [],
  negativeProof: null,
  artifacts: [],
  blocker: null,
  nextDependency: null,
});

const tasks = [
  t('FS0.1', 'Content rights registry', 'backend-engineer', [], 'FS0', [
    'backend/src/Vni.Ielts.Domain/Content/',
    'backend/src/Vni.Ielts.Application/Content/',
    'backend/src/Vni.Ielts.Infrastructure/Content/',
    'backend/src/Vni.Ielts.Api/Endpoints/ContentEndpoints.cs',
  ]),
  t('FS0.2', 'Machine-readable content inventory', 'devops-engineer', [], 'FS0', [
    'scripts/content-inventory.mjs',
  ]),
  t('FS0.3', 'Product/config decisions as versioned data', 'domain-analyst', [], 'FS0', [
    'contracts/schemas/',
    'docs/domain/',
  ]),
  t('FS0.4', 'AI/R2 secret contract', 'security-engineer', [], 'FS0', [
    'backend/src/Vni.Ielts.Api/Common/StartupConfiguration.cs',
    'appsettings',
    'docs/security/',
  ]),
  t('FS0.5', 'Baseline executable', 'qa-engineer', [], 'FS0', [
    '_workspace/workflow/agents/qa-baseline.md',
  ]),

  t('FS1.1', 'Exam Package schema v2 + ResponseSlot', 'domain-analyst', ['FS0.3'], 'FS1', [
    'contracts/schemas/exam.schema.json',
  ]),
  t('FS1.2', 'Domain mapping ExamVersion..ResponseSlot', 'domain-analyst', ['FS1.1'], 'FS1', [
    'backend/src/Vni.Ielts.Domain/Exams/ExamContent.cs',
  ]),
  t('FS1.3', 'v1 compatibility + migration', 'backend-engineer', ['FS1.2'], 'FS1', [
    'backend/src/Vni.Ielts.Infrastructure/',
  ]),
  t('FS1.4', 'Package validation rules', 'backend-engineer', ['FS1.2'], 'FS1', [
    'backend/src/Vni.Ielts.Application/Exams/',
  ]),
  t('FS1.5', 'API/OpenAPI/client for slots', 'backend-engineer', ['FS1.3', 'FS1.4'], 'FS1', [
    'backend/src/Vni.Ielts.Api/Endpoints/ExamEndpoints.cs',
    'contracts/openapi/',
    'packages/api-client/',
  ]),

  t('FS2.1', 'Two separate import paths', 'backend-engineer', ['FS1.5'], 'FS2'),
  t('FS2.2', 'Safe source extraction (DOCX/PDF)', 'security-engineer', ['FS1.5'], 'FS2'),
  t('FS2.3', 'Provider-neutral AI parser', 'ai-evaluation-engineer', ['FS2.1'], 'FS2'),
  t('FS2.4', 'CMS review workflow', 'frontend-engineer', ['FS2.1'], 'FS2', ['apps/admin/src/']),
  t('FS2.5', 'Pilot VOL 9 Test 1 package', 'domain-analyst', ['FS2.1', 'FS2.2', 'FS0.1'], 'FS2', [
    'fixtures/exams/',
  ]),
  t('FS2.6', 'Cambridge batch readiness', 'backend-engineer', ['FS2.5'], 'FS2'),

  t('FS3.1', 'PracticeUnit projection', 'backend-engineer', ['FS1.5'], 'FS3'),
  t('FS3.2', 'Catalogue API', 'backend-engineer', ['FS3.1'], 'FS3'),
  t('FS3.3', 'Start-session contract', 'backend-engineer', ['FS3.1'], 'FS3'),
  t('FS3.4', 'Session part state', 'backend-engineer', ['FS3.3'], 'FS3', [
    'backend/src/Vni.Ielts.Domain/Sessions/ExamSession.cs',
  ]),
  t('FS3.5', 'History separation', 'backend-engineer', ['FS3.4'], 'FS3'),

  t('FS4.1', 'Runner shell', 'frontend-engineer', ['FS3.2', 'FS3.3'], 'FS4', [
    'apps/web/src/features/exam/practice-runner/',
  ]),
  t('FS4.2', 'Header/timer', 'frontend-engineer', ['FS4.1'], 'FS4'),
  t('FS4.3', 'Footer by ResponseSlot', 'frontend-engineer', ['FS4.1'], 'FS4'),
  t('FS4.4', 'Reading layout', 'frontend-engineer', ['FS4.1'], 'FS4'),
  t('FS4.5', 'Listening layout + audio', 'frontend-engineer', ['FS4.1'], 'FS4'),
  t('FS4.6', 'Question renderers + a11y', 'frontend-engineer', ['FS4.1'], 'FS4'),
  t('FS4.7', 'Autosave/offline per slot', 'frontend-engineer', ['FS3.4', 'FS4.1'], 'FS4'),

  t('FS5.1', 'Slot-based scorer', 'domain-analyst', ['FS1.2'], 'FS5', [
    'backend/src/Vni.Ielts.Domain/Exams/DeterministicScorer.cs',
  ]),
  t('FS5.2', 'Result contract', 'backend-engineer', ['FS5.1'], 'FS5'),
  t('FS5.3', 'Canonical explanation', 'ai-evaluation-engineer', ['FS2.3', 'FS5.2'], 'FS5'),
  t('FS5.4', 'Personalized explanation', 'ai-evaluation-engineer', ['FS5.3'], 'FS5'),
  t('FS5.5', 'Evidence safety', 'ai-evaluation-engineer', ['FS5.3'], 'FS5'),
  t('FS5.6', 'Failure semantics', 'backend-engineer', ['FS5.2', 'FS5.3'], 'FS5'),

  t('FS6.1', 'Versioned rubric data', 'domain-analyst', ['FS0.3'], 'FS6'),
  t('FS6.2', 'Writing editor', 'frontend-engineer', ['FS4.1'], 'FS6'),
  t('FS6.3', 'OpenAI adapter', 'ai-evaluation-engineer', ['FS6.1'], 'FS6'),
  t('FS6.4', 'Gemini adapter', 'ai-evaluation-engineer', ['FS6.1'], 'FS6'),
  t('FS6.5', 'Server-side validation of AI marking', 'backend-engineer', ['FS6.3', 'FS6.4'], 'FS6'),
  t('FS6.6', 'Task/full Writing result', 'backend-engineer', ['FS6.5'], 'FS6'),
  t('FS6.7', 'Initial evaluation set', 'ai-evaluation-engineer', ['FS6.5'], 'FS6'),
  t('FS6.8', 'Production controls', 'devops-engineer', ['FS6.5'], 'FS6'),

  t('FS7.1', 'Package-driven sequence', 'backend-engineer', ['FS3.4'], 'FS7'),
  t('FS7.2', 'Transition rules', 'backend-engineer', ['FS7.1'], 'FS7'),
  t('FS7.3', 'Deadlines', 'backend-engineer', ['FS7.2'], 'FS7'),
  t('FS7.4', 'Result aggregation', 'backend-engineer', ['FS7.2', 'FS5.2', 'FS6.6'], 'FS7'),
  t('FS7.5', 'Resume/recovery', 'frontend-engineer', ['FS7.2'], 'FS7'),

  t('FS8.1', 'Recording contract v2', 'backend-engineer', ['FS3.4'], 'FS8'),
  t('FS8.2', 'R2-compatible private store', 'devops-engineer', ['FS8.1', 'FS0.4'], 'FS8'),
  t('FS8.3', 'Resumable/retry upload', 'backend-engineer', ['FS8.2'], 'FS8'),
  t('FS8.4', 'Web recorder', 'frontend-engineer', ['FS8.1'], 'FS8'),
  t('FS8.5', 'Native seam', 'mobile-engineer', ['FS8.4'], 'FS8'),
  t('FS8.6', 'Retention/deletion/reconciliation', 'security-engineer', ['FS8.2'], 'FS8'),
  t('FS8.7', 'No-voice result state', 'backend-engineer', ['FS8.3', 'FS7.4'], 'FS8'),

  t('FS9.1', 'Security/privacy hardening', 'security-engineer', ['FS7.4', 'FS8.7', 'FS6.6'], 'FS9'),
  t('FS9.2', 'Accessibility/responsive', 'frontend-engineer', ['FS4.6', 'FS6.2', 'FS8.4'], 'FS9'),
  t('FS9.3', 'Performance/reliability', 'devops-engineer', ['FS7.4', 'FS8.7'], 'FS9'),
  t('FS9.4', 'Full regression', 'qa-engineer', ['FS9.1', 'FS9.2', 'FS9.3'], 'FS9'),
  t('FS9.5', 'Operational docs', 'devops-engineer', ['FS9.3'], 'FS9'),
  t('FS9.6', 'Final report', 'workflow-orchestrator', ['FS9.4', 'FS9.5'], 'FS9'),
];

const board = {
  workflow: 'dynamic-project-plan',
  version: 2,
  runId: 'fscore-' + now.slice(0, 10).replace(/-/g, ''),
  plan: 'docs/development/four-skills-functional-core-todolist.md',
  status: 'in_progress',
  baselineCommit: null,
  currentPhase: 'FS0',
  certificationTarget: 'Functional Core Ready - Speaking AI deferred',
  carriedBlockers: [
    {
      id: 'R19',
      summary:
        'F4.4 CodeQL static-analysis gate cannot run; Foundation Ready withheld. External/owner decision, not code. Feature work proceeds per the workflow-orchestrator standing rule; carried into the final report.',
    },
  ],
  openBusinessDecisions: [
    {
      id: 'rights-registry-entries',
      summary:
        'Owner has not named which sources carry learner-production rights. Registry ships with every source defaulting to fixture-only and the publish endpoint refusing. Not a code blocker.',
    },
  ],
  tasks,
  agents: {},
  updatedAt: now,
  notes:
    'Orchestrator owns this file, the plan checklist, and the report. Teammates write _workspace/workflow/agents/<name>.md only.',
};

// Trailing newline: _workspace/ is NOT in .prettierignore (only *.md is), so this
// file is checked by `pnpm format:check`. Without the newline the board fails the
// formatting gate and looks like a real style regression.
fs.writeFileSync('_workspace/workflow/task-board.json', JSON.stringify(board, null, 2) + '\n');
console.log('tasks written:', tasks.length);
