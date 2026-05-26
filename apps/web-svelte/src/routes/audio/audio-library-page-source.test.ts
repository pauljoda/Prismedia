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
    expect(pageSource).toContain("{shufflePlayKey}");
    expect(pageSource.indexOf("Sub-Libraries")).toBeLessThan(pageSource.lastIndexOf("<AudioTrackList"));
  });
});
