import { readFile } from "node:fs/promises";
import { describe, expect, it } from "vitest";

describe("audio track playlist completion wiring", () => {
  it("reports audio track completion to the shell playlist", async () => {
    const source = await readFile("src/routes/audio/tracks/[id]/+page.svelte", "utf8");

    expect(source).toContain("playlist.isPlaylistItem(\"audio-track\", track.id)");
    expect(source).toContain("onPlaybackComplete");
    expect(source).toContain("playlist.reportContentEnded(\"audio-track\", track.id)");
  });
});
