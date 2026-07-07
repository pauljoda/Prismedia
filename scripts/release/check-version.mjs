import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = join(fileURLToPath(new URL(".", import.meta.url)), "..", "..");
const pkg = JSON.parse(readFileSync(join(repoRoot, "package.json"), "utf8"));
const changelog = readFileSync(join(repoRoot, "CHANGELOG.md"), "utf8");
const versionPattern = /^\d+\.\d+\.\d+$/;
const requiredChangelogSections = [
  "### What's New",
  "### Added",
  "### Changed",
  "### Fixed",
  "### Removed",
  "### Docs",
];

if (!changelog.includes("## [Unreleased]")) {
  console.error("CHANGELOG.md must contain an [Unreleased] section.");
  process.exit(1);
}

for (const section of requiredChangelogSections) {
  if (!changelog.includes(section)) {
    console.error(`CHANGELOG.md must contain ${section}.`);
    process.exit(1);
  }
}

if (!versionPattern.test(pkg.version)) {
  console.error(`package.json version "${pkg.version}" is not valid (expected X.Y.Z).`);
  process.exit(1);
}

const packageJsons = [
  "apps/web-svelte/package.json",
  "documentation-site/package.json",
  "packages/contracts/package.json",
  "packages/ui-svelte/package.json",
];

for (const relativePath of packageJsons) {
  const packagePath = join(repoRoot, relativePath);
  if (!existsSync(packagePath)) continue;

  const workspacePkg = JSON.parse(readFileSync(packagePath, "utf8"));
  if (workspacePkg.version !== pkg.version) {
    console.error(`${relativePath} version ${workspacePkg.version} must match root version ${pkg.version}.`);
    process.exit(1);
  }
}

process.exit(0);
