import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

function source() {
  return readFileSync(resolve(process.cwd(), "src/routes/audio/[id]/+page.svelte"), "utf8");
}

describe("audio library page", () => {
  it("keeps playback actions in the hero and sub-libraries above tracks", () => {
    const pageSource = source();

    expect(pageSource).toContain("Play All");
    expect(pageSource).toContain("Shuffle");
    expect(pageSource.indexOf("Sub-Libraries")).toBeLessThan(pageSource.lastIndexOf("<AudioTrackList"));
  });

  it("drives the shared global playback store instead of mounting a per-page player", () => {
    const pageSource = source();

    // The player is mounted once at the root layout; the page hands its tracks to the store.
    expect(pageSource).not.toContain("<AudioVidStackPlayer");
    expect(pageSource).toContain("useAudioPlayback");
    expect(pageSource).toContain("playback.play(");
  });
});
