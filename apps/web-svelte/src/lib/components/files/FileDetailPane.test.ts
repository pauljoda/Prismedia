import { render, screen, within } from "@testing-library/svelte";
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

    const propertiesRow = container.querySelector(".properties-row");
    expect(propertiesRow).toBeInTheDocument();
    expect(within(propertiesRow as HTMLElement).getByRole("link", { name: "A Feature Film" })).toHaveClass("entity-thumbnail");
    expect(within(propertiesRow as HTMLElement).getByText("Kind")).toBeInTheDocument();
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
