import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";

describe("AudioWaveformFilmstrip", () => {
  it("renders the waveform as a scrolling strip under a fixed playhead", async () => {
    const source = await readFile(
      resolve(process.cwd(), "src/lib/components/AudioWaveformFilmstrip.svelte"),
      "utf8",
    );

    expect(source).toContain("bind:this={trackEl}");
    expect(source).toContain("const trackWidth = $derived");
    expect(source).toContain("containerEl.clientWidth / 2 - trackPosition");
    expect(source).toContain("width: ${trackWidth}px");
    expect(source).toContain("left-1/2 z-20 -translate-x-1/2");
  });
});
