# Backend Engineer D3 Report

Run: `demo-rlw-2026-09-07`

Task: D3 Listening assets for the demo paper.

## Commands

1. `dotnet run --project backend/tools/Vni.Ielts.AssetSync -- push --dry-run`
   - Exit code: 0
   - Result: `push done: 0 uploaded, 7 already present.`
   - Interpretation: AssetSync saw seven local fixture assets and every one already matched a remote object by size under `vni-ielts-ai-dev/examassets/`.

2. `dotnet run --project backend/tools/Vni.Ielts.AssetSync -- pull --dry-run`
   - Exit code: 0
   - Result: `pull done: 51 downloaded, 7 already present.`
   - Interpretation: 51 remote assets are present in R2 but not local, and the same seven local assets are already present remotely.

No real `push` was run because the dry-run push reported zero uploads. This avoids rewriting remote objects unnecessarily.

## Local Files Classified By Push Dry-Run

Would upload:

- None.

Already present remotely:

- `examassets/exam-1-listening-part1.mp3`
- `examassets/exam-1-listening-part2.mp3`
- `examassets/exam-1-listening-part3.mp3`
- `examassets/exam-1-listening-part4.mp3`
- `examassets/exam-1-listening-part2-community-centre-map.jpg`
- `examassets/exam-1-writing-task1-chart.jpg`
- `examassets/vol9-test-1-listening-full.mp4`

AssetSync confirms remote presence by matching object key and byte size. It does not compare a remote checksum during dry-run.

## Remote Keys Confirmed By Pull Dry-Run

The following Listening papers have remote audio listed by `pull --dry-run`:

- `cam16-test-1`: `examassets/cam16-test-1-listening-part1.mp3` through `part4.mp3`
- `cam16-test-2`: `examassets/cam16-test-2-listening-part1.mp3` through `part4.mp3`
- `cam16-test-4`: `examassets/cam16-test-4-listening-part1.mp3` through `part4.mp3`
- `cam17-test-1`: `examassets/cam17-test-1-listening-part1.mp3` through `part4.mp3`
- `cam17-test-2`: `examassets/cam17-test-2-listening-part1.mp3` through `part4.mp3`
- `cam17-test-3`: `examassets/cam17-test-3-listening-part1.mp3` through `part4.mp3`
- `cam17-test-4`: `examassets/cam17-test-4-listening-part1.mp3` through `part4.mp3`
- `cam18-test-1`: `examassets/cam18-test-1-listening-part1.mp3` through `part4.mp3`
- `cam18-test-3`: `examassets/cam18-test-3-listening-part1.mp3` through `part4.mp3`
- `cam18-test-4`: `examassets/cam18-test-4-listening-part1.mp3` through `part4.mp3`
- `cam19-test-1`: `examassets/cam19-test-1-listening-part1.mp3` through `part4.mp3`
- `cam19-test-2`: `examassets/cam19-test-2-listening-part1.mp3` through `part4.mp3`
- `vol9-test-2`: `examassets/vol9-test-2-listening-full.mp4`
- `vol9-test-4`: `examassets/vol9-test-4-listening-full.mp4`
- `vol9-test-6`: `examassets/vol9-test-6-listening-full.mp4`

## Recommended Demo Papers

Safest Listening papers with confirmed remote audio:

- `exam-1`
- `vol9-test-1`
- `vol9-test-2`
- `vol9-test-4`
- `vol9-test-6`
- `cam16-test-1`, `cam16-test-2`, `cam16-test-4`
- `cam17-test-1`, `cam17-test-2`, `cam17-test-3`, `cam17-test-4`
- `cam18-test-1`, `cam18-test-3`, `cam18-test-4`
- `cam19-test-1`, `cam19-test-2`

Avoid using `cam18-test-2`, `cam19-test-3`, `cam21-test-*`, `vol9-test-7`, or `vol9-test-8` for the demo unless their audio is separately verified, because these fixture papers were not confirmed in the AssetSync dry-run output.

## Remaining Smoke

This task verified object-storage inventory only. Final `200` or `206` playback proof still needs the API running and should be covered by D6/QA by requesting the first Listening audio URL for the chosen paper.
