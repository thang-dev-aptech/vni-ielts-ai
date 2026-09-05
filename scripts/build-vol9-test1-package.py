#!/usr/bin/env python3
"""
Build exam/Vol9Test1 from owner-supplied VOL 9 REAL IELTS Test 1 DOCX sources.

Usage:
  python scripts/build-vol9-test1-package.py
  python scripts/import-exam-package.py exam/Vol9Test1 vol9-test-1 reading,listening
"""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import sys
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = ROOT / "exam" / "Vol9Test1"
VOL9 = next(ROOT.glob("**/VOL 9 - REAL IELTS"), None)

READING_TEST = "READING/TEST 1 -R.docx"
READING_KEY = "READING/KEY - EXPLAINATION/KEY TEST 1-R.docx"
LISTENING_TEST = "LISTENING/TEST 1-L.docx"
LISTENING_KEY = "LISTENING/KEY - TRANSCRIPT/KEY TEST 1-L.docx"
LISTENING_AUDIO = "LISTENING/AUDIO/TEST 1.mp4"

HEADINGS_P2 = [
    ("i", "Training coin makers"),
    ("ii", "A form of regional promotion"),
    ("iii", "More gold than silver in Greece"),
    ("iv", "Calculating what a coin was worth"),
    ("v", "A simpler, more efficient way of trading"),
    ("vi", "Putting a face to famous historical names"),
    ("vii", "An uncertain future"),
    ("viii", "Careless workmanship caused breakages"),
    ("ix", "Unchanging coins signalled trustworthiness"),
    ("x", "Reading the writing on coins"),
    ("xi", "Significant records of past societies"),
]


def docx_paras(path: Path) -> list[str]:
    with zipfile.ZipFile(path) as archive:
        root = ET.fromstring(archive.read("word/document.xml"))
    ns = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
    paras: list[str] = []
    for para in root.findall(".//w:p", ns):
        text = "".join(t.text for t in para.findall(".//w:t", ns) if t.text)
        if text.strip():
            paras.append(text.strip())
    return paras


def parse_key(path: Path) -> dict[str, list[str]]:
    text = "\n".join(docx_paras(path))
    answers: dict[str, list[str]] = {}
    for block in re.split(r"Câu số\s+", text):
        block = block.strip()
        if not block:
            continue
        head, _, body = block.partition(":")
        label = head.strip()
        raw = body.split("...")[0].strip()
        if not raw:
            continue
        if re.match(r"^\d+\s*-\s*\d+$", label):
            first_line = raw.split("\n")[0].strip()
            parts = []
            for part in re.split(r"[,/]", first_line):
                part = part.strip()
                if not part:
                    continue
                part = re.split(r"\s+Section\s+\d", part)[0].strip()
                parts.append(part)
            answers[label.replace(" ", "")] = parts
            continue
        if label.isdigit():
            first_line = raw.split("\n")[0].strip()
            first_line = re.split(r"\s+Section\s+\d", first_line)[0].strip()
            if re.match(r"^[A-G]\b", first_line):
                answers[label] = [first_line.split()[0].rstrip(".")]
            elif re.match(r"^[ivxlc]+\b", first_line, re.I):
                answers[label] = [first_line.split()[0].lower()]
            else:
                parts = [p.strip() for p in first_line.split(",") if p.strip()]
                if len(parts) > 1 and all(len(p) < 40 for p in parts):
                    answers[label] = parts
                else:
                    answers[label] = [first_line]
    return answers


def parse_mcq_options(paras: list[str], question_num: int) -> tuple[str, list[dict]]:
    prompt = ""
    options: list[dict] = []
    in_block = False
    for line in paras:
        head = re.match(rf"^{question_num}[\s\xa0]+(.+)$", line)
        if head:
            prompt = head.group(1).strip()
            in_block = True
            continue
        if not in_block:
            continue
        option = re.match(r"^([A-E])[\s\xa0]+(.+)$", line)
        if option:
            options.append({"key": option.group(1), "text": option.group(2).strip()})
            continue
        next_q = re.match(r"^(\d+)[\s\xa0]", line)
        if next_q and int(next_q.group(1)) > question_num:
            break
    return prompt, options


