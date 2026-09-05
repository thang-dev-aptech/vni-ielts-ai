#!/usr/bin/env python3
"""
Prepare Cambridge IELTS sources for the ExamImporter.

Reads each book PDF under Đề IELTS/Đề CAM, writes per-test paper + key
text files under _workspace/cambridge/prepared/, and a manifest the
operator can feed to ExamImporter once AI credit is available.

Cam 17 and Cam 21 have a text layer (pdftotext). Cam 16/18/19/20 are
scans — answer-key pages are OCR'd with tesseract --psm 6; paper pages
are queued for OCR in a second pass (much larger).

This script never calls an AI provider and never publishes.
"""

from __future__ import annotations

import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
CAM_ROOT = ROOT / "Đề IELTS" / "Đề CAM"
OUT = ROOT / "_workspace" / "cambridge" / "prepared"

# Page ranges from each book's Contents (Listening starts the test; answer
# keys are the Listening+Reading key block). Speaking/Writing live inside
# the same test span and ride along when the full test is parsed later.
BOOKS = {
    "cam17": {
        "label": "Cambridge IELTS 17",
        "pdf": CAM_ROOT / "Cam 17" / "Cambridge Ielts 17.pdf",
        "mode": "text",
        "tests": {
            "1": {"paper": (10, 30), "key": (119, 120)},
            "2": {"paper": (31, 52), "key": (121, 122)},
            "3": {"paper": (53, 74), "key": (123, 124)},
            "4": {"paper": (75, 95), "key": (125, 126)},
        },
        "audio_glob": "Cam 17/*t{test}*audio*.mp3",
    },
    "cam21": {
        "label": "Cambridge IELTS 21",
        "pdf": CAM_ROOT / "Cam 21" / "Cambridge IELTS 21.pdf",
        "mode": "text",
        # PDF page index is +1 vs the printed Contents page number.
        # Confirmed 2026-09-03: PDF 118 = TEST 1 LISTENING, 119 = TEST 1 READING.
        "tests": {
            "1": {"paper": (10, 30), "key": (118, 119)},
            "2": {"paper": (31, 52), "key": (120, 121)},
            "3": {"paper": (53, 74), "key": (122, 123)},
            "4": {"paper": (75, 95), "key": (124, 125)},
        },
        "audio_glob": "Cam 21/Cambridge IELTS 21 Audio/*Test {test}*",
    },
    # Scanned books — key pages only in this pass (paper OCR is the long job).
    "cam16": {
        "label": "Cambridge IELTS 16",
        "pdf": CAM_ROOT / "Cam 16" / "Cam 16.pdf",
        "mode": "ocr-full",
        # Empirically: answer keys around pp. 123–130 (OCR smoke 2026-09-03).
        # Paper page range confirmed by rendering + tesseract, 2026-09-03: Test 1
        # LISTENING@11, Test 2@33, Test 4@77 (22-page cadence; Test 3 inferred).
        # Key ranges widened to three pages, 2026-09-04: a two-page window
        # opened on the previous test's Reading tail and closed before this
        # test's Reading heading, so key-reading.txt came out empty on Tests
        # 1–3 and key-listening.txt on Test 4. `split_listening_reading` now
        # takes the READING that follows the LISTENING, so the extra page is
        # harmless when it is the next test's.
        # Read off the rendered page, 2026-09-04: PDF 124 carries the printed
        # folio 123 and the badge "TEST 2 / LISTENING". Each test's key is two
        # printed pages (Listening, then Reading), so PDF = printed + 1 gives
        # Test 1 → 122–123, Test 2 → 124–125, Test 3 → 126–127, Test 4 →
        # 128–129. The earlier "empirical" 123–124 window was Test 1's Reading
        # followed by Test 2's Listening — a key for the wrong test that would
        # have passed every count-based check.
        "tests": {
            "1": {"paper": (11, 32), "key": (122, 123)},
            "2": {"paper": (33, 54), "key": (124, 125)},
            "3": {"paper": (55, 76), "key": (126, 127)},
            "4": {"paper": (77, 98), "key": (128, 129)},
        },
    },
    "cam18": {
        "label": "Cambridge IELTS 18",
        "pdf": CAM_ROOT / "Cam 18" / "0. Cambridge 18 (1).pdf",
        "mode": "ocr-full",
        # OCR discover 2026-09-03: TEST 1 L@121 … TEST 4 R@128
        # Paper page range confirmed by rendering + tesseract, 2026-09-03: Test 1
        # LISTENING@12, Test 2@34, Test 3@57, Test 4@80.
        "tests": {
            "1": {"paper": (12, 33), "key": (121, 122)},
            "2": {"paper": (34, 56), "key": (123, 124)},
            "3": {"paper": (57, 79), "key": (125, 126)},
            "4": {"paper": (80, 101), "key": (127, 128)},
        },
    },
    "cam19": {
        "label": "Cambridge IELTS 19",
        "pdf": CAM_ROOT / "Cam 19" / "Cambridge 19.pdf",
        "mode": "ocr-full",
        # OCR discover 2026-09-03: TEST 1 L@119 … TEST 4 R@126
        # Paper page range confirmed by rendering + tesseract, 2026-09-03: Test 1
        # LISTENING@9, Test 2@32, Test 3@54, Test 4@77.
        "tests": {
            "1": {"paper": (9, 31), "key": (119, 120)},
            "2": {"paper": (32, 53), "key": (121, 122)},
            "3": {"paper": (54, 76), "key": (123, 124)},
            "4": {"paper": (77, 99), "key": (125, 126)},
        },
    },
    "cam20": {
        "label": "Cambridge IELTS 20",
        "pdf": CAM_ROOT / "Cam 20" / "Cambridge IELTS 20 Academic.pdf",
        "mode": "ocr-keys",
        # Page range still being confirmed — placeholder near end of 184-page book.
        "tests": {
            "1": {"key": (160, 161)},
            "2": {"key": (162, 163)},
            "3": {"key": (164, 165)},
            "4": {"key": (166, 167)},
        },
    },
}


