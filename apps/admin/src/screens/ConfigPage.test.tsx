import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ApiError } from '@vni/auth';
import type { AdminRuntimeConfiguration } from '../lib/adminApi.js';

/**
 * The live Config screen — `config-admin-ui`.
 *
 * `useAdminAuth` and the API module are mocked. What is under test is "given
 * this payload, do the four panels tell the truth" — not the session or the
 * HTTP transport. Route wiring stays with admin-route-composition.
 */

vi.mock('../lib/AdminAuth.js', () => ({
  useAdminAuth: () => ({ accessToken: 'token-1' }),
}));

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    getRuntimeConfiguration: vi.fn(),
  };
});

const { getRuntimeConfiguration } = await import('../lib/adminApi.js');
const { ConfigPage } = await import('./ConfigPage.js');

function payload(overrides: Partial<AdminRuntimeConfiguration> = {}): AdminRuntimeConfiguration {
  return {
    ai: {
      skills: [
        {
          skill: 'writing-marking',
          status: 'available',
          provider: 'openai',
          model: 'gpt-4.1',
          version: 'writing-v3',
          fallback: { provider: 'gemini', status: 'available', model: 'gemini-2.5-pro' },
        },
        {
          skill: 'import-parser',
          status: 'unavailable',
          provider: null,
          model: null,
          version: null,
          fallback: null,
        },
      ],
    },
    writing: {
      rubricVersion: 'ielts-writing-synthetic-v1',
      task1Weight: 1,
      task2Weight: 2,
      feedbackLanguage: 'vi',
      criterionGranularity: 'per-criterion',
    },
    importArchive: {
      maxEntries: 200,
      maxTotalUncompressedBytes: 200 * 1024 * 1024,
      maxEntryUncompressedBytes: 50 * 1024 * 1024,
      maxCompressionRatio: 20,
      maxArchiveBytes: 80 * 1024 * 1024,
      extractionTimeoutSeconds: 30,
    },
    tokenPricing: { status: 'pending', blockers: ['B-5a', 'B-5b'] },
    ...overrides,
  };
}

beforeEach(() => {
  vi.mocked(getRuntimeConfiguration).mockReset();
});

describe('ConfigPage', () => {
  it('shows configured and unconfigured skills with safe model and version data', async () => {
    vi.mocked(getRuntimeConfiguration).mockResolvedValue(payload());
    render(<ConfigPage />);

    expect(await screen.findByText('Đã cấu hình')).toBeInTheDocument();
    expect(screen.getByText('Chưa cấu hình')).toBeInTheDocument();
    expect(screen.getByText('gpt-4.1')).toBeInTheDocument();
    expect(screen.getByText('writing-v3')).toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('shows writing rubric, returned 1:2 weights, language and granularity', async () => {
    vi.mocked(getRuntimeConfiguration).mockResolvedValue(payload());
    render(<ConfigPage />);

    expect(await screen.findByText('ielts-writing-synthetic-v1')).toBeInTheDocument();
    expect(screen.getByText('1 : 2')).toBeInTheDocument();
    expect(screen.getByText('vi')).toBeInTheDocument();
    expect(screen.getByText('per-criterion')).toBeInTheDocument();
  });

  it('does not invent import zeroes when the fetch fails', async () => {
    vi.mocked(getRuntimeConfiguration).mockRejectedValue(
      new ApiError({
        title: 'Forbidden',
        status: 403,
        detail: 'This account does not hold config.read.',
        code: 'PERMISSION_DENIED',
      }),
    );
    render(<ConfigPage />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Không đọc được cấu hình');
    expect(screen.queryByText('0 B')).not.toBeInTheDocument();
    expect(screen.queryByText('0 giây')).not.toBeInTheDocument();
    expect(screen.getByText(/B-5a/)).toBeInTheDocument();
    expect(screen.getByText(/B-5b/)).toBeInTheDocument();
  });

  it('renders import caps with readable units when the payload arrives', async () => {
    vi.mocked(getRuntimeConfiguration).mockResolvedValue(payload());
    render(<ConfigPage />);

    expect(await screen.findByText('200')).toBeInTheDocument();
    expect(screen.getByText('20:1')).toBeInTheDocument();
    expect(screen.getByText('30 giây')).toBeInTheDocument();
    expect(screen.getByText('80.0 MB')).toBeInTheDocument();
  });

  it('keeps two live configuration sections usable while token pricing stays Pending on B-5a/B-5b', async () => {
    vi.mocked(getRuntimeConfiguration).mockResolvedValue(payload());
    render(<ConfigPage />);

    // Token panel alone is Pending — and names the blockers. No invented price.
    expect(await screen.findByText('Pending.')).toBeInTheDocument();
    expect(screen.getByText(/B-5a và B-5b/)).toBeInTheDocument();
    expect(screen.queryByText(/\d+\s*(VNI|token)/i)).not.toBeInTheDocument();

    // Two real sections still carry live values from the payload.
    expect(screen.getByText('Nhà cung cấp theo kỹ năng')).toBeInTheDocument();
    expect(screen.getByText('Đã cấu hình')).toBeInTheDocument();
    expect(screen.getByText('gpt-4.1')).toBeInTheDocument();

    expect(screen.getByRole('heading', { name: 'Chấm Writing' })).toBeInTheDocument();
    expect(screen.getByText('ielts-writing-synthetic-v1')).toBeInTheDocument();
    expect(screen.getByText('1 : 2')).toBeInTheDocument();
  });

  it('keeps the token panel Pending on B-5a/B-5b while the rest of the screen is usable', async () => {
    vi.mocked(getRuntimeConfiguration).mockResolvedValue(payload());
    render(<ConfigPage />);

    expect(await screen.findByText('Nhà cung cấp theo kỹ năng')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Chấm Writing' })).toBeInTheDocument();
    expect(screen.getByText('Hạn mức gói nhập')).toBeInTheDocument();
    expect(screen.getByText('Pending.')).toBeInTheDocument();
    expect(screen.getByText(/B-5a và B-5b/)).toBeInTheDocument();
  });

  it('announces loading before the payload arrives', () => {
    vi.mocked(getRuntimeConfiguration).mockReturnValue(new Promise(() => undefined));
    render(<ConfigPage />);
    expect(screen.getByText('Đang tải cấu hình…')).toBeInTheDocument();
  });
});
