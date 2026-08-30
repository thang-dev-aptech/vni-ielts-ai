#!/usr/bin/env python3
"""
Turn an authored exam package into a development fixture.

<b>The package and the fixture are two different shapes, and the difference is
not cosmetic.</b> A package is authored per module — four `section.json` files
beside their prose, transcripts and assets — because that is how a human edits
one. A fixture is a single document validated against
`contracts/schemas/exam.schema.json`, because that is the shape the reader, the
ZIP importer and the CMS all have to agree on. This script is the seam between
them, and it exists so that seam is written once rather than by hand each time.

<b>It refuses to guess.</b> Anything the schema does not have a home for is
dropped loudly, not folded into a field that looks close enough: transcripts
stay out of the learner payload, answer keys travel but never reach a client,
and the band tables are copied verbatim because substituting one unsourced
table for another would be inventing a policy (G-11).

Usage:  python3 scripts/import-exam-package.py exam/Exam1 exam-1 [reading,listening,...]
"""

import json
import pathlib
import shutil
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
FIXTURES = ROOT / "fixtures" / "exams"
ASSETS = FIXTURES / "assets"

# The order a Full Test walks. `E-11`…`E-13`, and the package states it too.
MODULE_ORDER = ["reading", "listening", "writing", "speaking"]


def asset_name(slug: str, path: str) -> str:
    """Flatten `../assets/audio/listening-part1.mp3` to `slug-listening-part1.mp3`.

    The store serves one flat directory, and two packages both carrying
    `listening-part1.mp3` would otherwise silently overwrite each other — the
    second import would leave the first exam playing the second one's audio.
    """
    return f"{slug}-{pathlib.PurePosixPath(path).name}"


def copy_asset(package: pathlib.Path, slug: str, path: str) -> str:
    source = (package / path.replace("../", "")).resolve()
    if not source.exists():
        raise SystemExit(f"asset missing: {source}")
    ASSETS.mkdir(parents=True, exist_ok=True)
    target = ASSETS / asset_name(slug, path)
    shutil.copy2(source, target)
    return f"assets/{target.name}"


def convert_question(q: dict, package: pathlib.Path, slug: str) -> dict:
    out = {"id": q["id"], "order": q["order"], "type": q["type"]}

    if q.get("prompt"):
        out["prompt"] = q["prompt"]
    if q.get("options"):
        out["options"] = [{"key": o["key"], "text": o["text"]} for o in q["options"]]

    # <b>Marks come from the answer sheet, not from the object count.</b>
    # "Choose TWO letters" is one question object occupying two numbered lines,
    # and the band table is equated against numbered lines. The package states
    # the coverage; without carrying it, a 40-mark Listening section is scored
    # out of 36 against a table built for 40.
    covers = q.get("coversQuestions")
    if covers and len(covers) > 1:
        out["marks"] = len(covers)

    # <b>`coversQuestions` is the source; `marks` in a package is commentary.</b>
    # Both exist because the answer sheet is easier to check against a count and
    # a human editing the package wants to see the number. Only one of them is
    # read, so the other is free to drift — and a `marks` that silently disagrees
    # is worse than no `marks` at all. Refuse rather than pick a winner.
    stated = q.get("marks")
    if stated is not None and stated != out.get("marks", 1):
        raise SystemExit(
            f"{q['id']}: package says marks={stated} but coversQuestions implies "
            f"{out.get('marks', 1)}. These are the same number written twice; fix "
            f"the package so they agree."
        )

    if (group := q.get("group")) is not None:
        g = {"id": group["id"]}
        for key in ("title", "instruction", "text"):
            if group.get(key):
                g[key] = group[key]
        if group.get("image"):
            g["image"] = copy_asset(package, slug, group["image"])
        if group.get("eachLetterOnce"):
            g["eachLetterOnce"] = True
        out["group"] = g

    if (c := q.get("constraints")) is not None:
        kept = {k: v for k, v in c.items() if k in ("maxWords", "minWords")}
        if kept:
            out["constraints"] = kept

    if (key := q.get("answerKey")) is not None:
        out["answerKey"] = {"accepted": key["accepted"]}

    return out


