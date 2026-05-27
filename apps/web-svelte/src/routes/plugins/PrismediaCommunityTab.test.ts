import { fireEvent, render, screen } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import type { PluginProviderSummary } from "./plugin-page-types";
import PrismediaCommunityTab from "./PrismediaCommunityTab.svelte";

type PrismediaCommunityTabProps = ComponentProps<typeof PrismediaCommunityTab>;

const plugins: PluginProviderSummary[] = [
  {
    id: "imdb",
    name: "IMDb",
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{ entityKind: "video", actions: ["search"] }],
    auth: [{ key: "api_key", label: "API Key", required: true }],
    missingAuthKeys: ["api_key"],
  },
  {
    id: "tmdb",
    name: "TMDB",
    version: "1.0.0",
    installed: false,
    enabled: false,
    isNsfw: false,
    supports: [{ entityKind: "video-series", actions: ["search"] }],
    auth: [],
    missingAuthKeys: [],
  },
];

function baseProps(overrides: Partial<PrismediaCommunityTabProps> = {}): PrismediaCommunityTabProps {
  const props: PrismediaCommunityTabProps = {
    authSavingFor: null,
    installingId: null,
    loaded: true,
    loading: false,
    onInstall: vi.fn(),
    onRefresh: vi.fn(),
    onSaveAuth: vi.fn(),
    plugins,
  };

  return {
    ...props,
    ...overrides,
  };
}

describe("PrismediaCommunityTab", () => {
  it("filters community plugins locally", async () => {
    render(PrismediaCommunityTab, {
      props: baseProps(),
    });

    await fireEvent.input(screen.getByPlaceholderText("Filter by name or ID..."), {
      target: { value: "tmdb" },
    });

    expect(screen.queryByText("IMDb")).not.toBeInTheDocument();
    expect(screen.getByText("TMDB")).toBeInTheDocument();
  });

  it("collects auth values before saving", async () => {
    const onSaveAuth = vi.fn();
    render(PrismediaCommunityTab, {
      props: baseProps({ onSaveAuth }),
    });

    await fireEvent.click(screen.getByRole("button", { name: /configure/i }));
    await fireEvent.input(screen.getByLabelText(/api key/i), {
      target: { value: "secret" },
    });
    await fireEvent.click(screen.getByRole("button", { name: /save credentials/i }));

    expect(onSaveAuth).toHaveBeenCalledWith(plugins[0], { api_key: "secret" });
  });
});
