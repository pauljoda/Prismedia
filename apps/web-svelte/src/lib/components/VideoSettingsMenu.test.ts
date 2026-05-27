import { fireEvent, render, screen } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import { defaultSubtitleAppearance, type VideoSubtitleTrack } from "$lib/player/subtitle-types";
import type { ComponentProps } from "svelte";
import VideoSettingsMenu from "./VideoSettingsMenu.svelte";

type VideoSettingsMenuProps = ComponentProps<typeof VideoSettingsMenu>;

const subtitleTrack: VideoSubtitleTrack = {
  id: "sub-1",
  videoId: "video-1",
  language: "eng",
  label: "English",
  format: "vtt",
  source: "embedded",
  sourceFormat: "vtt",
  isDefault: true,
  url: "/subtitles/eng.vtt",
  sourceUrl: null,
  createdAt: "2026-01-01T00:00:00Z",
};

function baseProps(
  overrides: Partial<VideoSettingsMenuProps> = {},
): VideoSettingsMenuProps {
  const props: VideoSettingsMenuProps = {
    activeSubtitleLabel: "English",
    appearance: defaultSubtitleAppearance,
    displayedAudioTrackLabel: "Main",
    displayedAudioTracks: [
      {
        id: "audio-1",
        index: 0,
        streamIndex: 0,
        label: "Main",
        selected: true,
        source: "native" as const,
      },
    ],
    localAppearance: null,
    onAppearanceChange: vi.fn(),
    onAppearanceReset: vi.fn(),
    onClose: vi.fn(),
    onOpenView: vi.fn(),
    onPlaybackRateChange: vi.fn(),
    onQualityChange: vi.fn(),
    onSelectAudioTrack: vi.fn(),
    onSelectSubtitle: vi.fn(),
    onViewChange: vi.fn(),
    playbackRate: 1,
    qualityMode: "auto" as const,
    qualityOptions: [{ value: "auto" as const, label: "Auto" }],
    selectedQualityLabel: "Auto",
    subtitleTracks: [subtitleTrack],
    view: "root" as const,
  };

  return {
    ...props,
    ...overrides,
  };
}

describe("VideoSettingsMenu", () => {
  it("opens settings sections from the root menu", async () => {
    const onOpenView = vi.fn();
    render(VideoSettingsMenu, {
      props: baseProps({ onOpenView }),
    });

    await fireEvent.click(screen.getByRole("button", { name: /Quality/i }));
    await fireEvent.click(screen.getByRole("button", { name: /Captions/i }));

    expect(onOpenView).toHaveBeenNthCalledWith(1, "quality");
    expect(onOpenView).toHaveBeenNthCalledWith(2, "captions");
  });

  it("selects a caption track", async () => {
    const onSelectSubtitle = vi.fn();
    render(VideoSettingsMenu, {
      props: baseProps({
        activeSubtitleId: null,
        onSelectSubtitle,
        view: "captions",
      }),
    });

    await fireEvent.click(screen.getByRole("button", { name: /English - English embedded/i }));

    expect(onSelectSubtitle).toHaveBeenCalledWith("sub-1");
  });
});
