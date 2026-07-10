import { describe, expect, it } from "vitest";
import {
  ENTITY_KIND,
  REQUEST_KIND_MANIFEST,
  REQUEST_MEDIA_KIND,
} from "$lib/api/generated/codes";
import {
  entityKindForRequest,
  numericValue,
  REQUEST_KINDS,
  requestKindForEntityKind,
} from "./request-helpers";

describe("request helpers", () => {
  it("uses the generated backend request-kind manifest without a parallel table", () => {
    expect(REQUEST_KINDS).toBe(REQUEST_KIND_MANIFEST);
  });

  it("maps every request kind to the library entity kind it becomes (virtual entities)", () => {
    expect(entityKindForRequest(REQUEST_MEDIA_KIND.book)).toBe(ENTITY_KIND.book);
    expect(entityKindForRequest(REQUEST_MEDIA_KIND.author)).toBe(ENTITY_KIND.bookAuthor);
    expect(entityKindForRequest(REQUEST_MEDIA_KIND.movie)).toBe(ENTITY_KIND.movie);
    expect(entityKindForRequest(REQUEST_MEDIA_KIND.series)).toBe(ENTITY_KIND.videoSeries);
    expect(entityKindForRequest(REQUEST_MEDIA_KIND.artist)).toBe(ENTITY_KIND.musicArtist);
    expect(entityKindForRequest(REQUEST_MEDIA_KIND.album)).toBe(ENTITY_KIND.audioLibrary);
    // Unknown kinds fall back to a book poster rather than a wide video card.
    expect(entityKindForRequest("mystery")).toBe(ENTITY_KIND.book);
  });

  it("maps entity kinds back to request kinds for the review queue", () => {
    expect(requestKindForEntityKind(ENTITY_KIND.audioLibrary)).toBe(REQUEST_MEDIA_KIND.album);
    expect(requestKindForEntityKind(ENTITY_KIND.movie)).toBe(REQUEST_MEDIA_KIND.movie);
    expect(requestKindForEntityKind(ENTITY_KIND.videoSeason)).toBe(REQUEST_MEDIA_KIND.season);
    expect(requestKindForEntityKind(ENTITY_KIND.video)).toBe(REQUEST_MEDIA_KIND.episode);
    expect(requestKindForEntityKind(ENTITY_KIND.gallery)).toBeNull();
  });

  it("coerces numeric values, returning null for blanks and non-numbers", () => {
    expect(numericValue(7)).toBe(7);
    expect(numericValue("12")).toBe(12);
    expect(numericValue("")).toBeNull();
    expect(numericValue(null)).toBeNull();
    expect(numericValue("abc")).toBeNull();
  });
});
