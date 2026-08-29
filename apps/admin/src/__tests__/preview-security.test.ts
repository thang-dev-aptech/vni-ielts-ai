import { describe, expect, it } from 'vitest';
import { objectUrlFor, rememberObjectUrl } from '../lib/previewStore.js';

describe('media preview URL boundary', () => {
  it.each([
    'javascript:alert(1)',
    'data:text/html,<script>alert(1)</script>',
    'https://evil.test/a',
  ])('refuses a renderer-controlled %s URL', (url) => {
    expect(() => rememberObjectUrl(crypto.randomUUID(), url)).toThrow(/blob URLs/);
  });

  it('returns a browser-minted blob URL', () => {
    const mediaId = crypto.randomUUID();
    const url = 'blob:http://localhost/safe-preview';

    rememberObjectUrl(mediaId, url);

    expect(objectUrlFor(mediaId)).toBe(url);
  });
});
