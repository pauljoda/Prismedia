import { describe, expect, it } from "vitest";
import { pickPreferredSubtitleTrack } from "./subtitle-appearance";

describe("subtitle appearance helpers", () => {
  it("matches preferred subtitle languages against human labels", () => {
    const picked = pickPreferredSubtitleTrack(
      [
        { id: "track-ja", language: "jpn", label: "Japanese" },
        { id: "track-en", language: "und", label: "English" },
      ],
      "English,en,eng",
    );

    expect(picked).toBe("track-en");
  });
});