READING_MCQ_FALLBACK_OPTIONS: dict[int, dict[str, str]] = {
    39: {
        "A": "is involved in the perception of bitterness, sweetness and umami",
    },
}


def normalize_tfng(value: str) -> str:
    upper = value.upper()
    if upper in {"TRUE", "FALSE", "NOT GIVEN"}:
        return upper.replace(" ", "_") if " " in upper else upper
    if upper == "NOT_GIVEN":
        return "NOT GIVEN"
    return value


def q(id_: str, order: int, qtype: str, **extra) -> dict:
    item = {"id": id_, "order": order, "type": qtype}
    item.update(extra)
    return item


def key_field(accepted: list[str], marks: int = 1) -> dict:
    out: dict = {"answerKey": {"accepted": accepted}}
    if marks > 1:
        out["marks"] = marks
    return out


def split_passages(paras: list[str]) -> list[tuple[str, list[str]]]:
    indices = [i for i, p in enumerate(paras) if re.match(r"^PASSAGE\s+\d+$", p, re.I)]
    chunks: list[tuple[str, list[str]]] = []
    for idx, start in enumerate(indices):
        end = indices[idx + 1] if idx + 1 < len(indices) else len(paras)
        block = paras[start:end]
        title = block[0]
        body: list[str] = []
        for line in block[1:]:
            if re.match(r"^Questions?\s+\d", line, re.I):
                break
            if re.match(r"^Read the text", line, re.I):
                continue
            if line.startswith("---"):
                continue
            body.append(line)
        chunks.append((title, body))
    return chunks


def reading_body(chunk: list[str]) -> str:
    lines: list[str] = []
    for line in chunk:
        if re.match(r"^\d+\s+…", line):
            continue
        if line.startswith("List of Headings"):
            break
        lines.append(line)
    return "\n\n".join(lines)


