import { useEffect, useRef, useState } from 'react';
import type { MouseEvent } from 'react';
import { useFlash } from '../chrome/Confirm.js';
import {
  fetchImportDraftAssetObjectUrl,
  setGroupPositions,
  type ImportDraft,
  type ImportDraftGroup,
  type ImportDraftPosition,
} from '../lib/adminApi.js';
import { reasonOf } from '../screens/UserDetailPage.js';

/**
 * One shared group's hotspot editor — a point marker per option, placed by
 * clicking the group's own image.
 *
 * <b>Point markers, not drawn regions.</b> The confirmed scope decision: a
 * placed option is a small pin that is the draggable bank token elsewhere,
 * not an authored shape. The per-question drop-target buttons the learner
 * client already renders are untouched by anything here.
 *
 * <b>The image is fetched, not linked.</b> Before approval the image exists
 * only in private staging (`admin-draft-asset-preview`) — never at the
 * learner-facing asset route — so an `<img src=...>` pointed at the API
 * directly could not carry the bearer token it needs. `<img src={blobUrl}>`
 * is the same technique `MediaLibraryPage` already uses for the same reason.
 *
 * <b>Selection is shared between the option palette and a placed pin.</b>
 * Clicking an option button or clicking an existing pin both set the same
 * "armed" key; the next click on the image places (or moves) it. That is
 * what "clicking a placed pin re-enters placement mode" means — there is
 * only one placement mode, entered two ways.
 *
 * <b>Save replaces the whole set, and the caller must replace its whole
 * draft with the response.</b> `SetGroupPositionsAsync` writes the identical
 * positions array into every occurrence of this group id and resets
 * `approvalState`/`checklistConfirmed` like any other edit — so a save here
 * is content, not a metadata tweak, and the parent screen's `draft` state
 * must move forward with it rather than being patched in place.
 */
export function GroupPositionEditor({
  accessToken,
  draftId,
  group,
  onSaved,
}: {
  accessToken: string;
  draftId: string;
  group: ImportDraftGroup;
  onSaved: (draft: ImportDraft) => void;
}) {
  const { flash, say } = useFlash();

  const [pending, setPending] = useState<Record<string, { x: number; y: number }>>(() =>
    toRecord(group.positions),
  );
  const [armedKey, setArmedKey] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const [imageUrl, setImageUrl] = useState<string | null>(null);
  const [imageError, setImageError] = useState<string | null>(null);
  const objectUrl = useRef<string | null>(null);

  // A save (or a different draft loading) hands back a fresh `group` — local
  // state re-syncs from it rather than staying stuck on what was pending
  // before the request went out.
  useEffect(() => {
    setPending(toRecord(group.positions));
    setArmedKey(null);
  }, [group.id, group.positions]);

  useEffect(() => {
    let cancelled = false;
    setImageError(null);

    if (group.imageKey === null) {
      setImageUrl(null);
      return;
    }

    fetchImportDraftAssetObjectUrl(accessToken, draftId, group.imageKey)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        if (objectUrl.current !== null) URL.revokeObjectURL(objectUrl.current);
        objectUrl.current = url;
        setImageUrl(url);
      })
      .catch((error: unknown) => {
        if (!cancelled) setImageError(reasonOf(error));
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accessToken, draftId, group.imageKey]);

  useEffect(
    () => () => {
      if (objectUrl.current !== null) URL.revokeObjectURL(objectUrl.current);
    },
    [],
  );

  // Server-side filtering already keeps a group with no image out of
  // `draft.groups`; this is the same guarantee held locally rather than
  // trusted blindly.
  if (group.imageKey === null) return null;

  function toggleArm(key: string) {
    setArmedKey((current) => (current === key ? null : key));
  }

  function onImageClick(event: MouseEvent<HTMLImageElement>) {
    if (armedKey === null) return;
    const rect = event.currentTarget.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return;

    const x = clamp01((event.clientX - rect.left) / rect.width);
    const y = clamp01((event.clientY - rect.top) / rect.height);

    setPending((prev) => ({ ...prev, [armedKey]: { x, y } }));
    setArmedKey(null);
  }

  function remove(key: string) {
    setPending((prev) => {
      const next = { ...prev };
      delete next[key];
      return next;
    });
    setArmedKey((current) => (current === key ? null : current));
  }

  async function save() {
    setSaving(true);
    try {
      const positions: ImportDraftPosition[] = Object.entries(pending).map(([key, at]) => ({
        key,
        x: at.x,
        y: at.y,
      }));
      const updated = await setGroupPositions(accessToken, draftId, group.id, positions);
      say({ tone: 'ok', text: 'Đã lưu vị trí.' });
      onSaved(updated);
    } catch (error) {
      say({ tone: 'bad', text: reasonOf(error) });
    } finally {
      setSaving(false);
    }
  }

  const placedCount = Object.keys(pending).length;

  return (
    <div className="cms-panel">
      <div className="cms-panel-head">
        <h3>{group.title ?? group.id}</h3>
        <span className="cms-badge is-hold">
          {placedCount}/{group.options.length} đã đặt
        </span>
      </div>

      {flash}

      <div className="cms-version-actions">
        {group.options.map((option) => (
          <button
            key={option.key}
            type="button"
            className="cms-secondary"
            aria-pressed={armedKey === option.key}
            onClick={() => toggleArm(option.key)}
          >
            {option.key} — {option.text}
            {pending[option.key] !== undefined ? ' ✓' : ''}
          </button>
        ))}
      </div>

      {imageError !== null && <p className="cms-muted">{imageError}</p>}

      {imageUrl !== null && (
        <div style={{ position: 'relative', display: 'inline-block' }}>
          <img
            src={imageUrl}
            alt={group.title ?? group.id}
            style={{
              display: 'block',
              maxWidth: '100%',
              cursor: armedKey !== null ? 'crosshair' : 'default',
            }}
            onClick={onImageClick}
          />
          {Object.entries(pending).map(([key, at]) => (
            <button
              key={key}
              type="button"
              aria-label={`Vị trí ${key} — bấm để đặt lại`}
              aria-pressed={armedKey === key}
              onClick={(event) => {
                event.stopPropagation();
                toggleArm(key);
              }}
              style={{
                position: 'absolute',
                left: `${at.x * 100}%`,
                top: `${at.y * 100}%`,
                transform: 'translate(-50%, -50%)',
              }}
              className="cms-badge is-ready"
            >
              {key}
            </button>
          ))}
        </div>
      )}

      {placedCount > 0 && (
        <ul className="cms-notes">
          {Object.entries(pending).map(([key, at]) => (
            <li key={key}>
              <strong>{key}</strong> — {Math.round(at.x * 100)}%, {Math.round(at.y * 100)}%{' '}
              <button type="button" className="cms-secondary" onClick={() => remove(key)}>
                Xoá
              </button>
            </li>
          ))}
        </ul>
      )}

      <div className="cms-version-actions">
        <button
          type="button"
          className="cms-primary"
          disabled={saving}
          onClick={() => void save()}
        >
          {saving ? 'Đang lưu…' : 'Lưu vị trí'}
        </button>
      </div>
    </div>
  );
}

function toRecord(
  positions: ImportDraftPosition[] | null,
): Record<string, { x: number; y: number }> {
  const record: Record<string, { x: number; y: number }> = {};
  for (const position of positions ?? []) record[position.key] = { x: position.x, y: position.y };
  return record;
}

function clamp01(value: number): number {
  return Math.min(1, Math.max(0, value));
}
