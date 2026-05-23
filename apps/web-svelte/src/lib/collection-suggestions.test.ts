import { describe, expect, it } from "vitest";
import { buildTagSuggestions } from "./collection-suggestions";

describe("buildTagSuggestions", () => {
  it("counts every collection-rule tag target type", () => {
    const suggestions = buildTagSuggestions([
      {
        id: "tag-1",
        name: "Testing",
        videoCount: 1,
        galleryCount: 2,
        imageCount: 3,
        audioTrackCount: 4,
        imagePath: null,
        favorite: false,
        rating: null,
        isNsfw: false,
      },
    ]);

    expect(suggestions).toEqual([{ name: "Testing", count: 10 }]);
  });
});
