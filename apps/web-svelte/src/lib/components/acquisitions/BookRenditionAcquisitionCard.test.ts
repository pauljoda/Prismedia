import { cleanup, fireEvent, render, screen } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ACQUISITION_STATUS, BOOK_RENDITION, ENTITY_KIND, MONITOR_STATUS } from "$lib/api/generated/codes";
import type { AcquisitionDetail, MonitorView } from "$lib/api/generated/model";
import BookRenditionAcquisitionCard from "./BookRenditionAcquisitionCard.svelte";

vi.mock("$lib/components/acquisitions/AcquisitionPanel.svelte", async () => ({
  default: (await import("./AcquisitionPanel.test-stub.svelte")).default,
}));

describe("BookRenditionAcquisitionCard", () => {
  afterEach(() => {
    cleanup();
  });

  it("shows both rendition slots and each rendition's acquisition management", () => {
    render(BookRenditionAcquisitionCard, {
      ownership: { ebook: true, audiobook: false },
      acquisitions: [
        acquisition("ebook-acquisition", BOOK_RENDITION.ebook),
        acquisition("audio-acquisition", BOOK_RENDITION.audiobook),
      ],
      monitors: [],
      onRequest: vi.fn(),
    });

    expect(screen.getByRole("heading", { name: "Ebook" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Audiobook" })).toBeInTheDocument();
    expect(screen.getByText("In library")).toBeInTheDocument();
    expect(screen.getAllByTestId("acquisition-panel").map((panel) => panel.textContent)).toEqual([
      "ebook-acquisition",
      "audio-acquisition",
    ]);
  });

  it("synthesizes an owned ebook and requestable audiobook when no acquisition rows exist", async () => {
    const onRequest = vi.fn(async () => {});
    render(BookRenditionAcquisitionCard, {
      ownership: { ebook: true, audiobook: false },
      acquisitions: [],
      monitors: [],
      onRequest,
    });

    expect(screen.getByText("In library")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Request ebook" })).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Request audiobook" }));

    expect(onRequest).toHaveBeenCalledWith(BOOK_RENDITION.audiobook);
  });

  it("synthesizes an owned audiobook and requests the missing ebook on the same Book", async () => {
    const onRequest = vi.fn(async () => {});
    render(BookRenditionAcquisitionCard, {
      ownership: { ebook: false, audiobook: true },
      acquisitions: [],
      monitors: [],
      onRequest,
    });

    expect(screen.queryByRole("button", { name: "Request audiobook" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Request ebook" }));

    expect(onRequest).toHaveBeenCalledWith(BOOK_RENDITION.ebook);
  });

  it("stops only the selected rendition monitor", async () => {
    const audiobookMonitor = monitor("audio-monitor", BOOK_RENDITION.audiobook);
    const onToggleMonitor = vi.fn(async () => {});
    render(BookRenditionAcquisitionCard, {
      ownership: { ebook: true, audiobook: false },
      acquisitions: [],
      monitors: [monitor("ebook-monitor", BOOK_RENDITION.ebook), audiobookMonitor],
      onRequest: vi.fn(),
      onToggleMonitor,
    });

    await fireEvent.click(screen.getByRole("button", { name: "Stop monitoring audiobook" }));

    expect(onToggleMonitor).toHaveBeenCalledOnce();
    expect(onToggleMonitor).toHaveBeenCalledWith(audiobookMonitor);
  });

  it("keeps cancelled acquisition history visible while allowing the missing rendition to be requested again", async () => {
    const cancelled = acquisition("cancelled-audio", BOOK_RENDITION.audiobook);
    cancelled.summary.status = ACQUISITION_STATUS.cancelled;
    const onRequest = vi.fn(async () => {});
    render(BookRenditionAcquisitionCard, {
      ownership: { ebook: true, audiobook: false },
      acquisitions: [cancelled],
      monitors: [],
      onRequest,
    });

    expect(screen.getByTestId("acquisition-panel")).toHaveTextContent("cancelled-audio");
    await fireEvent.click(screen.getByRole("button", { name: "Request audiobook" }));

    expect(onRequest).toHaveBeenCalledWith(BOOK_RENDITION.audiobook);
  });
});

function acquisition(
  id: string,
  bookRendition: typeof BOOK_RENDITION[keyof typeof BOOK_RENDITION],
): AcquisitionDetail {
  return {
    summary: {
      id,
      status: ACQUISITION_STATUS.imported,
      statusMessage: null,
      title: "A Game of Thrones",
      author: "George R. R. Martin",
      series: null,
      year: 1996,
      posterUrl: null,
      progress: 1,
      createdAt: "2026-07-12T00:00:00Z",
      updatedAt: "2026-07-12T00:00:00Z",
      kind: ENTITY_KIND.book,
      entityId: "book-1",
      bookRendition,
    },
    candidates: [],
  };
}

function monitor(
  id: string,
  bookRendition: typeof BOOK_RENDITION[keyof typeof BOOK_RENDITION],
): MonitorView {
  return {
    id,
    kind: ENTITY_KIND.book,
    acquisitionId: null,
    status: MONITOR_STATUS.active,
    title: "A Game of Thrones",
    author: "George R. R. Martin",
    acquisitionStatus: null,
    createdAt: "2026-07-12T00:00:00Z",
    updatedAt: "2026-07-12T00:00:00Z",
    entityId: "book-1",
    bookRendition,
  };
}
