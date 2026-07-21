import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { DOWNLOAD_PROTOCOL, RELEASE_REJECTION_REASON } from "$lib/api/generated/codes";
import type { ReleaseCandidateView } from "$lib/api/generated/model";
import ReleaseTable from "./ReleaseTable.svelte";

describe("ReleaseTable", () => {
  it("labels every supported release protocol with its own chip", () => {
    render(ReleaseTable, {
      props: {
        candidates: [
          candidate({ id: "usenet-release", protocol: DOWNLOAD_PROTOCOL.usenet }),
          candidate({ id: "torrent-release", protocol: DOWNLOAD_PROTOCOL.torrent }),
          candidate({ id: "soulseek-release", protocol: DOWNLOAD_PROTOCOL.soulseek }),
        ],
        canChoose: true,
        busy: false,
        onQueue: vi.fn(),
      },
    });

    expect(screen.getAllByText("Usenet")).toHaveLength(2);
    expect(screen.getAllByText("Torrent")).toHaveLength(2);
    expect(screen.getAllByText("Soulseek")).toHaveLength(2);
  });

  it("reveals release candidates ten at a time", async () => {
    const candidates = Array.from({ length: 12 }, (_, index) =>
      candidate({ id: `release-${index + 1}`, title: `Release ${index + 1}` }),
    );

    render(ReleaseTable, {
      props: {
        candidates,
        canChoose: true,
        busy: false,
        onQueue: vi.fn(),
      },
    });

    expect(screen.queryByText("Release 11")).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Load 2 more releases" })).toHaveLength(2);

    await fireEvent.click(screen.getAllByRole("button", { name: "Load 2 more releases" })[0]);

    expect(screen.getAllByText("Release 11")).toHaveLength(2);
    expect(screen.queryByRole("button", { name: /Load .* more releases/ })).not.toBeInTheDocument();
  });

  it("lets the user manually download a rejected release", async () => {
    const onQueue = vi.fn();
    const rejected = candidate({
      accepted: false,
      rejections: [RELEASE_REJECTION_REASON.titleMismatch],
    });

    render(ReleaseTable, {
      props: {
        candidates: [rejected],
        canChoose: true,
        busy: false,
        onQueue,
      },
    });

    expect(screen.getAllByText("title mismatch")).toHaveLength(2);
    const downloadActions = screen.getAllByRole("button", { name: "Download anyway" });

    await fireEvent.click(downloadActions[0]);

    expect(onQueue).toHaveBeenCalledWith(rejected);
  });

  it("does not offer releases that cannot be queued safely", () => {
    render(ReleaseTable, {
      props: {
        candidates: [candidate({ accepted: false, rejections: [RELEASE_REJECTION_REASON.dangerousContent] })],
        canChoose: true,
        busy: false,
        onQueue: vi.fn(),
      },
    });

    expect(screen.queryByRole("button", { name: /Download/ })).not.toBeInTheDocument();
  });
});

function candidate(overrides: Partial<ReleaseCandidateView> = {}): ReleaseCandidateView {
  return {
    id: "release-1",
    indexerName: "Prowlarr",
    title: "The release the user wants",
    sizeBytes: 255_100_000,
    seeders: 4,
    peers: 2,
    protocol: DOWNLOAD_PROTOCOL.torrent,
    accepted: true,
    score: 100,
    rejections: [],
    infoUrl: null,
    publishedAt: null,
    ...overrides,
  };
}
