#!/usr/bin/env python3
"""
Merge a parsed Reading package and (when the book has audio) a parsed
Listening package into a development fixture, and wire the book's mp3s in.

ExamImporter never publishes and does not copy media. This script is the
seam that turns validated drafts plus the book's mp3s into
`fixtures/exams/<book>-test-N.json`, which DevelopmentExamSeeder loads on
the next API boot.

Usage:
  python3 scripts/assemble-cambridge-fixture.py \\
    --book cam17 --test 1 \\
    --reading _workspace/cambridge/packages/cam17-t1-reading.json \\
    --listening _workspace/cambridge/packages/cam17-t1-listening.json

`--listening` may be omitted for a book with no audio on disk (Cam 21):
the fixture then carries Reading only, and the description says so — a
Listening section whose parts have no recording is a paper nobody can sit.
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
CAM = ROOT / "Đề IELTS" / "Đề CAM"

SKILL_ORDER = ["reading", "listening"]

# Every book names its mp3s differently; the mapping is data, per book, and
# the layouts below were read off the disk rather than assumed. Cam 18 is
# "section" for test — the four files under `section1` are Test 1 parts 1–4,
# a layout confirmed by counting: four sections × four parts, no other axis.
# Cam 21 ships with no audio at all, so its entry is empty and the assembler
# refuses a Listening section for it rather than wiring silence.
BOOKS: dict[str, dict] = {
    "cam16": {
        "number": 16,
        "dir": CAM / "Cam 16",
        "audio": lambda t, p: [f"Test {t} Part {p}.mp3"],
    },
    "cam17": {
        "number": 17,
        "dir": CAM / "Cam 17",
        "audio": lambda t, p: [f"ELT_IELTS17_t{t}_audio{p}.mp3", f"IELTS17_t{t}_audio{p}.mp3"],
    },
    "cam18": {
        "number": 18,
        "dir": CAM / "Cam 18",
        "audio": lambda t, p: [
            f"18 section{t}-part{p}.mp3",
            f"18 section{t}- part{p}.mp3",
            f"18 section{t} part{p}.mp3",
        ],
    },
    "cam19": {
        "number": 19,
        "dir": CAM / "Cam 19",
        "audio": lambda t, p: [f"Test{t} Part{p}.mp3"],
    },
    "cam20": {"number": 20, "dir": CAM / "Cam 20", "audio": None},
    "cam21": {"number": 21, "dir": CAM / "Cam 21", "audio": None},
}


def load(path: pathlib.Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def section_for(package: dict, module: str) -> dict:
    matches = [s for s in package.get("sections") or [] if s.get("module") == module]
    if len(matches) != 1:
        raise SystemExit(
            f"{package.get('title', '<untitled>')}: expected exactly one {module} section, "
            f"found {len(matches)}"
        )
    section = matches[0]
    if not section.get("parts"):
        raise SystemExit(f"{module}: section has no parts")
    return section


def mark_count(section: dict) -> tuple[int, int]:
    questions = 0
    marks = 0
    for part in section.get("parts") or []:
        for q in part.get("questions") or []:
            questions += 1
            marks += int(q.get("marks") or 1)
    return questions, marks


def audio_source(book: dict, test: int, part: int) -> pathlib.Path:
    if book["audio"] is None:
        raise SystemExit(f"Cam {book['number']} has no audio on disk; assemble it with --reading only")
    candidates = [book["dir"] / name for name in book["audio"](test, part)]
    for path in candidates:
        if path.exists():
            return path
    raise SystemExit(
        f"Cam {book['number']} Test {test} Part {part} audio missing. Looked for: "
        + ", ".join(str(p.relative_to(ROOT)) for p in candidates)
    )


def wire_listening_audio(section: dict, slug: str, book: dict, test: int) -> None:
    parts = sorted(section.get("parts") or [], key=lambda p: p.get("order", 0))
    if len(parts) != 4:
        print(
            f"warning: listening has {len(parts)} part(s), expected 4",
            file=sys.stderr,
        )

    ASSETS.mkdir(parents=True, exist_ok=True)
    for index, part in enumerate(parts, start=1):
        source = audio_source(book, test, index)
        name = f"{slug}-listening-part{index}{source.suffix.lower()}"
        target = ASSETS / name
        shutil.copy2(source, target)
        part["kind"] = "recording"
        part["audio"] = f"assets/{name}"
        part.pop("body", None)
        print(f"  audio part {index}: {source.name} → assets/{name}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--book", required=True, choices=sorted(BOOKS))
    parser.add_argument("--test", required=True, type=int)
    parser.add_argument("--reading", required=True, type=pathlib.Path)
    parser.add_argument("--listening", type=pathlib.Path)
    parser.add_argument("--out", type=pathlib.Path)
    args = parser.parse_args()

    book = BOOKS[args.book]
    number = book["number"]
    slug = f"{args.book}-test-{args.test}"
    out = args.out or (FIXTURES / f"{slug}.json")

    reading = section_for(load(args.reading), "reading")
    reading["order"] = 1
    sections = [reading]
    envelope = load(TEMPLATE)

    if args.listening is not None:
        listening = section_for(load(args.listening), "listening")
        listening["order"] = 2
        wire_listening_audio(listening, slug, book, args.test)
        sections.append(listening)
        description = (
            f"Cambridge IELTS {number} Academic Test {args.test}. "
            "Reading và Listening chấm theo đáp án in trong sách; không có Writing và Speaking "
            "nên không thi thử full được. Chọn một trong hai kỹ năng để làm."
        )
    else:
        # Reading-only: either the book has no audio on disk (Cam 20/21) or this
        # test's Listening did not clear the key gate yet. Either way the learner
        # is told what is here rather than shown a Listening they cannot sit.
        why = (
            "bộ audio Listening của sách này chưa có"
            if book["audio"] is None
            else "phần Listening của đề này chưa sẵn sàng"
        )
        description = (
            f"Cambridge IELTS {number} Academic Test {args.test}. "
            f"Chỉ có Reading, chấm theo đáp án in trong sách; {why} "
            "nên không thi được Listening, Writing và Speaking."
        )

    fixture = {
        "formatVersion": envelope["formatVersion"],
        "title": f"Cambridge IELTS {number} — Test {args.test}",
        "variant": "academic",
        "description": description,
        "timingProfile": envelope["timingProfile"],
        "scoringProfile": envelope["scoringProfile"],
        "sections": sections,
    }

    out.parent.mkdir(parents=True, exist_ok=True)
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
