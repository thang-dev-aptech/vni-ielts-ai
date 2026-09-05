import 'fake-indexeddb/auto';

import { IDBFactory } from 'fake-indexeddb';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import {
  CaptureError,
  type CapturePermission,
  type CaptureResult,
  type SpeakingAudioCapture,
} from '@vni/speaking-audio';
import { I18nProvider } from '../../i18n/index.js';
import { resetDraftConnection, rememberDraft } from './recordingDraft.js';
import { SpeakingRecorder } from './SpeakingRecorder.js';

/**
 * Component states for FS8.4 — permission, queue, upload progress, re-record —
 * driven through the `capture` test seam so jsdom never needs a real mic.
 */

vi.mock('../auth/AuthContext.js', () => ({
  useAuth: () => ({
    accessToken: 'access-token',
    status: 'signed-in',
    user: null,
    signIn: async () => undefined,
    adoptSession: async () => undefined,
    refreshUser: async () => undefined,
    signOut: () => undefined,
  }),
}));

class FakeCapture implements SpeakingAudioCapture {
  readonly kind = 'web' as const;
  permission: CapturePermission = 'granted';
  permissionError: CaptureError | null = null;
  startError: CaptureError | null = null;
  blob = new Blob(['spoken'], { type: 'audio/webm' });
  stream: MediaStream | null = {
    getTracks: () => [],
  } as unknown as MediaStream;
  started = false;

  async checkPermission(): Promise<CapturePermission> {
    return this.permission;
  }

  async requestPermission(): Promise<CapturePermission> {
    if (this.permissionError) throw this.permissionError;
    return this.permission;
  }

  async start(): Promise<void> {
    if (this.startError) throw this.startError;
    this.started = true;
  }

  stop(): Promise<CaptureResult> {
    this.started = false;
    return Promise.resolve({
      blob: this.blob,
      fileUri: null,
      contentType: 'audio/webm',
      durationMs: 1_200,
    });
  }

  async cancel(): Promise<void> {
    this.started = false;
  }

  onInterruption(): () => void {
    return () => undefined;
  }

  getInputStream(): MediaStream | null {
    return this.stream;
  }
}

function mount(props: {
  capture?: FakeCapture;
  storedId?: string | null;
  prepSeconds?: number;
  responseSeconds?: number;
  onStored?: (id: string) => void;
}) {
  const capture = props.capture ?? new FakeCapture();
  const onStored = props.onStored ?? vi.fn();

  render(
    <I18nProvider>
      <SpeakingRecorder
        sessionId="sit-1"
        questionId="s-part-2"
        prepSeconds={props.prepSeconds ?? 0}
        responseSeconds={props.responseSeconds ?? 120}
        storedId={props.storedId ?? null}
        disabled={false}
        onStored={onStored}
        capture={capture}
      />
    </I18nProvider>,
  );

  return { capture, onStored };
}

