import { describe, expect, it } from "vitest";
import { REQUEST_MEDIA_KIND, REQUEST_PROVIDER_KIND } from "$lib/api/generated/codes";
import { inferRequestSourceForKind, numericValue, thumbnailAspectForKind } from "./request-helpers";

describe("request helpers", () => {
  it("infers the plugin source for book and author detail routes", () => {
    expect(inferRequestSourceForKind(REQUEST_MEDIA_KIND.book)).toBe(REQUEST_PROVIDER_KIND.plugin);
    expect(inferRequestSourceForKind(REQUEST_MEDIA_KIND.author)).toBe(REQUEST_PROVIDER_KIND.plugin);
  });

  it("uses a 2:3 poster aspect for books and authors", () => {
    expect(thumbnailAspectForKind(REQUEST_MEDIA_KIND.book)).toBe("2 / 3");
    expect(thumbnailAspectForKind(REQUEST_MEDIA_KIND.author)).toBe("2 / 3");
  });

  it("coerces numeric values, returning null for blanks and non-numbers", () => {
    expect(numericValue(7)).toBe(7);
    expect(numericValue("12")).toBe(12);
    expect(numericValue("")).toBeNull();
    expect(numericValue(null)).toBeNull();
    expect(numericValue("abc")).toBeNull();
  });
});
