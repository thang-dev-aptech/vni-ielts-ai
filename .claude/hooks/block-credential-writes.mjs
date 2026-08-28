#!/usr/bin/env node

/**
 * PreToolUse guard: block writes to credential files.
 *
 * Exit codes:
 *   0 - allow
 *   2 - block, and show stderr to Claude
 */

import path from "node:path";

const blockedNames = new Set([
  "credentials.json",
  "secrets.json",
  "serviceaccount.json",
  "service-account.json",
  ".npmrc",
  ".pypirc",
  ".netrc",
]);

const blockedPatterns = [
  /^\.env(?:\..*)?$/,
  /.*\.pem$/,
  /.*\.key$/,
  /.*\.p12$/,
  /.*\.pfx$/,
  /.*\.keystore$/,
  /^id_(?:rsa|dsa|ecdsa|ed25519)$/,
];

const allowedSuffixes = [".example", ".template", ".sample", ".dist"];

function isBlocked(filePath) {
  const name = path.basename(filePath.replaceAll("\\", "/"));
  const lowerName = name.toLowerCase();

  if (allowedSuffixes.some((suffix) => lowerName.endsWith(suffix))) {
    return false;
  }

  return (
    blockedNames.has(lowerName) ||
    blockedPatterns.some((pattern) => pattern.test(lowerName))
  );
}

let input = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", (chunk) => {
  input += chunk;
});

process.stdin.on("end", () => {
  let payload;
  try {
    payload = JSON.parse(input);
  } catch {
    // A malformed hook payload must not block every repository edit.
    process.exit(0);
  }

  const toolInput = payload?.tool_input ?? {};
  const filePath = toolInput.file_path ?? toolInput.notebook_path ?? "";

  if (!filePath || !isBlocked(filePath)) {
    process.exit(0);
  }

  const name = path.basename(filePath.replaceAll("\\", "/"));
  process.stderr.write(
    [
      `BLOCKED: refusing to write '${name}'.`,
      "",
      "Credential files must not be committed to this repository.",
      "CLAUDE.md rule 6: provider credentials belong in environment configuration,",
      "never in this repository.",
      "",
      "Document configuration in a '.example' file and keep real values outside git.",
      "See docs/requirements/assumptions-and-open-questions.md.",
      "",
    ].join("\n"),
  );
  process.exit(2);
});