function mockUploadApi(options: { initStatus?: number; putFail?: boolean } = {}) {
  const initStatus = options.initStatus ?? 200;

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      const method = init?.method ?? 'GET';

      if (url.includes('/recordings/init') && method === 'POST') {
        if (initStatus === 503) {
          return new Response(null, { status: 503 });
        }
        return new Response(
          JSON.stringify({
            uploadId: 'up-1',
            recordingId: 'rec-1',
            uploadUrl: 'https://storage.test/put',
            contentType: 'audio/webm',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }

      if (url.includes('/complete') && method === 'POST') {
        return new Response(JSON.stringify({ recordingId: 'rec-1' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }

      // Legacy multipart POST …/recordings (no /init or /complete suffix).
      if (/\/recordings\/?$/.test(url) && method === 'POST') {
        return new Response(JSON.stringify({ recordingId: 'rec-multipart' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }

      return new Response(JSON.stringify({ code: 'NOT_FOUND' }), { status: 404 });
    }),
  );

  // putWithProgress uses XHR, not fetch.
  class FakeXHR {
    status = options.putFail ? 500 : 200;
    statusText = options.putFail ? 'Error' : 'OK';
    response = null;
    upload = {
      onprogress: null as ((event: ProgressEvent) => void) | null,
    };
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    onabort: (() => void) | null = null;

    open() {}
    setRequestHeader() {}
    send() {
      queueMicrotask(() => {
        this.upload.onprogress?.({
          lengthComputable: true,
          loaded: 50,
          total: 100,
        } as ProgressEvent);
        this.upload.onprogress?.({
          lengthComputable: true,
          loaded: 100,
          total: 100,
        } as ProgressEvent);
        if (options.putFail) this.onerror?.();
        else this.onload?.();
      });
    }
  }

  vi.stubGlobal('XMLHttpRequest', FakeXHR as unknown as typeof XMLHttpRequest);
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('vni.locale', 'vi');
  resetDraftConnection();
  // eslint-disable-next-line no-global-assign -- test isolation
  indexedDB = new IDBFactory();
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
  mockUploadApi();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  resetDraftConnection();
});

it('states the permission hint and speaking budget before recording', () => {
  mount({ prepSeconds: 60, responseSeconds: 120 });

  expect(screen.getByRole('button', { name: 'Bắt đầu chuẩn bị' })).toBeInTheDocument();
  expect(screen.getByText(/Chuẩn bị 01:00/)).toBeInTheDocument();
  expect(
    screen.getByText(/Trình duyệt sẽ hỏi quyền micro trước khi đồng hồ/),
  ).toBeInTheDocument();
});

it('shows how to grant the mic when permission is refused', async () => {
  const capture = new FakeCapture();
  capture.permission = 'denied';
  mount({ capture });

  await userEvent.click(screen.getByRole('button', { name: 'Bắt đầu ghi âm' }));

  expect(await screen.findByRole('alert')).toHaveTextContent(/Chưa được cấp quyền micro/);
  expect(screen.getByText(/ổ khoá cạnh địa chỉ/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Thử lại' })).toBeInTheDocument();
});

it('uploads through init → PUT → complete and offers re-record when stored', async () => {
  const { onStored } = mount({});

  await userEvent.click(screen.getByRole('button', { name: 'Bắt đầu ghi âm' }));
  await userEvent.click(await screen.findByRole('button', { name: 'Dừng' }));

  expect(await screen.findByText('Đã lưu bản ghi')).toBeInTheDocument();
  expect(onStored).toHaveBeenCalledWith('rec-1');
  expect(screen.getByRole('button', { name: 'Ghi lại từ đầu' })).toBeInTheDocument();
});

it('falls back to multipart when init returns 503', async () => {
  mockUploadApi({ initStatus: 503 });
  const { onStored } = mount({});

  await userEvent.click(screen.getByRole('button', { name: 'Bắt đầu ghi âm' }));
  await userEvent.click(await screen.findByRole('button', { name: 'Dừng' }));

  expect(await screen.findByText('Đã lưu bản ghi')).toBeInTheDocument();
  expect(onStored).toHaveBeenCalledWith('rec-multipart');
});

it('queues the recording when offline and sends on reconnect', async () => {
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: false });
  const { onStored } = mount({});

  await userEvent.click(screen.getByRole('button', { name: 'Bắt đầu ghi âm' }));
  await userEvent.click(await screen.findByRole('button', { name: 'Dừng' }));

  expect(await screen.findByText(/Bản ghi đang giữ trên máy/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Gửi lại bản ghi' })).toBeInTheDocument();
  expect(onStored).not.toHaveBeenCalled();

  Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
  await act(async () => {
    window.dispatchEvent(new Event('online'));
  });

  await waitFor(() => expect(onStored).toHaveBeenCalledWith('rec-1'));
  expect(screen.getByText('Đã lưu bản ghi')).toBeInTheDocument();
});

it('restores an IndexedDB draft after remount and offers send again', async () => {
  await rememberDraft({
    sessionId: 'sit-1',
    questionId: 's-part-2',
    blob: new Blob(['held'], { type: 'audio/webm' }),
    mimeType: 'audio/webm',
    savedAt: Date.now(),
  });

  mount({});

  expect(await screen.findByText(/Bản ghi đang giữ trên máy/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Gửi lại bản ghi' })).toBeInTheDocument();
});

it('opens a server-held recording as stored with a re-record control', () => {
  mount({ storedId: 'rec-server' });

  expect(screen.getByText('Đã lưu bản ghi')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Ghi lại từ đầu' })).toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Bắt đầu ghi âm' })).toBeNull();
});
