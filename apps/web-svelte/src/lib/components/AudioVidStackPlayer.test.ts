import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";

function readLocalSource(path: string) {
  return readFileSync(new URL(path, import.meta.url), "utf8");
}

describe("AudioVidStackPlayer playback continuity", () => {
  const source = readLocalSource("./AudioVidStackPlayer.svelte");

  it("only defers hidden-tab autoplay for restored sessions, not already-started queues", () => {
    expect(source).toContain("let audioStartedInThisSession = false;");
    expect(source).toContain("audioStartedInThisSession = true;");
    expect(source).toContain("const deferWhenHidden = !audioStartedInThisSession;");
    expect(source).toContain("requestPlay(track.id, { deferWhenHidden, stealActiveTab: false });");
    expect(source).toContain("requestPlay(track.id, { stealActiveTab: true });");
    expect(source).toContain("document.visibilityState === \"visible\"");
    expect(source).toContain("!options?.deferWhenHidden");
    expect(source).toContain("audioStartedInThisSession");
  });

  it("records a skipped playback event before local next advances away from the current track", () => {
    expect(source).toContain("import { recordEntityPlaybackEvent } from \"$lib/api/playback\";");
    expect(source).toContain("PLAYBACK_EVENT_KIND.skipped");
    expect(source).toContain("function recordCurrentTrackSkip");
    expect(source).toContain("const skippedTrack = activeTrack;");
    expect(source).toContain("if (playback.next()) {\n      recordCurrentTrackSkip(skippedTrack);");
  });

  it("routes queue jumps through the same skip-aware player path", () => {
    expect(source).toContain("function jumpToQueuedTrack");
    expect(source).toContain("recordCurrentTrackSkip(skippedTrack);");
    expect(source).toContain("<PlaybackQueueFlyout onClose={() => (queueOpen = false)} onJumpTo={jumpToQueuedTrack} />");
  });

  it("keeps natural track end on the completed-play path instead of recording a skip", () => {
    const endedStart = source.indexOf("function handleTrackEnd()");
    const endedEnd = source.indexOf("function handleVolumeInput", endedStart);
    const endedSource = source.slice(endedStart, endedEnd);

    expect(source).toContain("if (playback.currentTrack) recordTrackPlay(playback.currentTrack.id);");
    expect(endedSource).not.toContain("recordCurrentTrackSkip");
  });
});
