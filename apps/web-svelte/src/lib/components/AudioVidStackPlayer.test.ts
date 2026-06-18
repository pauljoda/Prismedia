import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

function readLocalSource(path: string) {
  return readFileSync(new URL(path, import.meta.url), "utf8");
}

describe("AudioVidStackPlayer playback continuity", () => {
  it("only defers hidden-tab autoplay for restored sessions, not already-started queues", () => {
    const source = readLocalSource("./AudioVidStackPlayer.svelte");

    expect(source).toContain("let audioStartedInThisSession = false;");
    expect(source).toContain("audioStartedInThisSession = true;");
    expect(source).toContain("const deferWhenHidden = !audioStartedInThisSession;");
    expect(source).toContain("requestPlay(track.id, { deferWhenHidden, stealActiveTab: false });");
    expect(source).toContain("requestPlay(track.id, { stealActiveTab: true });");
    expect(source).toContain("document.visibilityState === \"visible\"");
    expect(source).toContain("!options?.deferWhenHidden");
    expect(source).toContain("audioStartedInThisSession");
  });
});
