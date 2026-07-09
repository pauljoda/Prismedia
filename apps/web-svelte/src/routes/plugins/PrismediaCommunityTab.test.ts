import { fireEvent, render, screen } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import type { PluginProvider } from "$lib/api/generated/model";
import PrismediaCommunityTab from "./PrismediaCommunityTab.svelte";

type PrismediaCommunityTabProps = ComponentProps<typeof PrismediaCommunityTab>;

const provider: PluginProvider = {
  id: "tmdb",
  name: "TMDB",
  version: "1.0.0",
  installed: true,
  enabled: true,
  isNsfw: false,
  supports: [{ entityKind: "video", actions: ["search"] }],
  auth: [{ key: "api_key", label: "API Key", required: true, url: null }],
  missingAuthKeys: ["api_key"],
};

function baseProps(overrides: Partial<PrismediaCommunityTabProps> = {}): PrismediaCommunityTabProps {
  return {
    authSavingFor: null,
    installingId: null,
    loaded: true,
    loading: false,
    onInstall: vi.fn(),
    onRefresh: vi.fn(),
    onSaveAuth: vi.fn(),
    plugins: [provider],
    ...overrides,
  };
}

describe("PrismediaCommunityTab", () => {
  it("submits configured credentials through the provider callback", async () => {
    const onSaveAuth = vi.fn();
    render(PrismediaCommunityTab, {
      props: baseProps({ onSaveAuth }),
    });

    await fireEvent.click(screen.getByRole("button", { name: "Configure" }));
    await fireEvent.input(screen.getByLabelText(/api key/i), {
      target: { value: "secret" },
    });
    await fireEvent.click(screen.getByRole("button", { name: /save credentials/i }));

    expect(onSaveAuth).toHaveBeenCalledWith(provider, { api_key: "secret" });
    expect(screen.queryByRole("button", { name: /save credentials/i })).not.toBeInTheDocument();
  });

  it("filters providers with the community search", async () => {
    render(PrismediaCommunityTab, {
      props: baseProps(),
    });

    await fireEvent.input(screen.getByPlaceholderText("Filter by name or ID..."), {
      target: { value: "missing" },
    });

    expect(screen.getByText("No plugins match your search.")).toBeInTheDocument();
  });
});
