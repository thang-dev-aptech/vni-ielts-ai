#!/usr/bin/env python3
"""
Documentation integrity checks for VNI IELTS AI.

This repository is documentation-only, so the documentation *is* the product.
These checks encode the conventions in docs/README.md so they are enforced
rather than remembered.

Run locally before committing:

    python3 scripts/check-docs.py

Exits non-zero if any check fails, so CI can gate on it.
"""

from __future__ import annotations

import os
import re
import sys
import urllib.parse
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SKIP_DIRS = {".git", "node_modules", "bin", "obj", "dist"}
DOC_SUFFIXES = {".md", ".mdc"}

# Files deleted on 2026-08-20. They targeted a discontinued Claude Design canvas
# project. Links to them must never come back.
DELETED = [
    "design-prompts.md",
    "design-prompts-v2.md",
    "prompt-hoan-thien-man-con-lai.md",
    "v2-web-design-audit.md",
    "web-demo-review.md",
]

failures: list[str] = []
warnings: list[str] = []
notes: list[str] = []


def doc_files() -> list[Path]:
    out = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            if Path(name).suffix in DOC_SUFFIXES:
                out.append(Path(dirpath) / name)
    return sorted(out)


def rel(p: Path) -> str:
    return str(p.relative_to(ROOT))


def fail(check: str, detail: str) -> None:
    failures.append(f"{check}: {detail}")


# ── 1 · Relative links resolve ────────────────────────────────────────────
# Several patterns, because one regex misses reference-style links and anchors.
INLINE_LINK = re.compile(r"\]\(\s*(?!https?:|mailto:|tel:|#)([^)\s]+?)\s*(?:\"[^\"]*\")?\)")
REF_LINK = re.compile(r"^\s*\[[^\]]+\]:\s*(?!https?:|mailto:|#)(\S+)", re.M)


def check_links(files: list[Path]) -> None:
    checked = 0
    for path in files:
        text = path.read_text(encoding="utf-8")
        targets = INLINE_LINK.findall(text) + REF_LINK.findall(text)
        for raw in targets:
            target = urllib.parse.unquote(raw.split("#")[0])
            if not target:
                continue  # pure anchor
            checked += 1
            resolved = (path.parent / target).resolve()
            if not resolved.exists():
                fail("broken link", f"{rel(path)} -> {target}")
    notes.append(f"{checked} relative links checked")


# ── 2 · No links to deleted files ─────────────────────────────────────────
def check_deleted(files: list[Path]) -> None:
    for path in files:
        text = path.read_text(encoding="utf-8")
        for name in DELETED:
            # A link, not a mention. next-actions.md records the deletion by name,
            # which is correct and must not trip this check.
            if re.search(r"\]\([^)]*" + re.escape(name), text):
                fail("link to deleted file", f"{rel(path)} -> {name}")


# ── 3 · Status taxonomy has no qualifiers ─────────────────────────────────
# docs/README.md states the rule using the forbidden forms as examples, so it
# is the one file allowed to contain them.
QUALIFIER = re.compile(r"\b(CONFIRMED|EXISTING|PROPOSED|UNCONFIRMED)\s*\(")


def check_qualifiers(files: list[Path]) -> None:
    for path in files:
        if rel(path) == "docs/README.md":
            continue
        for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if QUALIFIER.search(line):
                fail("status qualifier", f"{rel(path)}:{lineno} — nuance belongs in a Note column")


# ── 4 · Canonical definitions live in exactly one place ───────────────────
CANONICAL = {
    "status taxonomy": "Verifiable as present today",
    "source precedence": "Product-owner decision made in the current working session",
}


def check_canonical(files: list[Path]) -> None:
    for label, marker in CANONICAL.items():
        holders = [rel(p) for p in files if marker in p.read_text(encoding="utf-8")]
        if holders != ["docs/README.md"]:
            fail(
                "duplicated canonical definition",
                f"{label} should be defined only in docs/README.md, found in: {holders or 'nowhere'}",
            )


