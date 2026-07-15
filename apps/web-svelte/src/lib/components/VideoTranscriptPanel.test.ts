import { render, screen, waitFor } from "@testing-library/svelte";
import { describe, expect, it, vi } from "vitest";
import VideoTranscriptPanel from "./VideoTranscriptPanel.svelte";
import type { VideoSubtitleTrack } from "$lib/player/subtitle-types";

vi.mock("$lib/player/video-subtitles", () => ({
  fetchVideoSubtitleCues: vi.fn().mockResolvedValue({
    cues: [
      { start: 0, end: 1, text: "First line" },
      { start: 1, end: 2, text: "Previous line" },
      { start: 2, end: 3, text: "Current line" },
      { start: 3, end: 4, text: "Next one" },
      { start: 4, end: 5, text: "Next two" },
      { start: 5, end: 6, text: "Next three" },
      { start: 6, end: 7, text: "Hidden later line" },
    ],
  }),
}));

vi.mock("$lib/stores/session.svelte", () => ({
  useSession: () => ({ isAdmin: true }),
}));

const track: VideoSubtitleTrack = {
  id: "track-1",
  videoId: "video-1",
  language: "en",
  label: "SDH",
  format: "vtt",
  source: "embedded",
  sourceFormat: "vtt",
  isDefault: true,
  url: "/api/videos/video-1/subtitles/track-1",
  sourceUrl: null,
  createdAt: "2026-05-10T00:00:00.000Z",
};

describe("VideoTranscriptPanel", () => {
  it("renders a compact mobile dock as a scrollable full transcript window", async () => {
    render(VideoTranscriptPanel, {
      props: {
        videoId: "video-1",
        tracks: [track],
        activeTrackId: "track-1",
        onActiveTrackIdChange: vi.fn(),
        currentTime: 2.5,
        onSeek: vi.fn(),
        onTracksChanged: vi.fn(),
        variant: "compact",
        isDocked: true,
        onDockToggle: vi.fn(),
      },
    });

    await waitFor(() => {
      expect(screen.getByText("Current line")).toBeInTheDocument();
    });

    expect(screen.getByText("First line")).toBeInTheDocument();
    expect(screen.getByText("Previous line")).toBeInTheDocument();
    expect(screen.getByText("Next one")).toBeInTheDocument();
    expect(screen.getByText("Next two")).toBeInTheDocument();
    expect(screen.getByText("Next three")).toBeInTheDocument();
    expect(screen.getByText("Hidden later line")).toBeInTheDocument();
  });
});
