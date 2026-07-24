import { describe, expect, it } from "vitest";
import { pickPreferredSubtitleTrack } from "./subtitle-appearance";

describe("subtitle appearance helpers", () => {
  it("adds every case-insensitive matching term so English Forced outranks plain English", () => {
    const picked = pickPreferredSubtitleTrack(
      [
        { id: "track-en", language: "eng", label: "English" },
        { id: "track-en-forced", language: "ENG", label: "English Forced" },
        { id: "track-ja", language: "jpn", label: "Japanese Forced" },
      ],
      [
        { term: "forced", weight: 80 },
        { term: "English", weight: 55 },
        { term: "Eng", weight: 35 },
      ],
    );

    expect(picked).toBe("track-en-forced");
  });

  it("uses ISO aliases and list order only to break equal scores", () => {
    const picked = pickPreferredSubtitleTrack(
      [
        { id: "track-ja", language: "jpn", label: "Japanese" },
        { id: "track-en", language: "und", label: "English" },
      ],
      [{ term: "en", weight: 50 }],
    );

    expect(picked).toBe("track-en");
  });
});
