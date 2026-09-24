import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ImportDraft, ImportDraftGroup } from '../lib/adminApi.js';

/**
 * `GroupPositionEditor` — the point-marker hotspot editor.
 *
 * <b>The image is fetched, not linked.</b> `fetchImportDraftAssetObjectUrl`
 * is mocked to resolve immediately to a fixed `blob:` string, since the real
 * function's `URL.createObjectURL`/network round trip is `adminApi.ts`'s
 * concern, already covered by `fetchMediaObjectUrl`'s own precedent — this
 * file tests the editor's placement math and save wiring, not fetch plumbing.
 *
 * <b>`getBoundingClientRect` is stubbed per test.</b> jsdom does no layout, so
 * the image element reports a zero-size rect by default; every placement
 * test gives it a fixed, known box before clicking, which is what makes the
 * fraction assertions exact rather than approximate.
 */

vi.mock('../lib/adminApi.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/adminApi.js')>();
  return {
    ...actual,
    fetchImportDraftAssetObjectUrl: vi.fn(),
    setGroupPositions: vi.fn(),
  };
});

const { fetchImportDraftAssetObjectUrl, setGroupPositions } = await import('../lib/adminApi.js');
const { GroupPositionEditor } = await import('../components/GroupPositionEditor.js');

function group(overrides: Partial<ImportDraftGroup> = {}): ImportDraftGroup {
  return {
    id: 'g-1',
    title: 'Sơ đồ bảo tàng',
    instruction: null,
    imageKey: 'assets/map.jpg',
    text: null,
    eachLetterOnce: false,
    options: [
      { key: 'A', text: 'Kitchen' },
      { key: 'B', text: 'Garden' },
    ],
    positions: null,
    ...overrides,
  };
}

function draftWith(g: ImportDraftGroup): ImportDraft {
  return {
    draftId: 'draft-1',
    definitionId: 'def-1',
    versionNumber: 1,
    route: 'structuredpackage',
    approvalState: 'reviewrequired',
    revision: 2,
    reviewedBy: null,
    presentSkills: ['reading'],
    findings: [],
    warnings: [],
    checklistConfirmed: [],
    checklistComplete: false,
    groups: [g],
  };
}

/** A fixed 200×100 box at the origin — click math below assumes exactly this. */
function stubImageBox(img: HTMLElement) {
  img.getBoundingClientRect = vi.fn(
    () =>
      ({
        left: 0,
        top: 0,
        right: 200,
        bottom: 100,
        width: 200,
        height: 100,
        x: 0,
        y: 0,
        toJSON: () => ({}),
      }) as DOMRect,
  );
}

beforeEach(() => {
  vi.mocked(fetchImportDraftAssetObjectUrl).mockReset();
  vi.mocked(setGroupPositions).mockReset();
  vi.mocked(fetchImportDraftAssetObjectUrl).mockResolvedValue('blob:mock-image');
  // jsdom does not implement it; the editor calls it on unmount/re-fetch.
  URL.revokeObjectURL = vi.fn();
});