def convert_part(p: dict, module: str, package: pathlib.Path, slug: str) -> dict:
    out = {"order": p["order"], "kind": p["kind"]}

    if p.get("title"):
        out["title"] = p["title"]

    if module == "reading" and p.get("body"):
        out["body"] = p["body"]
    if module == "listening" and p.get("audio"):
        out["audio"] = copy_asset(package, slug, p["audio"])

    if module == "writing":
        out["taskNumber"] = p["taskNumber"]
        out["body"] = p["prompt"]
        if p.get("image"):
            out["image"] = copy_asset(package, slug, p["image"])
        if (c := p.get("constraints")) and c.get("minWords"):
            out["constraints"] = {"minWords": c["minWords"]}
        if p.get("rubricRef"):
            out["rubricRef"] = p["rubricRef"]
        # <b>A writing task is one question, and it carries no prompt of its
        # own.</b> The task statement is already the part's `body`, which the
        # runner renders in the left-hand column; copying it onto the question
        # too printed the same paragraph twice on one screen — once as the task
        # and once above the box the learner types into.
        #
        # The body is the right home for it: it is the same text the paper puts
        # at the top of the page, it sits beside the chart it refers to, and it
        # stays there while the learner scrolls the essay box. A question-level
        # prompt is for a question that asks something the part does not.
        out["questions"] = [
            {
                "id": f"w-task-{p['taskNumber']}",
                "order": p["taskNumber"],
                "type": "essay-task",
            }
        ]
        return out

    if module == "speaking":
        out["partNumber"] = p["partNumber"]
        if (cue := p.get("cueCard")) is not None:
            out["cueCard"] = {"topic": cue["topic"], "bullets": list(cue["bullets"])}
            if cue.get("closing"):
                out["cueCard"]["bullets"].append(cue["closing"])
        out["body"] = speaking_body(p)
        out["questions"] = [
            {
                "id": f"s-part-{p['partNumber']}",
                "order": p["partNumber"],
                "type": "speaking-response",
                "prompt": p.get("title"),
            }
        ]
        return out

    out["questions"] = [
        convert_question(q, package, slug) for q in sorted(p["questions"], key=lambda q: q["order"])
    ]
    return out


def speaking_body(p: dict) -> str:
    """The examiner's own words, flattened into what the candidate is shown.

    <b>Not invented.</b> Every line here is `frame`, `topics` or `themes` from
    the package. `M-5` has not decided whether the prompt is delivered as text,
    a recording or a live examiner, so the text is what exists today and the
    seam for audio stays null rather than being filled with a default.
    """
    lines: list[str] = []
    frame = p.get("frame") or {}

    for key in ("opening", "transitionIn", "instructions"):
        if frame.get(key):
            lines.append(frame[key])

    for topic in p.get("topics") or []:
        lines.append(f"**{topic['title']}**")
        if topic.get("lead"):
            lines.append(topic["lead"])
        for branch in (topic.get("branches") or {}).values():
            lines.extend(f"· {q}" for q in branch)
        lines.extend(f"· {q}" for q in topic.get("questions") or [])

    for theme in p.get("themes") or []:
        lines.append(f"**{theme['title']}**")
        lines.extend(f"· {q['text']}" for q in theme.get("questions") or [])

    # <b>Named, because they arrive out of order and cannot be moved.</b>
    # The examiner asks these *after* the long turn, but `PartView` has one
    # body and one cue card, and the body renders above the card. Rather than
    # invent a second body field for one module, the block says where it
    # belongs. If Speaking grows a real script surface — `H-1` decides its
    # shape — this becomes two fields and the label goes.
    rounding = p.get("roundingOffQuestions") or []
    if rounding:
        lines.append("**Sau phần nói, giám khảo có thể hỏi thêm:**")
        lines.extend(f"· {q}" for q in rounding)

    return "\n\n".join(lines)


def scoring_profile(profile: dict) -> dict:
    """Only what the schema has a home for, and the drops are deliberate.

    <b>`overallBand` is dropped.</b> The package restates the rounding rule —
    ".25 rounds up to the half" — and that rule lives in code on purpose: it is
    the one part of band scoring stable enough not to be per-version data
    (rule 4). Two copies of a rounding rule is one copy too many, and the one
    that drifts is always the copy nobody is running.

    <b>`criterionWeights` is dropped.</b> `H-8` has not settled the Writing
    criterion set, so a per-exam weighting would be a policy invented here.

    <b>`rawToBand` is copied verbatim, including its known deviation.</b> The
    package notes that its Reading 6.0 boundary sits at raw 24 where the
    commonly published table puts it at 23. Substituting one unsourced table
    for another is not a fix, it is a different invention — `H-4` decides.
    The prose that says so stays in the package; the schema has no field for it.
    """
    kept = {"rawToBand": profile["rawToBand"]}
    if "answerMatching" in profile:
        kept["answerMatching"] = profile["answerMatching"]

    dropped = sorted(set(profile) - {"rawToBand", "answerMatching", "criterionWeights"})
    if dropped:
        print(f"  dropped from scoringProfile (no schema field): {', '.join(dropped)}")

    return kept


