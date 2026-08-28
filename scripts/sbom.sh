#!/usr/bin/env bash
#
# F5.2 — writes an SBOM per image into `_artifacts/sbom/`.
#
# ── Why this exists separately from the release workflow ────────────────────
#
# `release-images.yml` already asks buildx for `sbom: true`, which attaches an
# SBOM to the pushed image as an attestation. That is the right place for a
# RELEASE artifact and the wrong place for a CI one: it only runs on a tag, and
# what it produces lives in a registry rather than in the build's own outputs.
#
# `verify.yml` uploads `_artifacts/sbom/` on every run — and until this script
# existed, nothing wrote there, so `if-no-files-found: ignore` made that step
# silently green over an empty directory. A retention step that retains
# nothing is worse than none: it looks like evidence.
#
# ── Why syft rather than `docker sbom` ──────────────────────────────────────
#
# `docker sbom` is a Docker Desktop convenience that is absent on a CI runner.
# syft is the tool underneath it, runs as a container, and needs nothing but
# the socket. Verified on this host: SPDX-2.3, 138 packages for the API image,
# including the real .NET dependencies (AWSSDK.Core, BouncyCastle, DnsClient).
#
# Usage:
#   scripts/sbom.sh                 # every image, tagged with the commit SHA
#   scripts/sbom.sh vni-ielts-api   # one image
#
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/_artifacts/sbom"
SYFT="${VNI_SYFT_IMAGE:-anchore/syft:latest}"

SHA="$(git -C "$ROOT" rev-parse HEAD 2>/dev/null || echo unknown)"

if ! docker info >/dev/null 2>&1; then
  if [ "${VNI_REQUIRE_DOCKER:-0}" = "1" ]; then
    echo "sbom: Docker is required (VNI_REQUIRE_DOCKER=1) but not reachable." >&2
    exit 1
  fi
  echo "sbom: Docker is not available — skipping."
  exit 0
fi

mkdir -p "$OUT"

IMAGES=("$@")
if [ "${#IMAGES[@]}" -eq 0 ]; then
  IMAGES=(vni-ielts-api vni-ielts-worker vni-ielts-web vni-ielts-admin)
fi

failures=0
written=0

for name in "${IMAGES[@]}"; do
  ref="${name}:${SHA}"

  if ! docker image inspect "$ref" >/dev/null 2>&1; then
    # <b>A missing image is a failure, not a skip.</b> This runs after the
    # image stage; if the image is absent then either that stage did not run
    # or it did not produce what it claimed, and writing no SBOM while exiting
    # 0 would hide exactly that.
    echo "sbom: FAIL — no image $ref. Run scripts/verify-images.sh first." >&2
    failures=$((failures + 1))
    continue
  fi

  echo "sbom: $ref ..."

  # MSYS_NO_PATHCONV: the container-side paths in this command must not be
  # rewritten by Git Bash. → the same trap documented in pbm-setup.sh.
  if MSYS_NO_PATHCONV=1 docker run --rm \
    -v /var/run/docker.sock:/var/run/docker.sock \
    "$SYFT" "$ref" -o spdx-json > "$OUT/${name}.spdx.json" 2>/dev/null; then

    # An SBOM that parses but describes nothing is the failure mode worth
    # catching: syft exits 0 on an image it could not read.
    #
    # <b>Read via a RELATIVE path, from $ROOT.</b> `$OUT` is a POSIX path
    # (`/c/Users/...`) and a native Windows `node` cannot open it — passing it
    # here reported "0 packages" for four SBOMs that were each megabytes of
    # correct SPDX. The guard was right and the reader was wrong, which is the
    # more dangerous way round. → the same trap as pbm-run.sh and otel-smoke.sh
    packages="$(cd "$ROOT" && node -e "
      try {
        const s = JSON.parse(require('fs').readFileSync('_artifacts/sbom/${name}.spdx.json','utf8'));
        console.log((s.packages || []).length);
      } catch { console.log(0); }
    " 2>/dev/null || echo 0)"

    if [ "${packages:-0}" -lt 1 ]; then
      echo "sbom: FAIL — $ref produced an SBOM describing 0 packages." >&2
      failures=$((failures + 1))
    else
      echo "sbom:   ok — $packages package(s) → _artifacts/sbom/${name}.spdx.json"
      written=$((written + 1))
    fi
  else
    echo "sbom: FAIL — syft could not read $ref." >&2
    failures=$((failures + 1))
  fi
done

echo
if [ "$failures" -ne 0 ]; then
  echo "sbom: $failures image(s) produced no usable SBOM." >&2
  exit 1
fi

echo "sbom: OK — $written SBOM(s) in _artifacts/sbom/ for commit $SHA."