# ── 5 · Every CONFIRMED row cites a Source ────────────────────────────────
def check_sources() -> None:
    path = ROOT / "docs/requirements/confirmed.md"
    if not path.exists():
        fail("missing file", "docs/requirements/confirmed.md")
        return
    total = missing = 0
    for lineno, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if "| CONFIRMED |" not in line:
            continue
        total += 1
        if "Owner brief" not in line and "Owner decision" not in line:
            missing += 1
            fail("CONFIRMED without Source", f"docs/requirements/confirmed.md:{lineno}")
    notes.append(f"{total} CONFIRMED rows, {total - missing} with a traceable Source")


# ── 6 · No credential-shaped strings ──────────────────────────────────────
SECRET_PATTERNS = [
    (re.compile(r"AQ\.[A-Za-z0-9_-]{20,}"), "Google API key"),
    (re.compile(r"AIza[A-Za-z0-9_-]{30,}"), "Google API key"),
    (re.compile(r"sk-[A-Za-z0-9]{32,}"), "OpenAI-style key"),
    (re.compile(r"gh[pousr]_[A-Za-z0-9]{30,}"), "GitHub token"),
]


BINARY_SUFFIXES = {".png", ".jpg", ".jpeg", ".gif", ".docx", ".pdf", ".rar", ".zip", ".ico"}


def tracked_files() -> list[Path] | None:
    """Files git actually tracks, or None if this is not a git checkout."""
    import subprocess

    try:
        out = subprocess.run(
            ["git", "-C", str(ROOT), "ls-files", "-z"],
            capture_output=True, text=True, check=True,
        ).stdout
    except (subprocess.CalledProcessError, FileNotFoundError):
        return None
    return [ROOT / name for name in out.split("\0") if name]


def scan_for_secrets(path: Path) -> str | None:
    if path.suffix in BINARY_SUFFIXES or rel(path) == "scripts/check-docs.py":
        return None  # the patterns themselves live in this file
    try:
        text = path.read_text(encoding="utf-8")
    except (UnicodeDecodeError, OSError):
        return None
    for pattern, label in SECRET_PATTERNS:
        if pattern.search(text):
            return label
    return None


def check_secrets() -> None:
    """A committed secret fails the build. A local-only one is a warning.

    The distinction matters: a key sitting in a gitignored file is not a
    repository problem, but it is still a key sitting on someone's disk, and
    saying nothing about it would be the wrong kind of quiet.
    """
    tracked = tracked_files()

    if tracked is not None:
        scope = tracked
    else:
        # Not a git checkout — scan everything and treat it all as in scope.
        scope = []
        for dirpath, dirnames, filenames in os.walk(ROOT):
            dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
            scope.extend(Path(dirpath) / n for n in filenames)

    for path in scope:
        if not path.exists():
            continue
        label = scan_for_secrets(path)
        if label:
            fail("secret in a tracked file", f"{rel(path)} — looks like a {label}")

    if tracked is None:
        return

    # Untracked and ignored files: warn, do not fail.
    tracked_set = {p.resolve() for p in tracked}
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            path = Path(dirpath) / name
            if path.resolve() in tracked_set:
                continue
            label = scan_for_secrets(path)
            if label:
                warnings.append(
                    f"{rel(path)} holds what looks like a {label}. "
                    "It is not in git, but excluding a key is not revoking it."
                )


# ── 7 · Phase status is consistent ────────────────────────────────────────
STALE_PHASE = [
    ("Status: Phase 0", "README once claimed Phase 0 while CLAUDE.md said Phase 1"),
    ("Phase 0/1", "ambiguous phase marker"),
    ("## Phase 1 — Google Stitch", "Stitch was evaluated and dropped"),
]


def check_phase(files: list[Path]) -> None:
    for path in files:
        text = path.read_text(encoding="utf-8")
        for marker, why in STALE_PHASE:
            if marker in text:
                fail("stale phase claim", f"{rel(path)} contains {marker!r} — {why}")


def main() -> int:
    files = doc_files()
    notes.append(f"{len(files)} documentation files")

    check_links(files)
    check_deleted(files)
    check_qualifiers(files)
    check_canonical(files)
    check_sources()
    check_secrets()
    check_phase(files)

    for note in notes:
        print(f"  {note}")
    print()

    if warnings:
        print(f"WARNINGS — {len(warnings)}, not blocking:\n")
        for w in warnings:
            print(f"  {w}")
        print()

    if failures:
        print(f"FAILED — {len(failures)} problem(s):\n")
        for f in failures:
            print(f"  {f}")
        return 1

    print("All documentation checks passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