def build_reading(paras: list[str], answers: dict[str, list[str]]) -> dict:
    passages = split_passages(paras)
    titles = ["The Baobabs of Madagascar", "Coins – the first form of money", "Creating a Better Grapefruit"]
    parts = []
    questions: list[dict] = []

    # Passage 1 — T/F/NG 1-6, completion 7-13
    p1_body = reading_body(passages[0][1]) if passages else ""
    for n in range(1, 7):
        stmt = next((p for p in paras if re.match(rf"^{n}\s", p)), f"Question {n}")
        stmt = re.sub(r"^\d+\s*", "", stmt)
        questions.append(
            q(
                f"r-{n}",
                n,
                "true-false-notgiven",
                prompt=stmt,
                group={"id": "r-tfng-1-6", "instruction": "Choose TRUE, FALSE or NOT GIVEN."},
                **key_field([normalize_tfng(answers[str(n)][0])]),
            )
        )
    notes = (
        "Baobabs under threat\n\n"
        "Reasons why Madagascar's baobabs are declining in number:\n"
        "• Morondava area\n"
        "- trees may be falling down because of [7] from nearby fields\n"
        "- lone trees are at risk from [8]\n"
        "• Forest areas\n"
        "- land clearance: mainly by burning carried out to create space for [9] activity\n"
        "- practice of [10] makes seed germination difficult\n"
        "- disappearance of certain [11] which dispersed baobab seeds\n\n"
        "Jim Bond's efforts to record information about the baobab:\n"
        "- making [12] of areas of relatively undamaged baobab forest\n"
        "- producing a [13] showing importance of baobab in local culture"
    )
    for n in range(7, 14):
        questions.append(
            q(
                f"r-{n}",
                n,
                "completion",
                prompt=f"Answer for question {n}",
                group={
                    "id": "r-notes-7-13",
                    "title": "Baobabs under threat",
                    "instruction": "Write ONE WORD ONLY from the passage for each answer.",
                    "text": notes,
                },
                constraints={"maxWords": 1},
                **key_field(answers[str(n)]),
            )
        )

    # Passage 2 — headings 14-20, multi-select 21-22, completion 23-26
    p2_body = reading_body(passages[1][1]) if len(passages) > 1 else ""
    heading_opts = [{"key": k, "text": t} for k, t in HEADINGS_P2]
    for n in range(14, 21):
        questions.append(
            q(
                f"r-{n}",
                n,
                "matching",
                prompt=f"Choose the most suitable heading for paragraph {n - 13}.",
                options=heading_opts,
                group={
                    "id": "r-headings-14-20",
                    "instruction": "Choose the correct heading for each paragraph from the list below.",
                },
                **key_field([answers[str(n)][0].lower()]),
            )
        )
    mc_opts = [
        {"key": "A", "text": "There is no record of the names of the coin makers."},
        {"key": "B", "text": "Coins were not popular as payment for traded objects."},
        {"key": "C", "text": "Coins with the same design often looked quite different."},
        {"key": "D", "text": "Greek coins were more beautiful in design than Roman coins."},
        {"key": "E", "text": "At times, too many coins were produced in Greece."},
    ]
    questions.append(
        {
            **q(
                "r-21",
                21,
                "multiple-select",
                coversQuestions=[21, 22],
                prompt="Which TWO of these comments about ancient coins are made by the writer?",
                options=mc_opts,
            ),
            **key_field([answers["21-22"]], marks=2),
        }
    )
    summary = (
        "Greek traders and coins\n\n"
        "The ancient Greek [23] was made up of many city-states which were spread over a large area. "
        "Many Greeks were seafarers who travelled the seas trading. Before coins, they had to do this through [24]. "
        "This meant that, whenever they set out, they had to carry a lot of goods, which could get damaged on the voyages. "
        "The Greeks liked coins; they were made of metal, and the weather and sea had little effect on them. "
        "The Greeks had a plentiful supply of [25] which they mined themselves, but were able to avoid the problem of [26] "
        "which the Spanish encountered much later."
    )
    for n in range(23, 27):
        questions.append(
            q(
                f"r-{n}",
                n,
                "completion",
                prompt=f"Answer for question {n}",
                group={
                    "id": "r-summary-23-26",
                    "title": "Greek traders and coins",
                    "instruction": "Write ONE WORD ONLY from the passage for each answer.",
                    "text": summary,
                },
                constraints={"maxWords": 1},
                **key_field(answers[str(n)]),
            )
        )

    # Passage 3 — paragraph match 27-33, completion 34-38, MCQ 39-40
    p3_body = reading_body(passages[2][1]) if len(passages) > 2 else ""
    para_opts = [{"key": c, "text": c} for c in list("ABCDEFGHI")]
    p3_items = [
        (27, "details of an experiment in taste comparison"),
        (28, "examples of medications that could be improved using compounds that remove bitterness"),
        (29, "a biological reason why some people don't like bitter-tasting foods"),
        (30, "an explanation of how the taste process works"),
        (31, "why bitterness is more interesting commercially than other tastes"),
        (32, "an example of how compounds that remove bitterness could indirectly benefit health"),
        (33, "a reason why people have different taste preferences from animals"),
    ]
    for n, prompt in p3_items:
        questions.append(
            q(
                f"r-{n}",
                n,
                "matching",
                prompt=prompt,
                options=para_opts,
                group={
                    "id": "r-para-27-33",
                    "instruction": "Which paragraph contains the following information? Choose A–I.",
                },
                **key_field([answers[str(n)][0]]),
            )
        )
    sentence_group = (
        "The grapefruit used to make drinks is chosen because it contains a smaller amount of a substance called [34]. "
        "Animals associate a bitter taste with [35] plants. "
        "Our varying degree of sensitivity to bitter tastes is thought to be [36]. "
        "People who are extremely aware of bitter tastes are called [37]. "
        "Receptors inside the [38] on the tongue detect the taste of food and drink."
    )
    for n in range(34, 39):
        questions.append(
            q(
                f"r-{n}",
                n,
                "completion",
                prompt=f"Answer for question {n}",
                group={
                    "id": "r-sentences-34-38",
                    "instruction": "Write NO MORE THAN TWO WORDS from the passage for each answer.",
                    "text": sentence_group,
                },
                constraints={"maxWords": 2},
                **key_field(answers[str(n)]),
            )
        )
    for n in (39, 40):
        prompt, options = parse_mcq_options(paras, n)
        for key, text in READING_MCQ_FALLBACK_OPTIONS.get(n, {}).items():
            if not any(o["key"] == key for o in options):
                options.insert(0, {"key": key, "text": text})
        options.sort(key=lambda o: o["key"])
        questions.append(
            q(
                f"r-{n}",
                n,
                "multiple-choice",
                prompt=prompt or f"Question {n}",
                options=options,
                **key_field([answers[str(n)][0]]),
            )
        )

    parts = [
        {
            "order": 1,
            "kind": "passage",
            "title": titles[0],
            "body": p1_body,
            "questions": questions[:13],
        },
        {
            "order": 2,
            "kind": "passage",
            "title": titles[1],
            "body": p2_body,
            "questions": questions[13:26],
        },
        {
            "order": 3,
            "kind": "passage",
            "title": titles[2],
            "body": p3_body,
            "questions": questions[26:],
        },
    ]
    return {"module": "reading", "order": 1, "parts": parts}


