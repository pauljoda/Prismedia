import { describe, expect, it } from "vitest";
import { parseWebVttCues } from "./video-subtitles";

describe("video-subtitles", () => {
  it("parses WebVTT subtitle assets into transcript cues", () => {
    expect(
      parseWebVttCues(`WEBVTT

intro
00:00.500 --> 00:02.000 align:center
<v Speaker>Hello there.</v>

00:03:04.250 --> 00:03:05.500
Second line
still second line
`),
    ).toEqual([
      { start: 0.5, end: 2, text: "Hello there." },
      { start: 184.25, end: 185.5, text: "Second line\nstill second line" },
    ]);
  });
});
