#!/usr/bin/env python3
"""
PreToolUse guard: block writes to credential files.

Enforces CLAUDE.md rule 6 mechanically rather than by convention:

  > No hosted-LLM API call may be written or executed, and no AI credentials
  > may be added, until the product owner selects a provider and supplies keys.

The AI provider for this project is undecided and the Claude API is excluded
by owner decision, so no provider credentials belong in this repository at all.

Exit codes:
  0 - allow
  2 - block, and show stderr to Claude
"""

import json
import re
import sys
from pathlib import PurePath

# Filenames and patterns that must never be written in this repository.
BLOCKED_NAMES = {
    "credentials.json",
    "secrets.json",
    "serviceaccount.json",
    "service-account.json",
    ".npmrc",
    ".pypirc",
    ".netrc",
}

BLOCKED_PATTERNS = [
    re.compile(r"^\.env(\..*)?$"),        # .env, .env.local, .env.production
    re.compile(r".*\.pem$"),
    re.compile(r".*\.key$"),
    re.compile(r".*\.p12$"),
    re.compile(r".*\.pfx$"),
    re.compile(r".*\.keystore$"),
    re.compile(r"^id_(rsa|dsa|ecdsa|ed25519)$"),
]

# Allowed: template/example files carry no real secrets.
ALLOWED_SUFFIXES = (".example", ".template", ".sample", ".dist")


def is_blocked(file_path: str) -> bool:
    name = PurePath(file_path).name

    if name.endswith(ALLOWED_SUFFIXES):
        return False
    if name.lower() in BLOCKED_NAMES:
        return True
    return any(p.match(name) for p in BLOCKED_PATTERNS)


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except (json.JSONDecodeError, ValueError):
        # Never fail closed on a malformed payload - that would block all edits.
        return 0

    tool_input = payload.get("tool_input") or {}
    file_path = tool_input.get("file_path") or tool_input.get("notebook_path") or ""

    if not file_path or not is_blocked(file_path):
        return 0

    name = PurePath(file_path).name
    print(
        f"BLOCKED: refusing to write '{name}'.\n"
        "\n"
        "Credential files must not be committed to this repository.\n"
        "CLAUDE.md rule 6: no AI credentials may be added until the product owner\n"
        "selects a provider (the Claude API is excluded by owner decision).\n"
        "\n"
        "If you need to document configuration, write a '.example' file instead\n"
        "and keep real values in the environment.\n"
        "See docs/requirements/assumptions-and-open-questions.md item B-1.",
        file=sys.stderr,
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())
