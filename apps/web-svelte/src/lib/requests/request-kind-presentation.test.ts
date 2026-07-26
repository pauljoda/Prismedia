import { describe, expect, it } from "vitest";
import { REQUEST_MEDIA_KIND } from "$lib/api/generated/codes";
import { requestKindAccent } from "./request-kind-presentation";

describe("requestKindAccent", () => {
  it("uses the shared entity-family spectrum for request choices", () => {
    expect(requestKindAccent(REQUEST_MEDIA_KIND.book)).toBe("#3b869c");
    expect(requestKindAccent(REQUEST_MEDIA_KIND.audiobook)).toBe("#775ca5");
    expect(requestKindAccent(REQUEST_MEDIA_KIND.author)).toBe("#3b869c");
    expect(requestKindAccent(REQUEST_MEDIA_KIND.movie)).toBe("#b76337");
    expect(requestKindAccent(REQUEST_MEDIA_KIND.series)).toBe("#9e873b");
    expect(requestKindAccent(REQUEST_MEDIA_KIND.artist)).toBe("#775ca5");
    expect(requestKindAccent(REQUEST_MEDIA_KIND.album)).toBe("#775ca5");
  });
});
