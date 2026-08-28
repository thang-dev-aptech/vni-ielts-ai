#!/usr/bin/env node
//
// F4.5 — every base image is pinned by digest.
//
// <b>A moving tag is not a pin, and the comment in backend/Dockerfile claimed
// it was.</b> `10.0-noble` is rebuilt whenever the upstream is rebuilt, so two
// deployments of the same commit can be two different systems — which surfaces
// as "it worked yesterday" and is unfalsifiable after the fact because the tag
// no longer points where it did.
//
// This is a check rather than a convention because a new `FROM` line is the
// easiest thing in the world to add without one, and nothing else in the
// repository would notice.
//
// Usage: node scripts/check-base-image-pins.mjs

import { readFileSync, existsSync } from 'node:fs';
import path from 'node:path';

/** Every Dockerfile that produces something deployable. */
export const DOCKERFILES = [
  'backend/Dockerfile',
  'backend/Dockerfile.worker',
  'apps/web/Dockerfile',
  'apps/admin/Dockerfile',
];

/**
 * Finds `FROM` lines whose image is not digest-pinned.
 *
 * @param {string} contents a Dockerfile
 * @returns {string[]} the offending FROM lines
 */
export function unpinnedFrom(contents) {
  return (
    contents
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => /^FROM\s+/i.test(line))
      // A stage alias — `FROM build AS runtime` — refers to an earlier stage in
      // this same file and has no digest to pin. Only lines naming a registry
      // image are candidates, and those always carry a `:` or a `/`.
      .filter((line) => {
        const image = line.split(/\s+/)[1] ?? '';
        return image.includes('/') || image.includes(':');
      })
      .filter((line) => !/@sha256:[0-9a-f]{64}/i.test(line))
  );
}

function main() {
  const root = path.resolve(import.meta.dirname, '..');
  const problems = [];
  let checked = 0;

  for (const relative of DOCKERFILES) {
    const file = path.join(root, relative);

    if (!existsSync(file)) {
      problems.push(`${relative} does not exist. If it moved, update DOCKERFILES.`);
      continue;
    }

    for (const line of unpinnedFrom(readFileSync(file, 'utf8'))) {
      problems.push(`${relative}: ${line}`);
    }
    checked++;
  }

  if (problems.length > 0) {
    console.error('Base images that are not pinned by digest:\n');
    for (const problem of problems) console.error(`  · ${problem}`);
    console.error(
      '\nUse `image:tag@sha256:…`. Resolve the digest with:\n' +
        "  docker pull <image:tag> && docker image inspect <image:tag> --format '{{index .RepoDigests 0}}'\n" +
        '\nA moving tag means two deployments of one commit can differ.',
    );
    process.exit(1);
  }

  console.log(`  ${checked} Dockerfile(s), every base image pinned by digest`);
}

if (process.argv[1] && import.meta.url.endsWith(path.basename(process.argv[1]))) {
  main();
}