def write_answer_keys(package: pathlib.Path, sections: list[dict]) -> None:
    """Regenerate the package's consolidated answer key from what was just built.

    <b>A third hand-maintained copy of the answer key is a third thing that can
    be wrong.</b> `answer-keys.json` already declares the section files as its
    source of truth, but nothing enforced that — it was exported once, by hand,
    and would have drifted the first time a key changed.

    It is derived from the **converted** questions rather than re-read from the
    section files, so it reflects what the scorer will actually see, `marks`
    included. The narrative fields are human-written and are preserved verbatim:
    a generator that overwrote `status` or `correctedOn` would be discarding the
    only part of the file a person put judgement into.
    """
    out = package / "answer-keys.json"
    existing = json.loads(out.read_text(encoding="utf-8")) if out.exists() else {}
    previous = existing.get("modules", {})

    modules = {}
    for section in sections:
        if section["module"] not in ("reading", "listening"):
            continue

        items = [
            {
                "id": q["id"],
                "order": q["order"],
                "type": q["type"],
                "marks": q.get("marks", 1),
                "accepted": q["answerKey"]["accepted"],
            }
            for part in section["parts"]
            for q in part["questions"]
            if "answerKey" in q
        ]

        carried = previous.get(section["module"], {})
        modules[section["module"]] = {
            "questionObjects": len(items),
            "rawMarks": sum(i["marks"] for i in items),
            **{k: carried[k] for k in ("verifiedSimulation", "humanReadable") if k in carried},
            "items": items,
        }

    document = {
        **{k: v for k, v in existing.items() if k != "modules"},
        "modules": modules,
    }
    document.setdefault("formatVersion", "1.0")

    changed = previous and any(
        [i["id"], i["marks"], i["accepted"]]
        != [j["id"], j["marks"], j["accepted"]]
        for module, spec in modules.items()
        for i, j in zip(spec["items"], previous.get(module, {}).get("items", []))
    )

    out.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"wrote {out.relative_to(ROOT)}")
    for module, spec in modules.items():
        print(f"  {module}: {spec['questionObjects']} objects, {spec['rawMarks']} marks")
    if changed:
        print("  ⚠ keys changed since the last export — update `updatedOn` and the "
              "human-readable answer-key.md files to match")


def main() -> None:
    if len(sys.argv) not in (3, 4):
        raise SystemExit(__doc__)

    package = (ROOT / sys.argv[1]).resolve()
    slug = sys.argv[2]
    selected = MODULE_ORDER if len(sys.argv) == 3 else sys.argv[3].split(",")
    unknown = set(selected) - set(MODULE_ORDER)
    if unknown or not selected:
        raise SystemExit(f"unknown or empty module selection: {','.join(sorted(unknown))}")

    manifest = json.loads((package / "manifest.json").read_text(encoding="utf-8"))
    exam = json.loads((package / "exam.json").read_text(encoding="utf-8"))

    sections = []
    for order, module in enumerate(selected, start=1):
        path = package / module / "section.json"
        if not path.exists():
            print(f"  · no {module} section — skipped")
            continue
        section = json.loads(path.read_text(encoding="utf-8"))
        sections.append(
            {
                "module": module,
                "order": order,
                "parts": [
                    convert_part(p, module, package, slug)
                    for p in sorted(section["parts"], key=lambda p: p["order"])
                ],
            }
        )

    fixture = {
        "formatVersion": "1.0",
        "title": exam["title"],
        "variant": exam["variant"],
        "description": exam.get("description", ""),
        "timingProfile": exam["timingProfile"],
        "scoringProfile": scoring_profile(exam["scoringProfile"]),
        "sections": sections,
    }

    # The tables' own prose does not fit the schema and does not need to — it
    # is provenance, and provenance lives with the package, not with the copy
    # the seeder loads.
    raw = fixture["scoringProfile"]["rawToBand"]
    for note in ("provisional", "note", "readingDeviation"):
        raw.pop(note, None)

    FIXTURES.mkdir(parents=True, exist_ok=True)
    out = FIXTURES / f"{slug}.json"
    out.write_text(json.dumps(fixture, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    counts = {s["module"]: sum(len(p["questions"]) for p in s["parts"]) for s in sections}
    print(f"wrote {out.relative_to(ROOT)}")
    write_answer_keys(package, sections)
    print(f"  modules: {counts}")
    if manifest.get("blockingDefects"):
        print("  package declares blocking defects:")
        for d in manifest["blockingDefects"]:
            print(f"    · {d}")


if __name__ == "__main__":
    main()
