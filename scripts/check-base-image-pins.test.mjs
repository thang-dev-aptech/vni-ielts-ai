// F4.5 — run: node --test scripts/check-base-image-pins.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { unpinnedFrom, DOCKERFILES } from './check-base-image-pins.mjs';

const DIGEST = 'sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c';

test('a digest-pinned FROM passes', () => {
  assert.deepEqual(unpinnedFrom(`FROM node:24-slim@${DIGEST} AS build\n`), []);
});

test('a moving tag is caught — this is the whole point', () => {
  // `10.0-noble` is rebuilt upstream; it names an intent, not an artifact.
  const found = unpinnedFrom('FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build\n');
  assert.equal(found.length, 1);
});

test('`latest` is caught', () => {
  assert.equal(unpinnedFrom('FROM nginx:latest\n').length, 1);
});

test('a bare image with no tag at all is caught', () => {
  assert.equal(unpinnedFrom('FROM nginxinc/nginx-unprivileged\n').length, 1);
});

test('a stage alias is not treated as a base image', () => {
  // `FROM build AS runtime` refers to an earlier stage in the same file and
  // has no digest to pin. Flagging it would make the rule unsatisfiable.
  assert.deepEqual(unpinnedFrom('FROM build AS runtime\n'), []);
});

test('a truncated or malformed digest is caught', () => {
  // A 64-hex digest is the only acceptable form; anything shorter is a
  // copy-paste accident that would fail at pull time, or worse, resolve.
  assert.equal(unpinnedFrom('FROM node:24-slim@sha256:deadbeef AS build\n').length, 1);
});

test('case-insensitive on the FROM keyword', () => {
  assert.equal(unpinnedFrom('from nginx:1.27\n').length, 1);
});

test('several stages are each checked', () => {
  const dockerfile = [
    `FROM node:24-slim@${DIGEST} AS build`,
    'FROM nginx:1.27-alpine AS runtime',
    '',
  ].join('\n');

  assert.equal(unpinnedFrom(dockerfile).length, 1);
});

test('every Dockerfile in this repository is pinned', () => {
  // The check that actually protects the repository, rather than the parser.
  const root = path.join(import.meta.dirname, '..');

  for (const relative of DOCKERFILES) {
    const contents = readFileSync(path.join(root, relative), 'utf8');
    assert.deepEqual(unpinnedFrom(contents), [], `${relative} has an unpinned base image`);
  }
});
