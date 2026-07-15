import { describe, expect, it } from "vitest";
import {
  bookReaderCommand,
  bookReaderContextFromUrl,
  bookReaderHref,
  bookReaderReturnHref,
  hrefWithoutBookReaderCommand,
} from "./book-reader-route";

describe("book reader route helpers", () => {
  it("recognizes only supported reader auto-open commands", () => {
    expect(bookReaderCommand(new URL("http://localhost/books/1/chapters/2?reader=resume"))).toBe(
      "resume",
    );
    expect(
      bookReaderCommand(new URL("http://localhost/books/1/chapters/2?reader=start-over")),
    ).toBe("start-over");
    expect(
      bookReaderCommand(new URL("http://localhost/books/1/chapters/2?reader=close")),
    ).toBeNull();
  });

  it("removes the reader auto-open command without dropping other URL state", () => {
    expect(
      hrefWithoutBookReaderCommand(
        new URL("http://localhost/books/1/chapters/2?reader=resume&tab=details#pages"),
      ),
    ).toBe("/books/1/chapters/2?tab=details#pages");
    expect(hrefWithoutBookReaderCommand(new URL("http://localhost/books/1/chapters/2"))).toBeNull();
  });

  it("parses a full reader route context from query params", () => {
    expect(
      bookReaderContextFromUrl(
        new URL(
          "http://localhost/books/book-1/reader?kind=volume&id=volume-1&returnId=book-1&command=resume&mode=webtoon&page=12&location=Text%2Fchapter.xhtml%23start&fraction=0.42&combined=1",
        ),
      ),
    ).toEqual({
      kind: "volume",
      id: "volume-1",
      returnId: "book-1",
      command: "resume",
      mode: "webtoon",
      pageIndex: 12,
      location: "Text/chapter.xhtml#start",
      fraction: 0.42,
      combined: true,
    });
  });

  it("rejects invalid reader route contexts", () => {
    expect(bookReaderContextFromUrl(new URL("http://localhost/books/book-1/reader"))).toBeNull();
    expect(
      bookReaderContextFromUrl(new URL("http://localhost/books/book-1/reader?kind=gallery&id=1")),
    ).toBeNull();
    expect(
      bookReaderContextFromUrl(new URL("http://localhost/books/book-1/reader?kind=chapter&id=")),
    ).toBeNull();
  });

  it("builds reader launch hrefs with encoded return context", () => {
    expect(
      bookReaderHref({
        bookId: "book 1",
        kind: "chapter",
        id: "chapter/1",
        returnId: "volume 1",
        command: "start-over",
        mode: "paged",
        pageIndex: 4,
        location: "Text/chapter 1.xhtml#start",
        fraction: 0.42,
        combined: true,
      }),
    ).toBe(
      "/books/book%201/reader?kind=chapter&id=chapter%2F1&returnId=volume+1&command=start-over&mode=paged&page=4&location=Text%2Fchapter+1.xhtml%23start&fraction=0.42&combined=1",
    );
  });

  it("builds return hrefs from explicit return context or natural fallback", () => {
    expect(
      bookReaderReturnHref("book-1", {
        kind: "chapter",
        id: "chapter-1",
        returnKind: "volume",
        returnId: "volume-1",
      }),
    ).toBe("/books/book-1/volumes/volume-1");

    expect(bookReaderReturnHref("book-1", { kind: "chapter", id: "chapter-1" })).toBe(
      "/books/book-1/chapters/chapter-1",
    );
    expect(bookReaderReturnHref("book-1", { kind: "volume", id: "volume-1" })).toBe(
      "/books/book-1/volumes/volume-1",
    );
    expect(bookReaderReturnHref("book-1", { kind: "book", id: "book-1" })).toBe("/books/book-1");
  });
});
