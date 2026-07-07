import { describe, expect, it } from "vitest";
import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

const repoRoot = path.resolve(import.meta.dirname, "..", "..");
const sourceRoot = path.join(repoRoot, "apps/web-svelte/src");
const transitionalApiImportPattern = /from\s+["']\$lib\/api\/(?:prismedia|identify)["']/;
const contractsNamedImportPattern = /import\s+(?:type\s+)?\{([\s\S]*?)\}\s+from\s+["']@prismedia\/contracts["']/g;
const legacyFrontendDtoNames = [
  "AudioLibraryListItemDto",
  "AudioTrackListItemDto",
  "BookChapterDto",
  "BookProgressDto",
  "GalleryListItemDto",
  "ImageListItemDto",
  "LibraryRootSummaryDto",
  "VideoDetailDto",
];

const allowedTransitionalApiImports = new Set([
  "apps/web-svelte/src/lib/api/identify.ts",
  "apps/web-svelte/src/lib/components/ImageLightboxDetails.svelte",
  "apps/web-svelte/src/lib/components/VideoMarkerEditor.svelte",
  "apps/web-svelte/src/lib/components/entities/EntityDetail.svelte",
  "apps/web-svelte/src/lib/components/entities/EntityGrid.svelte",
  "apps/web-svelte/src/lib/components/entities/EntityIndexPage.svelte",
  "apps/web-svelte/src/lib/components/entities/entity-index-page.test.ts",
  "apps/web-svelte/src/lib/components/files/FileDetailPane.svelte",
  "apps/web-svelte/src/lib/components/files/FileDetailPane.test.ts",
  "apps/web-svelte/src/lib/components/identify-review.test.ts",
  "apps/web-svelte/src/lib/components/identify-review.ts",
  "apps/web-svelte/src/lib/components/identify/IdentifyKindTab.svelte",
  "apps/web-svelte/src/lib/components/identify/IdentifyKindTab.test.ts",
  "apps/web-svelte/src/lib/components/identify/IdentifyReviewChild.svelte",
  "apps/web-svelte/src/lib/components/identify/IdentifyReviewChoice.svelte",
  "apps/web-svelte/src/lib/components/identify/IdentifyReviewChoice.test.ts",
  "apps/web-svelte/src/lib/components/identify/IdentifyReviewParent.svelte",
  "apps/web-svelte/src/lib/components/identify/IdentifyReviewSurfaces.test.ts",
  "apps/web-svelte/src/lib/components/identify/identify-candidate-card.test.ts",
  "apps/web-svelte/src/lib/components/identify/identify-candidate-card.ts",
  "apps/web-svelte/src/lib/components/identify/identify-review-helpers.ts",
  "apps/web-svelte/src/lib/components/identify/identify-store.svelte.ts",
  "apps/web-svelte/src/lib/components/identify/identify-store.test.ts",
  "apps/web-svelte/src/lib/components/settings/DiagnosticsSection.svelte",
  "apps/web-svelte/src/lib/components/settings/SettingsControl.svelte",
  "apps/web-svelte/src/lib/components/settings/SettingsControl.test.ts",
  "apps/web-svelte/src/lib/components/settings/WatchedLibrariesSection.svelte",
  "apps/web-svelte/src/lib/components/universal-lightbox-media.test.ts",
  "apps/web-svelte/src/lib/components/universal-lightbox-media.ts",
  "apps/web-svelte/src/lib/entities/video-capabilities.ts",
  "apps/web-svelte/src/lib/files/file-tree-state.test.ts",
  "apps/web-svelte/src/lib/files/file-tree-state.ts",
  "apps/web-svelte/src/lib/jobs/jobs-dashboard.test.ts",
  "apps/web-svelte/src/lib/jobs/jobs-dashboard.ts",
  "apps/web-svelte/src/lib/jobs/worker-health.ts",
  "apps/web-svelte/src/lib/settings/app-settings.ts",
  "apps/web-svelte/src/routes/+layout.ts",
  "apps/web-svelte/src/routes/audio/[id]/+page.svelte",
  "apps/web-svelte/src/routes/audio/tracks/[id]/+page.svelte",
  "apps/web-svelte/src/routes/books/[id]/+page.svelte",
  "apps/web-svelte/src/routes/books/[id]/chapters/[chapterId]/+page.svelte",
  "apps/web-svelte/src/routes/books/[id]/reader/+page.svelte",
  "apps/web-svelte/src/routes/books/[id]/volumes/[volumeId]/+page.svelte",
  "apps/web-svelte/src/routes/books/book-reader-next-chapter.test.ts",
  "apps/web-svelte/src/routes/collections/[id]/+page.svelte",
  "apps/web-svelte/src/routes/files/+page.svelte",
  "apps/web-svelte/src/routes/galleries/[id]/+page.svelte",
  "apps/web-svelte/src/routes/images/[id]/+page.svelte",
  "apps/web-svelte/src/routes/jobs/+page.svelte",
  "apps/web-svelte/src/routes/people/[id]/+page.svelte",
  "apps/web-svelte/src/routes/plugins/+page.svelte",
  "apps/web-svelte/src/routes/series/[id]/+page.svelte",
  "apps/web-svelte/src/routes/series/[id]/seasons/[seasonId]/+page.svelte",
  "apps/web-svelte/src/routes/settings/+page.svelte",
  "apps/web-svelte/src/routes/studios/[id]/+page.svelte",
  "apps/web-svelte/src/routes/tags/[id]/+page.svelte",
  "apps/web-svelte/src/routes/videos/[id]/+page.svelte",
  "apps/web-svelte/src/routes/videos/[id]/video-page-state.test.ts",
  "apps/web-svelte/src/routes/videos/[id]/video-page-state.ts",
]);

describe("frontend architecture boundaries", () => {
  it("does not grow imports from transitional handwritten API facades", () => {
    const actual = sourceFiles(sourceRoot)
      .filter((file) => transitionalApiImportPattern.test(readFileSync(file, "utf8")))
      .map((file) => path.relative(repoRoot, file).split(path.sep).join("/"))
      .sort();

    const unexpected = actual.filter((file) => !allowedTransitionalApiImports.has(file));

    expect(unexpected).toEqual([]);
  });

  it("does not import legacy frontend DTOs from contracts", () => {
    const offenders = sourceFiles(sourceRoot)
      .flatMap((file) => {
        const source = readFileSync(file, "utf8");
        const importedNames = [...source.matchAll(contractsNamedImportPattern)].flatMap((match) =>
          match[1]!
            .split(",")
            .map((specifier) => specifier.trim().replace(/^type\s+/, "").split(/\s+as\s+/)[0]?.trim())
            .filter((specifier): specifier is string => Boolean(specifier)),
        );
        const importedLegacyNames = legacyFrontendDtoNames.filter((name) =>
          importedNames.includes(name),
        );
        if (importedLegacyNames.length === 0) return [];

        return [{
          file: path.relative(repoRoot, file).split(path.sep).join("/"),
          names: importedLegacyNames.sort(),
        }];
      })
      .sort((left, right) => left.file.localeCompare(right.file));

    expect(offenders).toEqual([]);
  });
});

function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const fullPath = path.join(directory, entry);
    const stats = statSync(fullPath);

    if (stats.isDirectory()) {
      if (entry === "generated" || entry === "node_modules") return [];
      return sourceFiles(fullPath);
    }

    return /\.(?:svelte|svelte\.ts|ts)$/.test(entry) ? [fullPath] : [];
  });
}