def run(cmd: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(cmd, check=True, capture_output=True, text=True)


def pdftotext(pdf: pathlib.Path, first: int, last: int) -> str:
    return run(
        ["pdftotext", "-f", str(first), "-l", str(last), "-layout", str(pdf), "-"]
    ).stdout


def ocr_pages(pdf: pathlib.Path, first: int, last: int, work: pathlib.Path) -> str:
    """
    OCR each page as two vertical halves.

    Cambridge answer-key pages are two-column. A full-page --psm 6 pass
    routinely drops the right column; OCR-ing each half separately and
    joining left-then-right keeps question numbers in order for the
    CambridgeAnswerKeyNormalizer.
    """
    work.mkdir(parents=True, exist_ok=True)
    prefix = work / "page"
    run(
        [
            "pdftoppm",
            "-f",
            str(first),
            "-l",
            str(last),
            "-png",
            "-r",
            "220",
            str(pdf),
            str(prefix),
        ]
    )
    chunks: list[str] = []
    images = sorted(work.glob("page*.png"))
    # Ignore half-crops from a previous run.
    images = [p for p in images if not p.stem.endswith(("-L", "-R"))]
    if not images:
        raise SystemExit(f"OCR page images missing for {pdf.name} pp {first}-{last}")

    for image_path in images:
        left_path = work / f"{image_path.stem}-L.png"
        right_path = work / f"{image_path.stem}-R.png"
        run(
            [
                "ffmpeg",
                "-y",
                "-i",
                str(image_path),
                # Cut at 40 %, read off the renders: the left column ends by
                # ~35 % ("14&15 IN EITHER ORDER" is the widest cell) and the
                # right column's question numbers start at ~44 % (Cam 18) to
                # ~48 % (Cam 16). A 50 % cut ran through Cam 16's numbers, a
                # 45 % cut through Cam 18's, and an overlapping pair of crops
                # put half-read right-column text on the left column's lines
                # ("7 evening 27&28"). 40 % sits in the gutter for every book
                # seen so far (2026-09-04).
                "-vf",
                "crop=iw*0.40:ih:0:0",
                str(left_path),
            ]
        )
        run(
            [
                "ffmpeg",
                "-y",
                "-i",
                str(image_path),
                "-vf",
                "crop=iw*0.60:ih:iw*0.40:0",
                str(right_path),
            ]
        )
        for half_path in (left_path, right_path):
            text = run(
                ["tesseract", str(half_path), "stdout", "-l", "eng", "--psm", "6"]
            ).stdout
            chunks.append(text)
    return "\n".join(chunks)


def ocr_pages_single_column(
    pdf: pathlib.Path, first: int, last: int, work: pathlib.Path
) -> str:
    """
    OCR each page as a whole image.

    Cambridge paper pages (Listening/Reading/Writing/Speaking prompts) are
    single-column, unlike the two-column answer-key pages `ocr_pages` is
    built for — cropping them in half the same way would cut lines and
    paragraphs down the middle.
    """
    work.mkdir(parents=True, exist_ok=True)
    prefix = work / "page"
    run(
        [
            "pdftoppm",
            "-f",
            str(first),
            "-l",
            str(last),
            "-png",
            "-r",
            "220",
            str(pdf),
            str(prefix),
        ]
    )
    images = sorted(work.glob("page*.png"))
    if not images:
        raise SystemExit(f"OCR page images missing for {pdf.name} pp {first}-{last}")
    chunks = [
        run(["tesseract", str(image_path), "stdout", "-l", "eng", "--psm", "6"]).stdout
        for image_path in images
    ]
    return "\n".join(chunks)


def split_listening_reading(key_text: str) -> tuple[str, str]:
    """
    Split a Cambridge key extract into Listening and Reading halves.

    Match only a heading that sits alone on a line — never the word
    "listening"/"reading" inside prose or the shared title
    "Listening and Reading answer keys".
    """
    listening_re = re.compile(r"(?m)^\s*LIS\s*TE\s*N\s*ING\s*$", re.I)
    reading_re = re.compile(r"(?m)^\s*RE\s*A\s*D\s*ING\s*$", re.I)

    # Cambridge prints Test N's Listening key and then its Reading key. A
    # two-page key extract therefore often *opens* with the tail of Test N-1's
    # Reading (its READING heading first on the page) before Test N's
    # LISTENING. The old rule — "READING before LISTENING means no split" —
    # handed that whole extract to key-listening.txt and left key-reading.txt
    # empty on three of four Cam 16 tests. The test's own Reading key is the
    # first READING heading *after* its LISTENING heading.
    listen_m = listening_re.search(key_text)
    if listen_m is None:
        read_m = reading_re.search(key_text)
        return ("", key_text[read_m.start() :]) if read_m else (key_text, "")

    read_after = reading_re.search(key_text, listen_m.end())
    if read_after is None:
        return key_text[listen_m.start() :], ""

    next_listening = listening_re.search(key_text, read_after.end())
    reading_end = next_listening.start() if next_listening else len(key_text)
    return (
        key_text[listen_m.start() : read_after.start()],
        key_text[read_after.start() : reading_end],
    )


MODULE_HEADINGS = ("LISTENING", "READING", "WRITING", "SPEAKING")


def collapsed_heading(line: str) -> str:
    return re.sub(r"\s+", "", line).upper()


def is_module_heading(line: str, name: str) -> bool:
    """
    Cambridge text-layer headings arrive either letter-spaced (`RE ADI NG`,
    Cam 17) or as a plain all-caps word (`READING`, Cam 21/18). Page footers
    are Title Case (`Reading`) and must not split the paper — an all-caps
    match is the signal. Some scanned badges glue a stray OCR fragment onto
    the same line (`ea LISTENING`, Cam 18) so this looks for the module name
    as an uppercase run rather than requiring the whole line to equal it —
    bounded only against *other* uppercase letters either side, so it still
    can't match inside a longer all-caps word.
    """
    stripped = line.strip()
    if not stripped:
        return False
    if " " in stripped and collapsed_heading(line) == name:
        return True
    return re.search(rf"(?<![A-Z]){name}(?![A-Z])", stripped) is not None


# The module heading is printed as white-on-black badge art, not plain text.
# tesseract reads it fine most of the time (it did for every LISTENING/WRITING/
# SPEAKING badge seen so far) but drops it on some scanned Reading badges —
# observed on 3 of 4 Cam 16 tests, 2026-09-03 — leaving `is_module_heading`
# with zero matches even though the section is fully present. Each pattern
# here is a second, content-based way to find that module's start; it only
# fires when the badge scan found nothing for that name, so it never
# overrides a real heading match.
FALLBACK_HEADING_MARKERS = {
    "reading": re.compile(r"(?m)^READING\s+PASSAGE\s+1\b"),
    "listening": re.compile(r"(?m)^PART\s+1\s+Questions\s+1\b"),
}


def split_paper_modules(paper: str) -> dict[str, str]:
    lines = paper.splitlines(keepends=True)
    starts: list[tuple[int, str]] = []
    seen: set[str] = set()
    for i, line in enumerate(lines):
        for name in MODULE_HEADINGS:
            # First occurrence only. `READING PASSAGE 2` and `READING PASSAGE 3`
            # also match the READING badge test, and each later match used to
            # replace the module's start — so every prepared Reading paper
            # began at its last passage (18 of 20 on 2026-09-04) and the parser
            # was paid to transcribe a third of a test.
            if name.lower() in seen:
                continue
            if is_module_heading(line, name):
                starts.append((i, name.lower()))
                seen.add(name.lower())
                break

    found_names = {name for _, name in starts}
    for name, pattern in FALLBACK_HEADING_MARKERS.items():
        if name in found_names:
            continue
        match = pattern.search(paper)
        if match:
            starts.append((paper.count("\n", 0, match.start()), name))
    starts.sort()

    modules: dict[str, str] = {}
    for index, (line_no, name) in enumerate(starts):
        end = starts[index + 1][0] if index + 1 < len(starts) else len(lines)
        text = "".join(lines[line_no:end]).strip()
        if text:
            modules[name] = text + "\n"
    return modules


def write_module_extracts(test_dir: pathlib.Path, paper: str) -> dict[str, str]:
    test_dir = test_dir.resolve()
    modules = split_paper_modules(paper)
    written: dict[str, str] = {}
    for name, text in modules.items():
        path = test_dir / f"paper-{name}.txt"
        path.write_text(text, encoding="utf-8")
        written[name] = str(path.relative_to(ROOT))
    return written


def prepare_book(book_id: str, book: dict, keys_only: bool = False) -> list[dict]:
    pdf: pathlib.Path = book["pdf"]
    if not pdf.exists():
        print(f"skip {book_id}: PDF missing at {pdf}", file=sys.stderr)
        return []

    out_dir = OUT / book_id
    out_dir.mkdir(parents=True, exist_ok=True)
    records: list[dict] = []

    for test, pages in book["tests"].items():
        test_dir = out_dir / f"test-{test}"
        test_dir.mkdir(parents=True, exist_ok=True)
        record: dict = {
            "bookId": book_id,
            "label": book["label"],
            "test": test,
            "mode": book["mode"],
            "pdf": str(pdf.relative_to(ROOT)),
        }

        if "paper" in pages and not keys_only:
            if book["mode"] == "text":
                paper = pdftotext(pdf, pages["paper"][0], pages["paper"][1])
            else:
                paper = ocr_pages_single_column(
                    pdf, pages["paper"][0], pages["paper"][1], test_dir / "ocr-paper"
                )
            paper_path = test_dir / "paper.txt"
            paper_path.write_text(paper, encoding="utf-8")
            record["paper"] = str(paper_path.relative_to(ROOT))
            record["paperChars"] = len(paper)
            record["paperPages"] = list(pages["paper"])
            record["modules"] = write_module_extracts(test_dir, paper)

        if "key" in pages:
            if book["mode"] == "text":
                key_raw = pdftotext(pdf, pages["key"][0], pages["key"][1])
            else:
                key_raw = ocr_pages(
                    pdf, pages["key"][0], pages["key"][1], test_dir / "ocr-key"
                )
            (test_dir / "key-raw.txt").write_text(key_raw, encoding="utf-8")
            listening, reading = split_listening_reading(key_raw)
            (test_dir / "key-listening.txt").write_text(listening, encoding="utf-8")
            (test_dir / "key-reading.txt").write_text(reading, encoding="utf-8")
            record["keyRaw"] = str((test_dir / "key-raw.txt").relative_to(ROOT))
            record["keyListening"] = str(
                (test_dir / "key-listening.txt").relative_to(ROOT)
            )
            record["keyReading"] = str((test_dir / "key-reading.txt").relative_to(ROOT))
            record["keyPages"] = list(pages["key"])
            record["keyChars"] = len(key_raw)

        records.append(record)
        print(
            f"{book_id} test {test}: "
            + (
                f"paper={record.get('paperChars', 0)}c "
                if "paper" in record
                else "paper=pending-ocr "
            )
            + f"key={record.get('keyChars', 0)}c",
            flush=True,
        )

    return records


def resplit_book(book_id: str) -> None:
    """
    Re-derive the per-module paper files and the two key files from the
    `paper.txt` / `key-raw.txt` already on disk — no pdftotext, no OCR.

    The extraction is the slow, expensive step and it was right; the split
    was wrong. Redoing only the split is minutes instead of hours.
    """
    for test_dir in sorted((OUT / book_id).glob("test-*")):
        paper_path = test_dir / "paper.txt"
        key_path = test_dir / "key-raw.txt"
        summary = []
        if paper_path.exists():
            modules = write_module_extracts(test_dir, paper_path.read_text(encoding="utf-8"))
            summary.append("modules=" + ",".join(sorted(modules)))
        if key_path.exists():
            listening, reading = split_listening_reading(key_path.read_text(encoding="utf-8"))
            (test_dir / "key-listening.txt").write_text(listening, encoding="utf-8")
            (test_dir / "key-reading.txt").write_text(reading, encoding="utf-8")
            summary.append(f"key L={len(listening)}c R={len(reading)}c")
        print(f"{book_id} {test_dir.name}: " + " ".join(summary), flush=True)


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    args = sys.argv[1:]
    if args and args[0] == "--resplit":
        for book_id in args[1:] or list(BOOKS):
            if (OUT / book_id).is_dir():
                resplit_book(book_id)
        return 0

    # --keys-only: re-extract just the answer-key pages (a page range fix),
    # leaving the hours-long paper OCR alone. Rewrites the manifest with the
    # key records only, so do not use it for a first pass.
    keys_only = bool(args) and args[0] == "--keys-only"
    wanted = (args[1:] if keys_only else args) or list(BOOKS)
    all_records: list[dict] = []
    for book_id in wanted:
        if book_id not in BOOKS:
            print(f"unknown book {book_id}", file=sys.stderr)
            return 2
        all_records.extend(prepare_book(book_id, BOOKS[book_id], keys_only))

    manifest = {
        "generatedBy": "scripts/prepare-cambridge-sources.py",
        "out": str(OUT.relative_to(ROOT)),
        "records": all_records,
        "next": [
            "Wire paper.txt through ExamImporter (--source) once AI credit returns.",
            "Pass key-listening.txt / key-reading.txt through CambridgeAnswerKeyNormalizer "
            "then AnswerKeyInjection (--package / --key).",
            "Copy audio into exam/<slug>/assets and run scripts/import-exam-package.py.",
            "Restart API (Development) so DevelopmentExamSeeder publishes fixtures/exams/*.json.",
        ],
    }
    (OUT / "manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    print(f"wrote {len(all_records)} records → {OUT / 'manifest.json'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
