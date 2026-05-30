import { fireEvent, render, screen } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import StashCommunityIndexTab, { type StashScraperRow } from "./StashCommunityIndexTab.svelte";

type StashCommunityIndexTabProps = ComponentProps<typeof StashCommunityIndexTab>;

const entries: StashScraperRow[] = [
  {
    providerId: "stash-stashdb-video",
    name: "StashDB Video",
    version: "1.0.0",
    installed: false,
  },
  {
    providerId: "stash-fansdb-gallery",
    name: "FansDB Gallery",
    version: "1.0.0",
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
    expect(onInstall).toHaveBeenCalledWith("stash-stashdb-video");
  });
});
