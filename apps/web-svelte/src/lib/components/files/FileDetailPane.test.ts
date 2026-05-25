import { render, screen, within } from "@testing-library/svelte";
import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { FileDetail } from "$lib/api/prismedia";
import FileDetailPane from "./FileDetailPane.svelte";

describe("FileDetailPane", () => {
  it("renders the primary linked entity thumbnail beside the properties card", () => {
    const detail = fileDetail({
      linkedEntities: [
        {
          entityId: "video-1",
          kind: "video",
          title: "A Feature Film",
          coverUrl: "/covers/video-1.jpg",
        },
      ],
    });

    const { container } = render(FileDetailPane, { props: { detail } });

    const propertiesCard = container.querySelector(".properties-card");
    const propertiesRow = container.querySelector(".properties-row");
    const metaGrid = container.querySelector(".meta-grid");
    expect(propertiesCard).toBeInTheDocument();
    expect(propertiesRow).toBeInTheDocument();
    expect(propertiesCard).toContainElement(propertiesRow as HTMLElement);
    expect(propertiesCard).toContainElement(metaGrid as HTMLElement);
    expect(within(propertiesCard as HTMLElement).getByRole("link", { name: "A Feature Film" })).toHaveClass("entity-thumbnail");
    expect(within(propertiesCard as HTMLElement).getByText("Kind")).toBeInTheDocument();
    expect(screen.queryByText("Linked entities")).not.toBeInTheDocument();
  });

  it("uses the properties card as the metadata background instead of nesting a metadata card", () => {
    const source = readFileSync("src/lib/components/files/FileDetailPane.svelte", "utf8");

    expect(source).toContain(".properties-card");
    expect(source).toContain(".meta-grid {");
    expect(source).toContain("background: transparent;");
    expect(source).not.toContain(".meta-grid {\n    display: grid;\n    grid-template-columns: repeat(2, minmax(0, 1fr));\n    border: 1px solid var(--color-border-subtle);");
  });
});

function fileDetail(overrides: Partial<FileDetail> = {}): FileDetail {
  return {
    entry: {
      rootId: "root-1",
      path: "movies/feature-film.mkv",
      name: "feature-film.mkv",
      kind: "file",
      sizeBytes: 1_048_576,
      mimeType: "video/x-matroska",
      modifiedAt: "2026-04-10T12:35:00Z",
    },
    absolutePath: "/media/movies/feature-film.mkv",
    createdAt: "2026-04-09T21:07:00Z",
    linkedEntities: [],
    canPreview: false,
    ...overrides,
  };
}