def build_listening(paras: list[str], answers: dict[str, list[str]], transcript: str) -> dict:
    parts = []
    audio_ref = "../assets/audio/listening-full.mp4"

    # Section 1 — completion 1-10
    s1_text = (
        "Example: Jim wants to find out about a [0] room\n"
        "Jim's weekly rent will be [1]\n"
        "Jim can park his car in the [2]\n"
        "The telephone is in the [3]\n"
        "Jim will bring a [4] for his bedroom\n"
        "Peter would like Jim to bring a [5]\n"
        "Peter and Jim will probably watch TV on [6]\n"
        "Jim works in a [7] twice a week\n"
        "Peter will share the cost of [8] with Jim\n"
        "Peter wants Jim to move in by [9]\n"
        "Jim agrees to move in after his [10]"
    )
    s1_qs = []
    for n in range(1, 11):
        s1_qs.append(
            q(
                f"l-{n}",
                n,
                "completion",
                prompt=f"Question {n}",
                group={
                    "id": "l-s1-1-10",
                    "title": "House sharing",
                    "instruction": "Write NO MORE THAN THREE WORDS AND/OR A NUMBER for each answer.",
                    "text": s1_text,
                },
                constraints={"maxWords": 3},
                **key_field(answers[str(n)]),
            )
        )

    # Section 2 — completion 11-17, plan 18-20
    s2_opts = [
        {"key": "A", "text": "music room"},
        {"key": "B", "text": "assembly hall"},
        {"key": "C", "text": "computer room"},
        {"key": "D", "text": "cloakroom"},
        {"key": "E", "text": "Head's office"},
        {"key": "G", "text": "gym"},
        {"key": "H", "text": "reception"},
    ]
    s2_qs = []
    for n in range(11, 18):
        s2_qs.append(
            q(
                f"l-{n}",
                n,
                "completion",
                prompt=f"Question {n}",
                group={
                    "id": "l-s2-11-17",
                    "title": "Spring Down Junior School",
                    "instruction": "Write NO MORE THAN THREE WORDS AND/OR A NUMBER for each answer.",
                    "text": "Spring Down Junior School — complete the sentences.",
                },
                constraints={"maxWords": 3},
                **key_field(answers[str(n)]),
            )
        )
    for n in range(18, 21):
        letter = answers[str(n)][0].split()[0]
        s2_qs.append(
            q(
                f"l-{n}",
                n,
                "matching",
                prompt=f"Label the plan — question {n}",
                options=s2_opts,
                group={
                    "id": "l-plan-18-20",
                    "instruction": "Choose THREE correct answers A–G for questions 18–20.",
                },
                **key_field([letter]),
            )
        )

    # Section 3 — MCQ 21-24, multi-select pairs
    s3_qs = []
    mc_prompts = {
        21: "What do the students agree about defining forensic linguistics?",
        22: "Jen thinks it will be difficult to analyse the voice recording because",
        23: "Why does Jen think that the 'suspect' might vary their voice patterns?",
        24: "What does Rob think about Jen's approach to study?",
    }
    for n in range(21, 25):
        s3_qs.append(
            q(
                f"l-{n}",
                n,
                "multiple-choice",
                prompt=mc_prompts[n],
                options=[
                    {"key": "A", "text": "Option A"},
                    {"key": "B", "text": "Option B"},
                    {"key": "C", "text": "Option C"},
                ],
                **key_field([answers[str(n)][0]]),
            )
        )
    s3_qs.append(
        {
            **q(
                "l-25",
                25,
                "multiple-select",
                coversQuestions=[25, 26],
                prompt="Which TWO books do Jen and Rob decide to get from the library?",
                options=[
                    {"key": "A", "text": "Voice Identification"},
                    {"key": "B", "text": "Modern Forensic Techniques"},
                    {"key": "C", "text": "Language and Crime"},
                    {"key": "D", "text": "Forensics Today"},
                    {"key": "E", "text": "Research Methods in Forensic Linguistics"},
                ],
            ),
            **key_field([answers["25-26"]], marks=2),
        }
    )
    s3_qs.append(
        {
            **q(
                "l-27",
                27,
                "multiple-select",
                coversQuestions=[27, 28],
                prompt="Which TWO practical considerations are both Jen and Rob concerned about?",
                options=[
                    {"key": "A", "text": "how much time they have for writing up the project"},
                    {"key": "B", "text": "being able to do enough research"},
                    {"key": "C", "text": "only speaking to the 'suspect' once"},
                    {"key": "D", "text": "using recording equipment"},
                    {"key": "E", "text": "how easy it will be to contact the 'suspect'"},
                ],
            ),
            **key_field([answers["27-28"]], marks=2),
        }
    )
    s3_qs.append(
        {
            **q(
                "l-29",
                29,
                "multiple-select",
                coversQuestions=[29, 30],
                prompt="Which TWO sections of the project will Rob write alone?",
                options=[
                    {"key": "A", "text": "Methodology"},
                    {"key": "B", "text": "Literature review"},
                    {"key": "C", "text": "Introduction"},
                    {"key": "D", "text": "Analysis"},
                    {"key": "E", "text": "Discussion and conclusions"},
                ],
            ),
            **key_field([answers["29-30"]], marks=2),
        }
    )

    s4_notes = (
        "Dormancy\n\n"
        "Organisms enter a state of dormancy, or inactivity, to:\n"
        "• save available [31] …\n"
        "• reduce demands on their environment\n\n"
        "Value of dormancy\n"
        "Organisms which become dormant:\n"
        "• can live in conditions which are environmentally [32] …\n"
        "• can live in a wider range of places\n"
        "• have less [33] … from other animals\n\n"
        "Causes of dormancy\n"
        "• changes in temperature and [34] …\n"
        "• availability of food, water, oxygen\n\n"
        "Dormancy in insects\n"
        "• There may be a [35] … in their metabolism (diapause)\n\n"
        "The African lungfish\n"
        "• It buries itself in the [36] … and creates a slimy cocoon\n"
        "• Two dangers: [37] … and desiccation\n\n"
        "Snails\n"
        "• Some close their shells with a membrane during dormancy\n"
        "• Light-coloured shells reflect sunlight and limit [38] …\n\n"
        "Hibernation\n"
        "• Only warm-blooded animals hibernate\n\n"
        "The Arctic ground squirrel\n"
        "• It hibernates in an insulated, underground [39] …\n"
        "• It loses 40% of its weight during hibernation\n"
        "• When it wakes up, the first part of the body to heat up is the [40] …"
    )
    s4_qs = []
    for n in range(31, 41):
        s4_qs.append(
            q(
                f"l-{n}",
                n,
                "completion",
                prompt=f"Question {n}",
                group={
                    "id": "l-s4-31-40",
                    "title": "Dormancy",
                    "instruction": "Write ONE WORD ONLY for each answer.",
                    "text": s4_notes,
                },
                constraints={"maxWords": 1},
                **key_field(answers[str(n)]),
            )
        )

    parts = [
        {"order": 1, "kind": "recording", "title": "House sharing", "audio": audio_ref, "transcript": transcript[:8000], "questions": s1_qs},
        {"order": 2, "kind": "recording", "title": "Spring Down Junior School", "audio": audio_ref, "questions": s2_qs},
        {"order": 3, "kind": "recording", "title": "Forensic Linguistics", "audio": audio_ref, "questions": s3_qs},
        {"order": 4, "kind": "recording", "title": "Dormancy", "audio": audio_ref, "questions": s4_qs},
    ]
    return {"module": "listening", "order": 2, "parts": parts}


