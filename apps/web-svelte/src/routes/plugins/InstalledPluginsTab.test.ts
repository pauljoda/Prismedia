import { fireEvent, render, screen, within } from "@testing-library/svelte";
import type { ComponentProps } from "svelte";
import { describe, expect, it, vi } from "vitest";
import type {
  InstalledPlugin,
  ScraperPackage,
} from "$lib/api/plugins";
import type { PluginProviderSummary } from "./plugin-page-types";
import InstalledPluginsTab from "./InstalledPluginsTab.svelte";

type InstalledPluginsTabProps = ComponentProps<typeof InstalledPluginsTab>;

const provider: PluginProviderSummary = {
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

const installedPlugin: InstalledPlugin = {
  id: "plugin-imdb",
  pluginId: "imdb",
  name: "IMDb",
  version: "1.0.0",
  runtime: "dotnet-process",
  installPath: "/plugins/imdb",
  sha256: null,
  isNsfw: false,
  capabilities: { sceneByName: true },
  enabled: true,
  sourceIndex: null,
  authStatus: null,
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
};

const scraper: ScraperPackage = {
  id: "stashdb-package",
  packageId: "stashdb",
  name: "StashDB",
  version: "1.0.0",
  installPath: "/scrapers/stashdb",
  sha256: null,
  capabilities: { performerByName: true },
  enabled: true,
  isNsfw: true,
  pluginType: "stash",
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
};

function baseProps(overrides: Partial<InstalledPluginsTabProps> = {}): InstalledPluginsTabProps {
  const props: InstalledPluginsTabProps = {
    authExpandedFor: null,
    authSavingFor: null,
    authValues: {},
    checkingUpdates: false,
    installedPlugins: [installedPlugin],
    isSfw: false,
    onAuthCancel: vi.fn(),
    onCheckUpdates: vi.fn(),
    onInstalledPluginAuthToggle: vi.fn(),
    onInstalledPluginRemove: vi.fn(),
    onInstalledPluginSaveAuth: vi.fn(),
    onInstalledPluginToggle: vi.fn(),
    onInstalledPluginUpdate: vi.fn(),
    onProviderAuthToggle: vi.fn(),
    onProviderInstall: vi.fn(),
    onProviderRemove: vi.fn(),
    onProviderSaveAuth: vi.fn(),
    onProviderUpdate: vi.fn(),
    onScraperRemove: vi.fn(),
    onScraperToggle: vi.fn(),
    pluginUpdates: {},
    providerInstallingId: null,
    providerRemovingId: null,
    providerUpdatingId: null,
    providers: [provider],
    scrapers: [scraper],
    updatingPluginId: null,
  };

  return {
    ...props,
    ...overrides,
  };
}

describe("InstalledPluginsTab", () => {
  it("filters installed plugins locally", async () => {
    render(InstalledPluginsTab, {
      props: baseProps(),
    });

    await fireEvent.input(screen.getByPlaceholderText("Search installed..."), {
      target: { value: "stash" },
    });

    expect(screen.queryByText("TMDB")).not.toBeInTheDocument();
    expect(screen.getByText("IMDb")).toBeInTheDocument();
    expect(screen.getByText("StashDB")).toBeInTheDocument();
  });

  it("emits installed plugin actions", async () => {
    const onCheckUpdates = vi.fn();
    const onInstalledPluginToggle = vi.fn();
    render(InstalledPluginsTab, {
      props: baseProps({
        onCheckUpdates,
        onInstalledPluginToggle,
      }),
    });

    await fireEvent.click(screen.getByRole("button", { name: /check for updates/i }));
    const pluginCard = screen.getByText("IMDb").closest(".surface-card");
    expect(pluginCard).not.toBeNull();
    await fireEvent.click(within(pluginCard as HTMLElement).getByRole("button", { name: /disable/i }));

    expect(onCheckUpdates).toHaveBeenCalled();
    expect(onInstalledPluginToggle).toHaveBeenCalledWith(installedPlugin);
  });

  it("emits provider update actions for installed Prismedia providers", async () => {
    const onProviderUpdate = vi.fn();
    const updateable = {
      ...provider,
      updateAvailable: true,
      availableVersion: "1.1.0",
    };
    render(InstalledPluginsTab, {
      props: baseProps({
        installedPlugins: [],
        providers: [updateable],
        scrapers: [],
        onProviderUpdate,
      }),
    });

    const pluginCard = screen.getByText("TMDB").closest(".surface-card");
    expect(pluginCard).not.toBeNull();
    expect(within(pluginCard as HTMLElement).getByText("v1.1.0 available")).toBeInTheDocument();

    await fireEvent.click(within(pluginCard as HTMLElement).getByRole("button", { name: /update/i }));

    expect(onProviderUpdate).toHaveBeenCalledWith(updateable);
  });

  it("shows an empty state when there are no installed plugins", () => {
    render(InstalledPluginsTab, {
      props: baseProps({
        installedPlugins: [],
        providers: [],
        scrapers: [],
      }),
    });

    expect(screen.getByText("No plugins installed. Browse the community tabs to get started.")).toBeInTheDocument();
  });
});
