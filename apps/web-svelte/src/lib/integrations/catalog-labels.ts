import { ACQUISITION_ACCESS_KIND, type AcquisitionAccessKindCode } from "$lib/api/generated/codes";

/** User-facing access constraints; only a full publication can satisfy a download request. */
export const acquisitionAccessLabels: Record<AcquisitionAccessKindCode, string> = {
  [ACQUISITION_ACCESS_KIND.download]: "Full publication",
  [ACQUISITION_ACCESS_KIND.borrow]: "Loan",
  [ACQUISITION_ACCESS_KIND.purchase]: "Purchase",
  [ACQUISITION_ACCESS_KIND.sample]: "Sample",
  [ACQUISITION_ACCESS_KIND.external]: "External service",
};

/** Familiar publication format names for standard MIME types returned by a source. */
export function publicationFormatLabel(mediaType: string | null | undefined): string | null {
  switch (mediaType?.split(";")[0].trim().toLowerCase()) {
    case "application/epub+zip": return "EPUB";
    case "application/pdf": return "PDF";
    case "application/vnd.comicbook+zip":
    case "application/x-cbz": return "CBZ";
    case "application/vnd.comicbook-rar":
    case "application/x-cbr": return "CBR";
    case "application/x-mobipocket-ebook": return "MOBI";
    case "application/vnd.amazon.ebook": return "Kindle";
    default: return null;
  }
}
