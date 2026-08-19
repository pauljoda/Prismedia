import {
  getBookChapterMappings,
  getBookContents,
  replaceBookChapterMappings,
} from "$lib/api/generated/prismedia";
import type {
  BookChapterAudioMapping,
  BookChapterMappingsResponse,
  BookContentsResponse,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";

/** Fetches the server-projected EPUB table of contents without downloading the source archive. */
export function fetchBookContents(
  bookId: string,
  options?: RequestOptions,
): Promise<BookContentsResponse> {
  return getBookContents(bookId, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to load book contents ${bookId}`),
  );
}

/** Fetches the Book's persisted audiobook-to-readable-chapter overrides. */
export function fetchBookChapterMappings(
  bookId: string,
  options?: RequestOptions,
): Promise<BookChapterMappingsResponse> {
  return getBookChapterMappings(bookId, requestInit(options)).then((response) =>
    unwrapGenerated(response, `Failed to load chapter mappings for book ${bookId}`),
  );
}

/** Replaces the Book's complete persisted audiobook-to-readable-chapter override map. */
export function saveBookChapterMappings(
  bookId: string,
  mappings: readonly BookChapterAudioMapping[],
  options?: RequestOptions,
): Promise<BookChapterMappingsResponse> {
  return replaceBookChapterMappings(
    bookId,
    { mappings: [...mappings] },
    requestInit(options),
  ).then((response) =>
    unwrapGenerated(response, `Failed to save chapter mappings for book ${bookId}`),
  );
}