describe('GroupPositionEditor', () => {
  it('places a pin at the clicked fraction once a key is armed', async () => {
    const onSaved = vi.fn();
    render(
      <GroupPositionEditor
        accessToken="token-1"
        draftId="draft-1"
        group={group()}
        onSaved={onSaved}
      />,
    );

    const img = await screen.findByAltText('Sơ đồ bảo tàng');
    stubImageBox(img);

    // Nothing placed yet.
    expect(screen.getByText('0/2 đã đặt')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /^A —/ }));
    fireEvent.click(img, { clientX: 50, clientY: 25 });

    expect(screen.getByText('1/2 đã đặt')).toBeInTheDocument();
    const pin = screen.getByRole('button', { name: /Vị trí A/ });
    expect(pin.style.left).toBe('25%');
    expect(pin.style.top).toBe('25%');
  });

  it('clicking a placed pin re-arms it for a move, and a separate control removes it', async () => {
    render(
      <GroupPositionEditor
        accessToken="token-1"
        draftId="draft-1"
        group={group({ positions: [{ key: 'A', x: 0.25, y: 0.25 }] })}
        onSaved={vi.fn()}
      />,
    );

    const img = await screen.findByAltText('Sơ đồ bảo tàng');
    stubImageBox(img);

    // Stored on load, with no click at all.
    const pinBefore = screen.getByRole('button', { name: /Vị trí A/ });
    expect(pinBefore.style.left).toBe('25%');
    expect(pinBefore.style.top).toBe('25%');

    // Move: click the pin (re-arms 'A'), then click a new spot.
    fireEvent.click(pinBefore);
    fireEvent.click(img, { clientX: 150, clientY: 75 });
    const pinAfter = screen.getByRole('button', { name: /Vị trí A/ });
    expect(pinAfter.style.left).toBe('75%');
    expect(pinAfter.style.top).toBe('75%');

    // Remove: the dedicated control, not the pin itself.
    fireEvent.click(screen.getByRole('button', { name: 'Xoá' }));
    expect(screen.getByText('0/2 đã đặt')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Vị trí A/ })).not.toBeInTheDocument();
  });

  it('existing stored positions render at their correct spots on initial load', async () => {
    render(
      <GroupPositionEditor
        accessToken="token-1"
        draftId="draft-1"
        group={group({
          positions: [
            { key: 'A', x: 0.1, y: 0.2 },
            { key: 'B', x: 0.9, y: 0.8 },
          ],
        })}
        onSaved={vi.fn()}
      />,
    );

    await screen.findByAltText('Sơ đồ bảo tàng');

    expect(screen.getByText('2/2 đã đặt')).toBeInTheDocument();
    const pinA = screen.getByRole('button', { name: /Vị trí A/ });
    const pinB = screen.getByRole('button', { name: /Vị trí B/ });
    expect(pinA.style.left).toBe('10%');
    expect(pinA.style.top).toBe('20%');
    expect(pinB.style.left).toBe('90%');
    expect(pinB.style.top).toBe('80%');
  });

  it('keeps an unsaved pin when the parent refreshes the same group', async () => {
    const onSaved = vi.fn();
    const { rerender } = render(
      <GroupPositionEditor
        accessToken="token-1"
        draftId="draft-1"
        group={group({ positions: [] })}
        onSaved={onSaved}
      />,
    );

    const img = await screen.findByAltText('Sơ đồ bảo tàng');
    stubImageBox(img);

    fireEvent.click(screen.getByRole('button', { name: /^A —/ }));
    fireEvent.click(img, { clientX: 50, clientY: 25 });

    // Checklist and warning mutations replace the parent draft with a fresh
    // response, including a new positions array for this unchanged group.
    rerender(
      <GroupPositionEditor
        accessToken="token-1"
        draftId="draft-1"
        group={group({ positions: [] })}
        onSaved={onSaved}
      />,
    );

    expect(screen.getByText('1/2 đã đặt')).toBeInTheDocument();
    const pin = screen.getByRole('button', { name: /Vị trí A/ });
    expect(pin.style.left).toBe('25%');
    expect(pin.style.top).toBe('25%');
    expect(setGroupPositions).not.toHaveBeenCalled();
  });

  it('save sends the full current position set and hands the response to onSaved', async () => {
    const onSaved = vi.fn();
    const updated = draftWith(group({ positions: [{ key: 'A', x: 0.25, y: 0.25 }] }));
    vi.mocked(setGroupPositions).mockResolvedValue(updated);

    render(
      <GroupPositionEditor
        accessToken="token-1"
        draftId="draft-1"
        group={group()}
        onSaved={onSaved}
      />,
    );

    const img = await screen.findByAltText('Sơ đồ bảo tàng');
    stubImageBox(img);

    fireEvent.click(screen.getByRole('button', { name: /^A —/ }));
    fireEvent.click(img, { clientX: 50, clientY: 25 });

    fireEvent.click(screen.getByRole('button', { name: 'Lưu vị trí' }));

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(updated));

    expect(setGroupPositions).toHaveBeenCalledWith('token-1', 'draft-1', 'g-1', [
      { key: 'A', x: 0.25, y: 0.25 },
    ]);
  });

  it('a group with no image never renders', () => {
    const { container } = render(
      <GroupPositionEditor
        accessToken="token-1"
        draftId="draft-1"
        group={group({ imageKey: null })}
        onSaved={vi.fn()}
      />,
    );

    expect(container).toBeEmptyDOMElement();
    expect(fetchImportDraftAssetObjectUrl).not.toHaveBeenCalled();
  });
});