def load_exam1_scoring() -> dict:
    exam1 = json.loads((ROOT / "fixtures" / "exams" / "exam-1.json").read_text(encoding="utf-8"))
    scoring = exam1["scoringProfile"]
    raw = scoring["rawToBand"]
    for note in ("provisional", "note", "readingDeviation"):
        raw.pop(note, None)
    return {"rawToBand": raw, "answerMatching": scoring.get("answerMatching", {})}


def main() -> None:
    if VOL9 is None:
        raise SystemExit("VOL 9 source folder not found under repo root.")

    reading_paras = docx_paras(VOL9 / READING_TEST)
    listening_paras = docx_paras(VOL9 / LISTENING_TEST)
    reading_answers = parse_key(VOL9 / READING_KEY)
    listening_key_text = "\n".join(docx_paras(VOL9 / LISTENING_KEY))
    listening_answers = parse_key(VOL9 / LISTENING_KEY)
    transcript = listening_key_text.split("Section 1", 1)[-1] if "Section 1" in listening_key_text else ""

    PACKAGE.mkdir(parents=True, exist_ok=True)
    (PACKAGE / "reading").mkdir(exist_ok=True)
    (PACKAGE / "listening").mkdir(exist_ok=True)
    (PACKAGE / "assets" / "audio").mkdir(parents=True, exist_ok=True)

    audio_src = VOL9 / LISTENING_AUDIO
    if not audio_src.exists():
        raise SystemExit(f"Missing audio: {audio_src}")
    shutil.copy2(audio_src, PACKAGE / "assets" / "audio" / "listening-full.mp4")

    reading_section = build_reading(reading_paras, reading_answers)
    listening_section = build_listening(listening_paras, listening_answers, transcript)

    (PACKAGE / "reading" / "section.json").write_text(
        json.dumps(reading_section, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    (PACKAGE / "listening" / "section.json").write_text(
        json.dumps(listening_section, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )

    exam = {
        "formatVersion": "1.0",
        "title": "VOL 9 Test 1",
        "variant": "academic",
        "description": "VOL 9 REAL IELTS Test 1 — Reading and Listening only. Audio is one concatenated file per test; all four listening parts reference the same recording.",
        "timingProfile": {
            "sections": {
                "reading": {"durationSeconds": 3600, "transferTimeSeconds": 0},
                "listening": {"durationSeconds": 1800, "transferTimeSeconds": 600},
            }
        },
        "scoringProfile": load_exam1_scoring(),
    }
    (PACKAGE / "exam.json").write_text(json.dumps(exam, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    manifest = {
        "formatVersion": "1.0",
        "packageId": "vni-vol9-test-1",
        "createdAt": "2026-08-30T00:00:00Z",
        "createdBy": "build-vol9-test1-package.py",
        "exam": {"title": "VOL 9 Test 1", "variant": "academic"},
        "contents": {
            "exam": "exam.json",
            "sections": {"reading": "reading/section.json", "listening": "listening/section.json"},
        },
        "assets": [
            {
                "path": "assets/audio/listening-full.mp4",
                "mediaType": "video/mp4",
                "sizeBytes": audio_src.stat().st_size,
            }
        ],
        "blockingDefects": [
            "provenance: VOL 9 REAL IELTS source — learner-production rights not established (M-53)",
            "H-4: band tables are provisional/generic, not equated to this paper",
            "listening: one MP4 serves all four parts — part-level audio offsets not implemented",
        ],
        "readyToPublish": False,
    }
    (PACKAGE / "manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print(f"Wrote package under {PACKAGE.relative_to(ROOT)}")
    print(f"  reading questions: {sum(len(p['questions']) for p in reading_section['parts'])}")
    print(f"  listening questions: {sum(len(p['questions']) for p in listening_section['parts'])}")

    importer = ROOT / "scripts" / "import-exam-package.py"
    subprocess.run(
        [sys.executable, str(importer), "exam/Vol9Test1", "vol9-test-1", "reading,listening"],
        cwd=ROOT,
        check=True,
    )


if __name__ == "__main__":
    main()
