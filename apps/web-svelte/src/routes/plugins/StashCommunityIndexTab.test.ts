import { fireEvent, render, screen } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import type { CommunityIndexEntry } from "$lib/api/plugins";
import StashCommunityIndexTab from "./StashCommunityIndexTab.svelte";

type StashCommunityIndexTabProps = ComponentProps<typeof StashCommunityIndexTab>;

const entries: CommunityIndexEntry[] = [
  {
    id: "stashdb-video",
    name: "StashDB Video",
    version: "1.0.0",
    date: "2026-01-01",
    path: "stashdb-video.zip",
    sha256: "abc",
    requires: ["ffmpeg"],
    installed: false,
  },
  {
    id: "fansdb-gallery",
    name: "FansDB Gallery",
    version: "1.0.0",
    date: "2026-01-02",
    path: "fansdb-gallery.zip",
    sha256: "def",
    installed: true,
  },
];

function baseProps(overrides: Partial<StashCommunityIndexTabProps> = {}): StashCommunityIndexTabProps {
  const props: StashCommunityIndexTabProps = {
    entries,
    installingId: null,
    loaded: true,
    loading: false,
    onInstall: vi.fn(),
    onRefresh: vi.fn(),
  };

  return {
    ...props,
    ...overrides,
  };
}

describe("StashCommunityIndexTab", () => {
  it("filters community scrapers locally", async () => {
    render(StashCommunityIndexTab, {
      props: baseProps(),
    });

    await fireEvent.input(screen.getByPlaceholderText("Filter by name or ID..."), {
      target: { value: "gallery" },
    });

    expect(screen.queryByText("StashDB Video")).not.toBeInTheDocument();
    expect(screen.getByText("FansDB Gallery")).toBeInTheDocument();
  });

  it("emits refresh and install actions", async () => {
    const onInstall = vi.fn();
    const onRefresh = vi.fn();
    render(StashCommunityIndexTab, {
      props: baseProps({ onInstall, onRefresh }),
    });

    await fireEvent.click(screen.getByRole("button", { name: /refresh/i }));
    await fireEvent.click(screen.getByRole("button", { name: /install/i }));

    expect(onRefresh).toHaveBeenCalled();
    expect(onInstall).toHaveBeenCalledWith("stashdb-video");
  });
});
