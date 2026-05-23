import { describe, expect, it } from "vitest";
import type { LibrarySettings } from "$lib/api/prismedia";
import {
  buildSubtitleDefaults,
  clampTranscriptDockPercent,
  formatVideoTimestamp,
  readTranscriptDockPreferences,
  writeTranscriptDockPreference,
  writeTranscriptDockWidth,
} from "./video-page-state";

describe("video page state helpers", () => {
  it("formats playback timestamps for metadata panels", () => {
    expect(formatVideoTimestamp(9.8)).toBe("0:09");
    expect(formatVideoTimestamp(70)).toBe("1:10");
    expect(formatVideoTimestamp(3671)).toBe("1:01:11");
  });

  it("builds subtitle defaults from generated library settings", () => {
    expect(buildSubtitleDefaults(null)).toBeUndefined();
    const settings = {
      subtitlesAutoEnable: true,
      subtitlesPreferredLanguages: "ja,en",
      subtitleStyle: "native",
      subtitleFontScale: 1.25,
      subtitlePositionPercent: 72,
      subtitleOpacity: 0.85,
    } as LibrarySettings;

    expect(buildSubtitleDefaults(settings)).toEqual({
      autoEnable: true,
      preferredLanguages: "ja,en",
      appearance: {
        style: "native",
        fontScale: 1.25,
        positionPercent: 72,
        opacity: 0.85,
      },
    });
  });

  it("reads and writes transcript dock preferences with clamped widths", () => {
    const values = new Map<string, string>();
    const storage = {
      getItem: (key: string) => values.get(key) ?? null,
      setItem: (key: string, value: string) => values.set(key, value),
    };

    expect(readTranscriptDockPreferences(storage)).toEqual({ docked: false, videoPercent: 80 });
    writeTranscriptDockPreference(storage, true);
    writeTranscriptDockWidth(storage, 101);

    expect(readTranscriptDockPreferences(storage)).toEqual({ docked: true, videoPercent: 92 });
    expect(clampTranscriptDockPercent(22)).toBe(40);
  });
});
