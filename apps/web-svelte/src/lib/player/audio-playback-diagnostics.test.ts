import { describe, expect, it, vi } from "vitest";
import {
  AUDIO_PLAYBACK_DIAGNOSTIC_EVENT,
  AUDIO_PLAYBACK_PAUSE_SOURCE,
} from "$lib/api/generated/codes";
import {
  AudioPlaybackDiagnosticReporter,
  type AudioPlaybackDiagnosticSnapshot,
} from "./audio-playback-diagnostics";

function snapshot(
  overrides: Partial<AudioPlaybackDiagnosticSnapshot> = {},
): AudioPlaybackDiagnosticSnapshot {
  return {
    trackId: "11111111-1111-1111-1111-111111111111",
    positionSeconds: 42,
    durationSeconds: 180,
    bufferedAheadSeconds: 75,
    readyState: 4,
    networkState: 1,
    paused: false,
    ended: false,
    playIntent: true,
    documentVisible: true,
    documentHasFocus: true,
    mediaErrorCode: null,
    ...overrides,
  };
}

describe("audio playback diagnostic reporter", () => {
  it("attributes an unowned pause to the browser instead of inventing a user action", () => {
    const send = vi.fn();
    const reporter = new AudioPlaybackDiagnosticReporter(send, () => 1_000);

    reporter.report(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.pause, snapshot({ paused: true }));

    expect(send).toHaveBeenCalledWith(expect.objectContaining({
      event: AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.pause,
      pauseSource: AUDIO_PLAYBACK_PAUSE_SOURCE.browser,
    }));
  });

  it("consumes an explicit pause source once", () => {
    const send = vi.fn();
    const reporter = new AudioPlaybackDiagnosticReporter(send, () => 1_000);
    reporter.markPauseSource(AUDIO_PLAYBACK_PAUSE_SOURCE.userControl);

    reporter.report(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.pause, snapshot({ paused: true }));
    reporter.report(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.pause, snapshot({ paused: true }));

    expect(send).toHaveBeenNthCalledWith(1, expect.objectContaining({
      pauseSource: AUDIO_PLAYBACK_PAUSE_SOURCE.userControl,
    }));
    expect(send).toHaveBeenNthCalledWith(2, expect.objectContaining({
      pauseSource: AUDIO_PLAYBACK_PAUSE_SOURCE.browser,
    }));
  });

  it("measures recovery from the first waiting signal across a later stalled signal", () => {
    const send = vi.fn();
    let now = 2_000;
    const reporter = new AudioPlaybackDiagnosticReporter(send, () => now);

    reporter.report(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.waiting, snapshot());
    now = 2_400;
    reporter.report(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.stalled, snapshot());
    now = 3_750;
    reporter.report(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.playing, snapshot());

    expect(send).toHaveBeenLastCalledWith(expect.objectContaining({
      event: AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.playing,
      interruptionMilliseconds: 1_750,
      pauseSource: null,
    }));
  });

  it("keeps timing a recoverable media error while play intent remains active", () => {
    const send = vi.fn();
    let now = 1_000;
    const reporter = new AudioPlaybackDiagnosticReporter(send, () => now);

    reporter.report(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.error, snapshot({ playIntent: true }));
    now = 2_250;
    reporter.report(AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.playing, snapshot({ playIntent: true }));

    expect(send).toHaveBeenLastCalledWith(expect.objectContaining({
      event: AUDIO_PLAYBACK_DIAGNOSTIC_EVENT.playing,
      interruptionMilliseconds: 1_250,
    }));
  });
});
