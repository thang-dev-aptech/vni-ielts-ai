import { useState } from 'react';
import type {
  CandidateBandBoundaryPayload,
  CandidateCompletionPayload,
  CandidatePartCompletionPayload,
  CandidateSectionTimingPayload,
  CandidateSpeakingPartTimingPayload,
  ParsedCandidateDetail,
} from '../lib/adminApi.js';

export interface CompletionFinding {
  stage?: string;
  code?: string;
  pointer?: string;
  path?: string;
  message?: string;
  severity?: string;
}

export interface CandidateCompletionFormProps {
  candidate: ParsedCandidateDetail;
  canCreate: boolean;
  busy: boolean;
  error: string | null;
  findings?: CompletionFinding[] | null;
  onSubmit: (payload: CandidateCompletionPayload) => void;
}

const DEFAULT_RAW_TO_BAND: CandidateBandBoundaryPayload[] = [
  { minRaw: 0, band: 0.0 },
  { minRaw: 10, band: 4.0 },
  { minRaw: 16, band: 5.0 },
  { minRaw: 23, band: 6.0 },
  { minRaw: 30, band: 7.0 },
  { minRaw: 35, band: 8.0 },
  { minRaw: 39, band: 9.0 },
];

export function CandidateCompletionForm({
  candidate,
  canCreate,
  busy,
  error,
  findings,
  onSubmit,
}: CandidateCompletionFormProps) {
  const [variant, setVariant] = useState<'academic' | 'general'>('academic');

  // Timings per module
  const [timingMinutes, setTimingMinutes] = useState<Record<string, number>>(() => {
    const initial: Record<string, number> = {};
    for (const m of candidate.modules) {
      const mod = m.module ?? 'reading';
      if (mod === 'reading') initial[mod] = 60;
      else if (mod === 'listening') initial[mod] = 30;
      else if (mod === 'writing') initial[mod] = 60;
      else if (mod === 'speaking') initial[mod] = 15;
      else initial[mod] = 60;
    }
    return initial;
  });

  const [transferMinutes, setTransferMinutes] = useState<Record<string, number>>(() => {
    const initial: Record<string, number> = {};
    for (const m of candidate.modules) {
      const mod = m.module ?? '';
      if (mod === 'listening') initial[mod] = 2;
    }
    return initial;
  });

  // Speaking parts
  const hasSpeaking = candidate.modules.some((m) => m.module === 'speaking');
  const [speakingParts] = useState<CandidateSpeakingPartTimingPayload[]>([
    { part: 1, prepSeconds: 0, responseSeconds: 300 },
    { part: 2, prepSeconds: 60, responseSeconds: 120 },
    { part: 3, prepSeconds: 0, responseSeconds: 300 },
  ]);

  // Scoring
  const hasReadingOrListening = candidate.modules.some(
    (m) => m.module === 'reading' || m.module === 'listening',
  );
  const hasWriting = candidate.modules.some((m) => m.module === 'writing');

  const [task1Weight, setTask1Weight] = useState(0.33);
  const [task2Weight, setTask2Weight] = useState(0.67);
  const [speakingRef, setSpeakingRef] = useState('ielts-speaking-standard');

  // Asset refs per part
  const [partAssets, setPartAssets] = useState<
    Record<number, { audioAssetRef: string; imageAssetRef: string; kind: string }>
  >(() => {
    const initial: Record<
      number,
      { audioAssetRef: string; imageAssetRef: string; kind: string }
    > = {};
    let order = 1;
    for (const m of candidate.modules) {
      for (const p of m.parts) {
        initial[p.order || order] = {
          audioAssetRef: '',
          imageAssetRef: '',
          kind:
            m.module === 'reading'
              ? 'passage'
              : m.module === 'listening'
                ? 'section'
                : m.module === 'writing'
                  ? 'task'
                  : 'part',
        };
        order++;
      }
    }
    return initial;
  });

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!canCreate || busy) return;

    const sections: Record<string, CandidateSectionTimingPayload> = {};
    for (const m of candidate.modules) {
      const mod = m.module ?? 'reading';
      const durationSec = (timingMinutes[mod] ?? 60) * 60;
      const transferSec = transferMinutes[mod] ? transferMinutes[mod] * 60 : undefined;
      sections[mod] = {
        durationSeconds: durationSec,
        transferTimeSeconds: transferSec,
      };
    }

    const rawToBand: Record<string, CandidateBandBoundaryPayload[]> = {};
    if (hasReadingOrListening) {
      for (const m of candidate.modules) {
        if (m.module === 'reading' || m.module === 'listening') {
          rawToBand[m.module] = DEFAULT_RAW_TO_BAND;
        }
      }
    }

    const partDetails: CandidatePartCompletionPayload[] = [];
    for (const m of candidate.modules) {
      for (const p of m.parts) {
        const config = partAssets[p.order];
        partDetails.push({
          partOrder: p.order,
          kind: config?.kind || undefined,
          taskNumber: m.module === 'writing' ? p.order : undefined,
          partNumber: m.module === 'speaking' ? p.order : undefined,
          audioAssetRef: config?.audioAssetRef.trim() || undefined,
          imageAssetRef: config?.imageAssetRef.trim() || undefined,
        });
      }
    }

    const payload: CandidateCompletionPayload = {
      variant,
      timingProfile: {
        sections,
        speakingParts: hasSpeaking ? speakingParts : undefined,
      },
      scoringProfile: {
        rawToBand: Object.keys(rawToBand).length > 0 ? rawToBand : undefined,
        scoringProfileRef: hasSpeaking ? speakingRef : undefined,
        criterionWeights: hasWriting
          ? { task1: task1Weight, task2: task2Weight }
          : undefined,
      },
      partDetails: partDetails.length > 0 ? partDetails : undefined,
    };

    onSubmit(payload);
  }

  return (
    <form className="cms-panel" onSubmit={handleSubmit} aria-label="Hoàn thiện tạo bản nháp">
      <h2>Hoàn thiện thông số chuẩn hoá để tạo bản nháp (Draft)</h2>
      <p className="cms-muted">
        Đề thi đã qua bước rà soát nội dung. Nhập các thông số kỹ thuật (phân hệ, thời gian,
        thang điểm) để bộ chuyển đổi canonical biên dịch và tạo bản nháp chính thức.
      </p>

      {error !== null && (
        <div className="cms-alert is-bad" role="alert">
          <p>
            <strong>Lỗi:</strong> {error}
          </p>
          {findings && findings.length > 0 && (
            <ul style={{ marginTop: '0.5rem', paddingLeft: '1.25rem' }}>
              {findings.map((f, i) => (
                <li key={i}>
                  <code>{f.pointer || f.path || f.code || 'Lỗi kiểm tra'}</code>: {f.message}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <fieldset className="cms-field" style={{ border: 'none', padding: 0 }}>
        <legend style={{ fontWeight: 600, marginBottom: '0.5rem' }}>Phân hệ thi (Variant)</legend>
        <div style={{ display: 'flex', gap: '1.5rem' }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <input
              type="radio"
              name="variant"
              value="academic"
              checked={variant === 'academic'}
              onChange={() => setVariant('academic')}
              disabled={busy || !canCreate}
            />
            <span>Academic (Học thuật)</span>
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <input
              type="radio"
              name="variant"
              value="general"
              checked={variant === 'general'}
              onChange={() => setVariant('general')}
              disabled={busy || !canCreate}
            />
            <span>General Training (Đào tạo chung)</span>
          </label>
        </div>
      </fieldset>

      <div style={{ marginTop: '1rem' }}>
        <h3 style={{ fontSize: '1rem', fontWeight: 600, marginBottom: '0.5rem' }}>
          Thời gian thi từng phần (Timing Profile)
        </h3>
        {candidate.modules.map((m) => {
          const mod = m.module ?? 'reading';
          return (
            <div
              key={mod}
              style={{
                display: 'flex',
                gap: '1rem',
                alignItems: 'center',
                marginBottom: '0.5rem',
              }}
            >
              <span style={{ minWidth: '100px', textTransform: 'capitalize', fontWeight: 500 }}>
                {mod}:
              </span>
              <label className="cms-field" style={{ margin: 0, flexDirection: 'row', alignItems: 'center', gap: '0.5rem' }}>
                <span>Thời lượng (phút):</span>
                <input
                  type="number"
                  min="1"
                  max="180"
                  style={{ width: '80px' }}
                  value={timingMinutes[mod] ?? 60}
                  disabled={busy || !canCreate}
                  onChange={(e) =>
                    setTimingMinutes((prev) => ({
                      ...prev,
                      [mod]: parseInt(e.target.value, 10) || 1,
                    }))
                  }
                />
              </label>
              {mod === 'listening' && (
                <label className="cms-field" style={{ margin: 0, flexDirection: 'row', alignItems: 'center', gap: '0.5rem' }}>
                  <span>Chuyển đáp án (phút):</span>
                  <input
                    type="number"
                    min="0"
                    max="30"
                    style={{ width: '80px' }}
                    value={transferMinutes[mod] ?? 2}
                    disabled={busy || !canCreate}
                    onChange={(e) =>
                      setTransferMinutes((prev) => ({
                        ...prev,
                        [mod]: parseInt(e.target.value, 10) || 0,
                      }))
                    }
                  />
                </label>
              )}
            </div>
          );
        })}
      </div>

      <div style={{ marginTop: '1rem' }}>
        <h3 style={{ fontSize: '1rem', fontWeight: 600, marginBottom: '0.5rem' }}>
          Thang điểm &amp; Tiêu chí chấm (Scoring Profile)
        </h3>
        {hasReadingOrListening && (
          <p className="cms-muted" style={{ marginBottom: '0.5rem' }}>
            Reading / Listening: Áp dụng bảng chuyển đổi Raw-to-Band chuẩn IELTS (0 đến 40 câu hỏi, band 0 - 9.0).
          </p>
        )}
        {hasWriting && (
          <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', marginBottom: '0.5rem' }}>
            <span>Trọng số Writing:</span>
            <label className="cms-field" style={{ margin: 0, flexDirection: 'row', alignItems: 'center', gap: '0.5rem' }}>
              <span>Task 1:</span>
              <input
                type="number"
                step="0.01"
                min="0"
                max="1"
                style={{ width: '80px' }}
                value={task1Weight}
                disabled={busy || !canCreate}
                onChange={(e) => setTask1Weight(parseFloat(e.target.value) || 0)}
              />
            </label>
            <label className="cms-field" style={{ margin: 0, flexDirection: 'row', alignItems: 'center', gap: '0.5rem' }}>
              <span>Task 2:</span>
              <input
                type="number"
                step="0.01"
                min="0"
                max="1"
                style={{ width: '80px' }}
                value={task2Weight}
                disabled={busy || !canCreate}
                onChange={(e) => setTask2Weight(parseFloat(e.target.value) || 0)}
              />
            </label>
          </div>
        )}
        {hasSpeaking && (
          <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', marginBottom: '0.5rem' }}>
            <span>Mã tiêu chí Speaking:</span>
            <input
              type="text"
              style={{ width: '220px' }}
              value={speakingRef}
              disabled={busy || !canCreate}
              onChange={(e) => setSpeakingRef(e.target.value)}
            />
          </div>
        )}
      </div>

      <div style={{ marginTop: '1rem' }}>
        <h3 style={{ fontSize: '1rem', fontWeight: 600, marginBottom: '0.5rem' }}>
          Liên kết tài nguyên từng phần (Media &amp; Assets)
        </h3>
        {candidate.modules.map((m) =>
          m.parts.map((p) => {
            const config = partAssets[p.order] ?? {
              audioAssetRef: '',
              imageAssetRef: '',
              kind: '',
            };
            return (
              <div
                key={p.order}
                style={{
                  display: 'flex',
                  gap: '1rem',
                  alignItems: 'center',
                  marginBottom: '0.5rem',
                  padding: '0.5rem',
                  background: 'var(--cms-bg-subtle, rgba(0,0,0,0.02))',
                  borderRadius: '4px',
                }}
              >
                <span style={{ minWidth: '140px', fontWeight: 500 }}>
                  Part {p.order} ({m.module}):
                </span>
                {m.module === 'listening' && (
                  <label className="cms-field" style={{ margin: 0, flexDirection: 'row', alignItems: 'center', gap: '0.5rem' }}>
                    <span>Audio ref:</span>
                    <input
                      type="text"
                      placeholder="e.g. audio/part-1.mp3"
                      value={config.audioAssetRef}
                      disabled={busy || !canCreate}
                      onChange={(e) =>
                        setPartAssets((prev) => ({
                          ...prev,
                          [p.order]: { ...config, audioAssetRef: e.target.value },
                        }))
                      }
                    />
                  </label>
                )}
                <label className="cms-field" style={{ margin: 0, flexDirection: 'row', alignItems: 'center', gap: '0.5rem' }}>
                  <span>Ảnh ref:</span>
                  <input
                    type="text"
                    placeholder="e.g. images/diagram.png"
                    value={config.imageAssetRef}
                    disabled={busy || !canCreate}
                    onChange={(e) =>
                      setPartAssets((prev) => ({
                        ...prev,
                        [p.order]: { ...config, imageAssetRef: e.target.value },
                      }))
                    }
                  />
                </label>
              </div>
            );
          }),
        )}
      </div>

      <div className="cms-version-actions" style={{ marginTop: '1.5rem' }}>
        <button
          type="submit"
          className="cms-primary"
          disabled={busy || !canCreate}
          title={!canCreate ? 'Cần quyền exam.create để tạo bản nháp' : undefined}
        >
          {busy ? 'Đang xử lý…' : 'Tạo bản nháp đề thi (Create Draft)'}
        </button>
      </div>
    </form>
  );
}
