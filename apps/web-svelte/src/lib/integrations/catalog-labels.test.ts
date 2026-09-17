import { describe, expect, it } from "vitest";
import { ACQUISITION_ACCESS_KIND, ENTITY_KIND } from "$lib/api/generated/codes";
import { canImportPublication, publicationFormatLabel } from "./catalog-labels";

describe("catalog media imports", () => {
  it.each([["image/jpeg", "JPEG"], ["image/png", "PNG"], ["image/webp", "WebP"]])("offers supported full %s images", (mediaType, label) => {
    const offer = { id: "original", label: "Original", access: ACQUISITION_ACCESS_KIND.download, mediaType };
    expect(publicationFormatLabel(mediaType)).toBe(label);
    expect(canImportPublication(ENTITY_KIND.image, offer)).toBe(true);
    expect(canImportPublication(ENTITY_KIND.book, offer)).toBe(false);
    expect(canImportPublication(ENTITY_KIND.image, { ...offer, access: ACQUISITION_ACCESS_KIND.sample })).toBe(false);
  });
  it("keeps unsupported images and incompatible publication formats out of the importer", () => {
    const offer = { id: "original", label: "Original", access: ACQUISITION_ACCESS_KIND.download, mediaType: "image/svg+xml" };
    expect(canImportPublication(ENTITY_KIND.image, offer)).toBe(false);
    expect(canImportPublication(ENTITY_KIND.comicInstallment, { ...offer, mediaType: "application/pdf" })).toBe(false);
    expect(canImportPublication(ENTITY_KIND.book, { ...offer, mediaType: "application/epub+zip" })).toBe(true);
    expect(canImportPublication(ENTITY_KIND.comicInstallment, { ...offer, mediaType: "application/vnd.comicbook+zip" })).toBe(true);
  });
});
