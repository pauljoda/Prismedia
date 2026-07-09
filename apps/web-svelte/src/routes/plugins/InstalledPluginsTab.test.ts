import { fireEvent, render, screen, within } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import type { PluginProvider } from "$lib/api/generated/model";
import InstalledPluginsTab from "./InstalledPluginsTab.svelte";

type InstalledPluginsTabProps = ComponentProps<typeof InstalledPluginsTab>;

const provider: PluginProvider = {
  id: "tmdb",
  name: "TMDB",
  version: "1.0.0",
  installed: true,
  enabled: true,
  isNsfw: false,
  supports: [{ entityKind: "video", actions: ["search"] }],
  auth: [],
  missingAuthKeys: [],
};

const secondProvider: PluginProvider = {
  ...provider,
  id: "openlibrary",
  name: "Open Library",
  supports: [{ entityKind: "book", actions: ["search", "lookup-id"] }],
};

function baseProps(overrides: Partial<InstalledPluginsTabProps> = {}): InstalledPluginsTabProps {
  const props: InstalledPluginsTabProps = {
    authExpandedFor: null,
    authSavingFor: null,
    authValues: {},
    isSfw: false,
    onAuthCancel: vi.fn(),
    onProviderAuthToggle: vi.fn(),
    onProviderInstall: vi.fn(),
    onProviderRemove: vi.fn(),
    onProviderSaveAuth: vi.fn(),
    onProviderUpdate: vi.fn(),
    providerInstallingId: null,
    providerRemovingId: null,
    providerUpdatingId: null,
    providers: [provider, secondProvider],
  };

  return {
    ...props,
    ...overrides,
  };
}

describe("InstalledPluginsTab", () => {
  it("filters current plugin providers locally", async () => {
    render(InstalledPluginsTab, {
      props: baseProps(),
    });

    await fireEvent.input(screen.getByPlaceholderText("Search installed..."), {
      target: { value: "open" },
    });

    expect(screen.queryByText("TMDB")).not.toBeInTheDocument();
    expect(screen.getByText("Open Library")).toBeInTheDocument();
  });

  it("emits update actions from the current provider contract", async () => {
    const onProviderUpdate = vi.fn();
    const updateable: PluginProvider = {
      ...provider,
      updateAvailable: true,
      availableVersion: "1.1.0",
    };
    render(InstalledPluginsTab, {
      props: baseProps({
        providers: [updateable],
        onProviderUpdate,
      }),
    });

    const pluginCard = screen.getByText("TMDB").closest(".surface-card");
    expect(pluginCard).not.toBeNull();
    expect(within(pluginCard as HTMLElement).getByText("v1.1.0 available")).toBeInTheDocument();

    await fireEvent.click(within(pluginCard as HTMLElement).getByRole("button", { name: /update/i }));

    expect(onProviderUpdate).toHaveBeenCalledWith(updateable);
    expect(screen.queryByRole("button", { name: /check for updates/i })).not.toBeInTheDocument();
  });

  it("shows an empty state when there are no installed providers", () => {
    render(InstalledPluginsTab, {
      props: baseProps({ providers: [] }),
    });

    expect(screen.getByText("No plugins installed. Browse the community tabs to get started.")).toBeInTheDocument();
  });
});
