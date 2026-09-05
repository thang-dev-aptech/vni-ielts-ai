import assert from 'node:assert/strict';
import { test } from 'node:test';
import { quoteWindowsShellArgument } from './spawn-portable.mjs';

test('Windows shell quoting doubles slash runs before quotes and at the end', () => {
  const quoted = quoteWindowsShellArgument('C:\\media\\a\\"quoted"\\');

  assert.equal(quoted, '"C:\\media\\a\\\\\\"quoted\\"\\\\"');
});

test('Windows shell quoting encloses whitespace without changing it', () => {
  assert.equal(quoteWindowsShellArgument('two words'), '"two words"');
});
