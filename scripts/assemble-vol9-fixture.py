#!/usr/bin/env python3
"""
Merge a parsed VOL 9 Reading package and Listening package into a development
fixture, and wire the test's single Listening recording into it.

VOL 9 ships one `TEST N.mp4` per test covering all four parts, so every
Listening part points at the same file — exactly as `vol9-test-1.json` does.

Usage:
  python3 scripts/assemble-vol9-fixture.py --test 2 \\
    --reading _workspace/vol9/packages/vol9-t2-reading.json \\
    --listening _workspace/vol9/packages/vol9-t2-listening.json
"""

from __future__ import annotations

import argparse
import json
import pathlib
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TEMPLATE = ROOT / "fixtures" / "exams" / "vol9-test-1.json"
FIXTURES = ROOT / "fixtures" / "exams"
ASSETS = FIXTURES / "assets"
VOL9 = (
    ROOT / "Đề IELTS" / "Đề CAM"
    / "Đề thi thật (Chỉ L và R) VOL 9 - REAL IELTS-20260819T082203Z-1-001"
    / "VOL 9 - REAL IELTS"
)


def load(path: pathlib.Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def section_for(package: dict, module: str) -> dict:
    matches = [s for s in package.get("sections") or [] if s.get("module") == module]
    if len(matches) != 1:
        raise SystemExit(f"expected exactly one {module} section, found {len(matches)}")
    if not matches[0].get("parts"):
        raise SystemExit(f"{module}: section has no parts")
    return matches[0]


def mark_count(section: dict) -> tuple[int, int]:
    questions = marks = 0
    for part in section.get("parts") or []:
        for q in part.get("questions") or []:
            questions += 1
            marks += int(q.get("marks") or 1)
    return questions, marks


def wire_audio(section: dict, slug: str, test: int) -> None:
    source = VOL9 / "LISTENING" / "AUDIO" / f"TEST {test}.mp4"
    if not source.exists():
        raise SystemExit(f"VOL 9 Test {test} audio missing: {source.relative_to(ROOT)}")
    ASSETS.mkdir(parents=True, exist_ok=True)
    name = f"{slug}-listening-full{source.suffix.lower()}"
    shutil.copy2(source, ASSETS / name)
    parts = sorted(section.get("parts") or [], key=lambda p: p.get("order", 0))
    if len(parts) != 4:
        print(f"warning: listening has {len(parts)} part(s), expected 4", file=sys.stderr)
    for part in parts:
        part["kind"] = "recording"
        part["audio"] = f"assets/{name}"
        part.pop("body", None)
    print(f"  audio: {source.name} → assets/{name} (all {len(parts)} parts)")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--test", required=True, type=int)
    parser.add_argument("--reading", required=True, type=pathlib.Path)
    parser.add_argument("--listening", type=pathlib.Path)
    parser.add_argument("--out", type=pathlib.Path)
    args = parser.parse_args()

    slug = f"vol9-test-{args.test}"
    out = args.out or (FIXTURES / f"{slug}.json")
    envelope = load(TEMPLATE)

    reading = section_for(load(args.reading), "reading")
    reading["order"] = 1
    sections = [reading]
    if args.listening is not None:
        listening = section_for(load(args.listening), "listening")
        listening["order"] = 2
        wire_audio(listening, slug, args.test)
        sections.append(listening)
        title = f"VOL 9 — Test {args.test} (Reading + Listening)"
        description = envelope.get("description")
    else:
        # Reading-only until this test's Listening clears the key gate; the
        # learner is told so rather than shown a Listening they cannot sit.
        title = f"VOL 9 — Test {args.test} (Reading)"
        description = (
            f"VOL 9 Test {args.test}. Hiện chỉ có Reading, chấm theo đáp án của bộ đề; "
            "phần Listening của đề này chưa sẵn sàng."
        )

    fixture = {
        "formatVersion": envelope["formatVersion"],
        "title": title,
        "variant": "academic",
        "description": description,
        "timingProfile": envelope["timingProfile"],
        "scoringProfile": envelope["scoringProfile"],
        "sections": sections,
    }
    out.write_text(json.dumps(fixture, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print(f"wrote {out.relative_to(ROOT)}")
    short = False
    for section in sections:
        q, m = mark_count(section)
        print(f"  {section['module']}: {q} question(s), {m} mark(s)")
        short = short or m != 40
    if short:
        print("  warning: expected 40 marks per skill", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
