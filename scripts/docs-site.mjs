#!/usr/bin/env node

import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import path from "node:path";

const mode = process.argv[2];
if (!["start", "serve"].includes(mode)) {
  console.error("Usage: node scripts/docs-site.mjs <start|serve> [docusaurus args]");
  process.exit(1);
}

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const docsDir = path.join(repoRoot, "documentation-site");
const args = process.argv.slice(3);

while (args[0] === "--") {
  args.shift();
}

const child = spawn(
  "pnpm",
  ["--dir", docsDir, "exec", "docusaurus", mode, ...args],
  {
    cwd: repoRoot,
    stdio: "inherit",
    shell: process.platform === "win32",
  },
);

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }

  process.exit(code ?? 0);
});
