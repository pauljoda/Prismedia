import { fireEvent, render, waitFor } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import Page from "./[id]/reader/+page.svelte";
import type { BookDetail } from "$lib/api/media";
import type { EntityCardFull } from "$lib/api/entities";
import type { EntityThumbnail } from "$lib/api/generated/model";

const mocks = vi.hoisted(() => ({
  fetchBook: vi.fn(),
  fetchEntity: vi.fn(),
  goto: vi.fn(async () => {}),
  updateEntityProgress: vi.fn(),
}));

vi.mock("$lib/api/media", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/media")>();
  return { ...actual, fetchBook: mocks.fetchBook };
});

vi.mock("$lib/api/entities", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/entities")>();
  return { ...actual, fetchEntity: mocks.fetchEntity };
});

vi.mock("$lib/api/playback", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/playback")>();
  return { ...actual, updateEntityProgress: mocks.updateEntityProgress };
});

vi.mock("$app/navigation", () => ({
  goto: mocks.goto,
  invalidate: vi.fn(async () => {}),
  invalidateAll: vi.fn(async () => {}),
}));

vi.mock("$lib/nsfw/store.svelte", () => ({
  useNsfw: () => ({ mode: "show" }),
}));

function thumbnail(id: string, kind: string, title: string, sortOrder: number): EntityThumbnail {
  return {
    id,
    kind,
    title,
    parentEntityId: null,
    sortOrder,
    coverUrl: null,
    coverThumbUrl: null,
    hoverKind: "none",
    hoverUrl: null,
    hoverImages: [],
    meta: [],
    rating: null,
    isFavorite: false,
    isNsfw: false,
    isOrganized: true,
  };
}

function bookDetail(): BookDetail {
  return {
    id: "book-1",
    kind: "book",
    title: "Prismedia Book",
    parentEntityId: null,
    sortOrder: 0,
    bookType: "comic",
    format: "image-archive",
    coverPageId: null,
    capabilities: [],
    childrenByKind: [
      {
        kind: "book-chapter",
        label: "Chapters",
        entities: [
          thumbnail("chapter-1", "book-chapter", "Chapter One", 0),
          thumbnail("chapter-2", "book-chapter", "Chapter Two", 1),
        ],
      },
    ],
    relationships: [],
  };
}

function volumeBookDetail(): BookDetail {
  return {
    ...bookDetail(),
    capabilities: [
      {
        kind: "progress",
        currentEntityId: "chapter-2",
        unit: "page",
        index: 1,
        total: 2,
        mode: "paged",
        completedAt: null,
        updatedAt: "2026-05-26T00:00:00.000Z",
        workIndex: 1,
        workTotal: 6,
      },
    ],
    childrenByKind: [
      {
        kind: "book-volume",
        label: "Volumes",
        entities: [
          thumbnail("volume-1", "book-volume", "Volume One", 0),
          thumbnail("volume-2", "book-volume", "Volume Two", 1),
        ],
      },
    ],
  };
}

function chapterDetail(id: string, title: string): EntityCardFull {
  return {
    id,
    kind: "book-chapter",
    title,
    parentEntityId: "book-1",
    sortOrder: id === "chapter-1" ? 0 : 1,
    capabilities: [],
    childrenByKind: [
      {
        kind: "book-page",
        label: "Pages",
        entities: [
          thumbnail(`${id}-page-1`, "book-page", `${title} Page 1`, 0),
          thumbnail(`${id}-page-2`, "book-page", `${title} Page 2`, 1),
        ],
      },
    ],
    relationships: [],
  };
}

function volumeDetail(id: string, chapterIds: string[]): EntityCardFull {
  return {
    id,
    kind: "book-volume",
    title: id === "volume-1" ? "Volume One" : "Volume Two",
    parentEntityId: "book-1",
    sortOrder: id === "volume-1" ? 0 : 1,
    capabilities: [],
    childrenByKind: [
      {
        kind: "book-chapter",
        label: "Chapters",
        entities: chapterIds.map((chapterId, index) =>
          thumbnail(chapterId, "book-chapter", `Chapter ${index + 1}`, index),
        ),
      },
    ],
    relationships: [],
  };
}

describe("book reader next chapter navigation", () => {
  afterEach(() => {
    vi.clearAllMocks();
    page.url = new URL("http://localhost/") as unknown as typeof page.url;
    page.params = {};
    document.body.querySelectorAll('[role="dialog"]').forEach((dialog) => dialog.remove());
  });

  it("loads the next chapter instead of restarting the same chapter", async () => {
    const book = bookDetail();
    const chapters = new Map([
      ["chapter-1", chapterDetail("chapter-1", "Chapter One")],
      ["chapter-2", chapterDetail("chapter-2", "Chapter Two")],
    ]);

    page.params = { id: book.id };
    page.url = new URL(
      "http://localhost/books/book-1/reader?kind=chapter&id=chapter-1&returnId=book-1",
    ) as unknown as typeof page.url;

    let activeProgressSaves = 0;
    let maxActiveProgressSaves = 0;
    mocks.fetchBook.mockResolvedValue(book);
    mocks.fetchEntity.mockImplementation((id: string) => Promise.resolve(chapters.get(id) ?? book));
    mocks.updateEntityProgress.mockImplementation(async () => {
      activeProgressSaves++;
      maxActiveProgressSaves = Math.max(maxActiveProgressSaves, activeProgressSaves);
      await new Promise((resolve) => setTimeout(resolve, 20));
      activeProgressSaves--;
    });

    const { findByText, getByLabelText, getByText } = render(Page);

    await findByText("Prismedia Book · Chapter One");

    await fireEvent.click(getByLabelText("Next page"));
    await fireEvent.click(getByLabelText("Next page"));
    await fireEvent.click(getByText("Continue reading"));

    await waitFor(() => {
      expect(mocks.fetchEntity).toHaveBeenCalledWith("chapter-2");
    });
    expect(mocks.goto).toHaveBeenCalledWith(
      "/books/book-1/reader?kind=chapter&id=chapter-2&returnId=book-1&command=resume&mode=paged",
    );
    expect(maxActiveProgressSaves).toBe(1);
    await findByText("Prismedia Book · Chapter Two");
  });

  it("opens book resume from the saved chapter without loading the whole book", async () => {
    const book = volumeBookDetail();
    const progressChapter = chapterDetail("chapter-2", "Chapter Two");
    progressChapter.parentEntityId = "volume-1";
    const volume = volumeDetail("volume-1", ["chapter-1", "chapter-2", "chapter-3"]);

    page.params = { id: book.id };
    page.url = new URL(
      "http://localhost/books/book-1/reader?kind=book&id=book-1&returnId=book-1&command=resume",
    ) as unknown as typeof page.url;

    mocks.fetchBook.mockResolvedValue(book);
    mocks.fetchEntity.mockImplementation((id: string) => {
      if (id === "chapter-2") return Promise.resolve(progressChapter);
      if (id === "volume-1") return Promise.resolve(volume);
      throw new Error(`Unexpected eager entity load: ${id}`);
    });
    mocks.updateEntityProgress.mockResolvedValue(undefined);

    const { findByText } = render(Page);

    await findByText("Prismedia Book · Chapter Two");
    expect(mocks.fetchEntity).toHaveBeenCalledWith("chapter-2");
    expect(mocks.fetchEntity).toHaveBeenCalledWith("volume-1");
    expect(mocks.fetchEntity).not.toHaveBeenCalledWith("chapter-1");
    expect(mocks.fetchEntity).not.toHaveBeenCalledWith("chapter-3");
    expect(mocks.fetchEntity).not.toHaveBeenCalledWith("volume-2");
  });
});
