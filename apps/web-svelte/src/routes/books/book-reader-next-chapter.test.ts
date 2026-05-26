import { fireEvent, render, waitFor } from "@testing-library/svelte";
import { afterEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import Page from "./[id]/reader/+page.svelte";
import type { BookDetail, EntityCardFull } from "$lib/api/prismedia";
import type { EntityThumbnail } from "$lib/api/generated/model";

const mocks = vi.hoisted(() => ({
  fetchBook: vi.fn(),
  fetchEntity: vi.fn(),
  goto: vi.fn(async () => {}),
  updateEntityProgress: vi.fn(),
}));

vi.mock("$lib/api/prismedia", async (importOriginal) => {
  const actual = await importOriginal<typeof import("$lib/api/prismedia")>();
  return {
    ...actual,
    fetchBook: mocks.fetchBook,
    fetchEntity: mocks.fetchEntity,
    updateEntityProgress: mocks.updateEntityProgress,
  };
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

    mocks.fetchBook.mockResolvedValue(book);
    mocks.fetchEntity.mockImplementation((id: string) => Promise.resolve(chapters.get(id) ?? book));
    mocks.updateEntityProgress.mockResolvedValue(undefined);

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
    await findByText("Prismedia Book · Chapter Two");
  });
});
