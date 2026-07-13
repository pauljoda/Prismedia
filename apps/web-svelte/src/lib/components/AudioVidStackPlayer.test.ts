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

  it("records a skipped playback event before local next advances away from a quick-abandoned track", () => {
    expect(source).toContain("recordEntityPlaybackEvent");
    expect(source).toContain("const QUICK_SKIP_THRESHOLD_SECONDS = 10;");
    expect(source).toContain("PLAYBACK_EVENT_KIND.skipped");
    expect(source).toContain("function recordCurrentTrackSkip");
    expect(source).toContain("function isQuickSkipCandidate");
    expect(source).toContain("positionSeconds <= QUICK_SKIP_THRESHOLD_SECONDS");
    expect(source).toContain("elapsedSinceRequestSeconds <= QUICK_SKIP_THRESHOLD_SECONDS");
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

    expect(source).toContain("recordTrackPlay(playback.currentTrack.id);");
    expect(endedSource).not.toContain("recordCurrentTrackSkip");
  });

  it("persists audiobook progress on its Book owner across transport boundaries", () => {
    expect(source).toContain("function saveAudiobookProgress");
    expect(source).toContain("updateEntityPlayback(ownerId");
    expect(source).toContain("saveAudiobookProgress({ completed: false })");
    expect(source).toContain("saveAudiobookProgress({ completed: isFinalAudiobookPart() })");
    expect(source).toContain("const absoluteSeconds = options.completed\n      ? 0");
    expect(source).toContain("const AUDIOBOOK_PROGRESS_SAVE_INTERVAL_SECONDS = 5;");
    const saveStart = source.indexOf("function saveAudiobookProgress");
    const saveEnd = source.indexOf("// Switch audio source", saveStart);
    expect(source.slice(saveStart, saveEnd)).not.toContain("durationSeconds");
    expect(source.slice(saveStart, saveEnd)).toContain("audiobookProgressSave = audiobookProgressSave");
  });

  it("uses runtime media durations for unprobed audiobook parts and still persists completion", () => {
    expect(source).toContain("const audiobookRuntimeDurations = new SvelteMap<string, number>();");
    expect(source).toContain("audiobookRuntimeDurations.set(track.id, audio.duration);");
    expect(source).toContain("audiobookDuration(playback.queue, audiobookRuntimeDurations)");
    expect(source).toContain(
      "audiobookAbsoluteTime(playback.queue, track.id, playback.currentTime, audiobookRuntimeDurations)",
    );
    expect(source).toContain("if (totalSeconds <= 0 && !options.completed) return;");
  });

  it("keeps audiobook parts ordered and out of music skip counting", () => {
    expect(source).toContain("const isAudiobook = $derived");
    expect(source).toContain("if (isAudiobook) return;");
    expect(source).toContain("disabled={isAudiobook}");
  });
});
