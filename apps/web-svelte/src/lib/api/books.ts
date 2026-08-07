import { getBookContents } from "$lib/api/generated/prismedia";
import type { BookContentsResponse } from "$lib/api/generated/model";
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
