import { readdirSync, readFileSync, statSync } from "node:fs";
import { basename, extname, join, relative, resolve } from "node:path";
import { describe, expect, it } from "vitest";

const sourceRoot = resolve(process.cwd(), "src");
const guardrailFile = "lib/api/api-facade-boundary.test.ts";
const scannedExtensions = new Set([".svelte", ".ts", ".js"]);

const trackedFacades = [
  { modulePath: "$lib/api/prismedia", currentImporters: 40 },
  { modulePath: "$lib/api/identify", currentImporters: 14 },
] as const;

describe("frontend API facade boundary", () => {
  it.each(trackedFacades)(
    "does not add new $modulePath importers",
    ({ modulePath, currentImporters }) => {
      const importers = sourceFiles(sourceRoot)
        .map((file) => ({
          absolutePath: file,
          relativePath: relative(sourceRoot, file).replaceAll("\\", "/"),
        }))
        .filter(({ relativePath }) => relativePath !== guardrailFile)
        .filter(({ relativePath }) => !relativePath.startsWith("lib/api/generated/"))
        .filter(({ absolutePath }) => readFileSync(absolutePath, "utf8").includes(modulePath))
        .map(({ relativePath }) => relativePath)
        .sort((left, right) => left.localeCompare(right));

      expect(
        importers,
        `${modulePath} importer count grew. Migrate callers to generated clients or update the ceiling only after an intentional review.`,
      ).toHaveLength(currentImporters);
    },
  );
});

function sourceFiles(directory: string): string[] {
  return readdirSync(directory)
    .filter((name) => !name.startsWith("."))
    .flatMap((name) => {
      const path = join(directory, name);
      const stats = statSync(path);
      if (stats.isDirectory()) {
        return sourceFiles(path);
      }

      return scannedExtensions.has(extname(path)) && basename(path) !== "service-worker.ts"
        ? [path]
        : [];
    });
}
