import {
  BOOK_FORMAT,
  BOOK_TYPE,
  type BookFormatCode,
  type BookTypeCode,
} from "$lib/api/generated/codes";

type BookFilterCode = BookTypeCode | BookFormatCode;
type BookFilterFamily = "type" | "format";

/** One server-resolved book type or format choice shown by the shared entity grid. */
export interface EntityGridBookFilterDefinition {
  id: string;
  family: BookFilterFamily;
  code: BookFilterCode;
  label: string;
}

function bookFilterDefinition(
  family: BookFilterFamily,
  code: BookFilterCode,
  label: string,
): EntityGridBookFilterDefinition {
  return { id: `book-${family}:${code}`, family, code, label };
}

/** Book type filters resolved by the list endpoint against the book detail row. */
export const BOOK_TYPE_FILTER_DEFS = [
  bookFilterDefinition("type", BOOK_TYPE.book, "Book"),
  bookFilterDefinition("type", BOOK_TYPE.comic, "Comic"),
  bookFilterDefinition("type", BOOK_TYPE.manga, "Manga"),
  bookFilterDefinition("type", BOOK_TYPE.novel, "Novel"),
] as const;

/** Book format filters resolved by the list endpoint against the book detail row. */
export const BOOK_FORMAT_FILTER_DEFS = [
  bookFilterDefinition("format", BOOK_FORMAT.imageArchive, "Comic Archive"),
  bookFilterDefinition("format", BOOK_FORMAT.epub, "EPUB"),
  bookFilterDefinition("format", BOOK_FORMAT.pdf, "PDF"),
] as const;

const BOOK_FILTER_BY_ID = new Map<string, EntityGridBookFilterDefinition>(
  [...BOOK_TYPE_FILTER_DEFS, ...BOOK_FORMAT_FILTER_DEFS].map((definition) => [
    definition.id,
    definition,
  ]),
);

/** Returns the canonical type or format definition for a persisted grid filter ID. */
export function bookFilterDefinitionFromId(
  id: string,
): EntityGridBookFilterDefinition | undefined {
  return BOOK_FILTER_BY_ID.get(id);
}
