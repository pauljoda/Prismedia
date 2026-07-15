export type BookReaderCommand = "resume" | "start-over";
export type BookReaderKind = "book" | "volume" | "chapter";
export type BookReaderMode = "paged" | "webtoon";

export interface BookReaderRouteContext {
  kind: BookReaderKind;
  id: string;
  returnKind?: BookReaderKind;
  returnId?: string;
  command?: BookReaderCommand;
  mode?: BookReaderMode;
  pageIndex?: number;
  /** One-launch EPUB href/CFI override. Does not replace the saved reading cursor. */
  location?: string;
  /** One-launch whole-book EPUB fraction, used when listening is ahead of reading. */
  fraction?: number;
  /** Whether this launch should expose the active audiobook transport inside the reader. */
  combined?: boolean;
}

export interface BookReaderHrefOptions extends BookReaderRouteContext {
  bookId: string;
}

export function bookReaderCommand(url: URL): BookReaderCommand | null {
  const command = url.searchParams.get("reader");
  return command === "resume" || command === "start-over" ? command : null;
}

export function hrefWithoutBookReaderCommand(url: URL): string | null {
  if (!url.searchParams.has("reader")) return null;

  const searchParams = new URLSearchParams(url.searchParams);
  searchParams.delete("reader");
  const search = searchParams.toString();

  return `${url.pathname}${search ? `?${search}` : ""}${url.hash}`;
}

export function bookReaderContextFromUrl(url: URL): BookReaderRouteContext | null {
  const kind = readerKind(url.searchParams.get("kind"));
  const id = cleanId(url.searchParams.get("id"));
  if (!kind || !id) return null;

  const returnKind = readerKind(url.searchParams.get("returnKind"));
  const returnId = cleanId(url.searchParams.get("returnId"));
  const command = readerCommand(url.searchParams.get("command"));
  const mode = readerMode(url.searchParams.get("mode"));
  const pageIndex = pageIndexValue(url.searchParams.get("page"));
  const location = cleanId(url.searchParams.get("location"));
  const fraction = fractionValue(url.searchParams.get("fraction"));
  const combined = url.searchParams.get("combined") === "1";

  return {
    kind,
    id,
    ...(returnKind ? { returnKind } : {}),
    ...(returnId ? { returnId } : {}),
    ...(command ? { command } : {}),
    ...(mode ? { mode } : {}),
    ...(pageIndex !== null ? { pageIndex } : {}),
    ...(location ? { location } : {}),
    ...(fraction !== null ? { fraction } : {}),
    ...(combined ? { combined: true } : {}),
  };
}

export function bookReaderHref(options: BookReaderHrefOptions): string {
  const params = new URLSearchParams({
    kind: options.kind,
    id: options.id,
  });

  if (options.returnId) params.set("returnId", options.returnId);
  if (options.command) params.set("command", options.command);
  if (options.mode) params.set("mode", options.mode);
  if (typeof options.pageIndex === "number") {
    params.set("page", String(Math.max(0, Math.floor(options.pageIndex))));
  }
  if (options.location) params.set("location", options.location);
  if (typeof options.fraction === "number" && Number.isFinite(options.fraction)) {
    params.set("fraction", String(Math.max(0, Math.min(1, options.fraction))));
  }
  if (options.combined) params.set("combined", "1");

  return `/books/${encodeURIComponent(options.bookId)}/reader?${params.toString()}`;
}

export function bookReaderReturnHref(
  bookId: string,
  context: Pick<BookReaderRouteContext, "kind" | "id" | "returnKind" | "returnId">,
): string {
  const kind = context.returnKind ?? context.kind;
  const id = context.returnId ?? context.id;
  return entityHref(bookId, kind, id);
}

function entityHref(bookId: string, kind: BookReaderKind, id: string): string {
  const encodedBookId = encodeURIComponent(bookId);
  const encodedId = encodeURIComponent(id);
  if (kind === "book") return `/books/${encodedBookId}`;
  if (kind === "volume") return `/books/${encodedBookId}/volumes/${encodedId}`;
  return `/books/${encodedBookId}/chapters/${encodedId}`;
}

function readerKind(value: string | null): BookReaderKind | null {
  return value === "book" || value === "volume" || value === "chapter" ? value : null;
}

function readerCommand(value: string | null): BookReaderCommand | null {
  return value === "resume" || value === "start-over" ? value : null;
}

function readerMode(value: string | null): BookReaderMode | null {
  return value === "paged" || value === "webtoon" ? value : null;
}

function cleanId(value: string | null): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed ? trimmed : null;
}

function pageIndexValue(value: string | null): number | null {
  if (!value) return null;
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < 0) return null;
  return Math.floor(parsed);
}

function fractionValue(value: string | null): number | null {
  if (value === null) return null;
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < 0 || parsed > 1) return null;
  return parsed;
}
