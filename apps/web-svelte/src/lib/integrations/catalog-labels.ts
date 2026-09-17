import { ACQUISITION_ACCESS_KIND, ENTITY_KIND, type AcquisitionAccessKindCode } from "$lib/api/generated/codes";
import type { CatalogOffer, EntityKind } from "$lib/api/generated/model";

/** User-facing access constraints; only a full publication can satisfy a download request. */
export const acquisitionAccessLabels: Record<AcquisitionAccessKindCode, string> = {
  [ACQUISITION_ACCESS_KIND.download]: "Full download",
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
    case "image/jpeg": return "JPEG";
    case "image/png": return "PNG";
    case "image/webp": return "WebP";
    default: return null;
  }
}

/** Only supported full-content formats can enter the publication importer. The server validates the resolved file too. */
export function canImportPublication(kind: EntityKind, offer: CatalogOffer): boolean {
  if (offer.access !== ACQUISITION_ACCESS_KIND.download) return false;
  const format = publicationFormatLabel(offer.mediaType);
  return kind === ENTITY_KIND.book ? format === "EPUB" || format === "PDF"
    : kind === ENTITY_KIND.comicInstallment ? format === "CBZ"
    : kind === ENTITY_KIND.image && (format === "JPEG" || format === "PNG" || format === "WebP");
}
