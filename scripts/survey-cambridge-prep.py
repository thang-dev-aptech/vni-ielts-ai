#!/usr/bin/env python3
"""
Preflight for `_workspace/cambridge/prepared/<book>/test-N/`: is each paper and
key text whole enough to be worth a paid parse?

A parse costs money and minutes; a key that turns out to be the other module's,
or a paper that starts at passage 3, fails only after both were spent. This
reads what is on disk and says, per test and module, what it found — so the
batch can skip the ones that are not ready and somebody can fix the prep.

Checks are deliberately shallow and printed rather than judged: the numbers
are for a person to read across a table.

usage: python3 scripts/survey-cambridge-prep.py [book ...]
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
PREPARED = ROOT / "_workspace" / "cambridge" / "prepared"

# What a whole paper must mention. Reading: the first and the last passage's
# question block. Listening: Part 1 and Part 4.
PAPER_MARKS = {
    "reading": [r"Questions?\s+1\b", r"Questions?\s+2[7-9]|Questions?\s+3[0-9]"],
    "listening": [r"PART\s*1|Part\s*1", r"PART\s*4|Part\s*4"],
}

# A key line: "12 game", "3. NOT GIVEN", "25&26 IN EITHER ORDER".
KEY_LINE = re.compile(r"^\s*(\d{1,2})\s*(?:[.&-]|\s)")


def key_numbers(text: str) -> list[int]:
    numbers = []
    for line in text.splitlines():
        m = KEY_LINE.match(line)
        if m:
            n = int(m.group(1))
            if 1 <= n <= 40:
                numbers.append(n)
    return numbers


def key_module_heading(text: str) -> str:
    head = text[:400].upper()
    if "READING" in head and "LISTENING" not in head:
        return "READING?"
    if "LISTENING" in head and "READING" not in head:
        return "LISTENING?"
    if "PASSAGE" in head:
        return "READING?"
    if "PART 1" in head or "PART1" in head:
        return "LISTENING?"
    return "?"


def survey(book: str, test: int) -> str:
    folder = PREPARED / book / f"test-{test}"
    cells = [f"{book} t{test}"]
    for module in ("reading", "listening"):
        paper = folder / f"paper-{module}.txt"
        key = folder / f"key-{module}.txt"
        if not paper.exists():
            cells.append(f"{module}: paper MISSING")
        else:
            text = paper.read_text(encoding="utf-8", errors="replace")
            marks = [bool(re.search(p, text)) for p in PAPER_MARKS[module]]
            cells.append(
                f"{module}: paper {len(text):>6}ch first={'y' if marks[0] else 'N'} last={'y' if marks[1] else 'N'}"
            )
        if not key.exists():
            cells.append("key MISSING")
        else:
            ktext = key.read_text(encoding="utf-8", errors="replace")
            nums = key_numbers(ktext)
            distinct = sorted(set(nums))
            span = f"{distinct[0]}-{distinct[-1]}" if distinct else "-"
            cells.append(
                f"key {len(ktext):>5}ch nums={len(distinct):>2} ({span}) head={key_module_heading(ktext)}"
            )
    return " | ".join(cells)


def main() -> int:
    books = sys.argv[1:] or sorted(p.name for p in PREPARED.iterdir() if p.is_dir())
    for book in books:
        for test in (1, 2, 3, 4):
            if (PREPARED / book / f"test-{test}").is_dir():
                print(survey(book, test))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
