import { render, screen, within } from "@testing-library/svelte";
import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import type { FileDetail } from "$lib/api/files";
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
    expect(propertiesCard).toBeInTheDocument();
    expect(within(propertiesCard as HTMLElement).getByRole("link", { name: "A Feature Film" })).toHaveClass("entity-thumbnail");
    expect(within(propertiesCard as HTMLElement).getByText("Kind")).toBeInTheDocument();
    expect(screen.queryByText("Linked entities")).not.toBeInTheDocument();
  });

  it("uses MetadataCard inside the properties section", () => {
    const source = readFileSync("src/lib/components/files/FileDetailPane.svelte", "utf8");

    expect(source).toContain(".properties-card");
    expect(source).toContain("MetadataCard");
    expect(source).toContain('title="Properties"');
  });

  it("renders excluded paths as filesystem-only details", () => {
    const detail = fileDetail({
      entry: {
        ...fileDetail().entry,
        excluded: true,
      },
      linkedEntities: [
        {
          entityId: "video-1",
          kind: "video",
          title: "A Feature Film",
          coverUrl: "/covers/video-1.jpg",
        },
      ],
      canPreview: true,
    });

    render(FileDetailPane, { props: { detail } });

    expect(screen.getByText("Excluded")).toBeInTheDocument();
    expect(screen.getByText("Library scans skip this path")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "A Feature Film" })).not.toBeInTheDocument();
    expect(screen.queryByText("Linked entities")).not.toBeInTheDocument();
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
