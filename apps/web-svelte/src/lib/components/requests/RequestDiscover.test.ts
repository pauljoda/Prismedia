import { fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  CONNECTION_STATUS, PLUGIN_CAPABILITY, INTEGRATION_OPERATION,
  ENTITY_KIND,
  IDENTIFY_ACTION,
  PLUGIN_SEARCH_FIELD_TYPE,
  REQUEST_MEDIA_KIND,
  REQUEST_PROVIDER_KIND,
} from "$lib/api/generated/codes";
import type { ConnectionResponse, EntityKind, RequestSearchResult } from "$lib/api/generated/model";
import type { PluginProvider } from "$lib/api/identify-types";
import RequestDiscoverHarness from "./RequestDiscover.test-harness.svelte";

const fetchPluginProviders = vi.fn();
const fetchSettingsValues = vi.fn();
const searchRequestsByPlugin = vi.fn();
const goto = vi.fn();
const fetchConnectionCatalog = vi.fn();
vi.mock("$lib/api/connections", () => ({ fetchConnectionCatalog: (...args: unknown[]) => fetchConnectionCatalog(...args) }));

vi.mock("$lib/api/plugins", () => ({
  fetchPluginProviders: (...args: unknown[]) => fetchPluginProviders(...args),
}));

vi.mock("$lib/api/settings", () => ({
  fetchLibraryRoots: async () => [],
  fetchSettingsValues: (...args: unknown[]) => fetchSettingsValues(...args),
}));

vi.mock("$lib/api/requests", () => ({
  searchRequestsByPlugin: (...args: unknown[]) => searchRequestsByPlugin(...args),
}));

vi.mock("$app/navigation", () => ({
  goto: (...args: unknown[]) => goto(...args),
  invalidateAll: vi.fn(),
}));

describe("RequestDiscover", () => {
  beforeEach(() => {
    fetchPluginProviders.mockReset();
    fetchSettingsValues.mockReset();
    searchRequestsByPlugin.mockReset();
    goto.mockReset();
    fetchConnectionCatalog.mockReset();
    fetchConnectionCatalog.mockResolvedValue({ title: "Source books", items: [], nextCursor: null });
    fetchPluginProviders.mockResolvedValue([tmdb(), tvdb(), openLibrary()]);
    fetchSettingsValues.mockResolvedValue({ values: { "identify.defaultProviders": {} } });
    searchRequestsByPlugin.mockResolvedValue({ results: [], providerErrors: [] });
  });

  it("browses a connected source inside Request and returns to title discovery", async () => {
    const connection: ConnectionResponse = { id: "book-source", name: "Reading collection", pluginId: "fixture-opds", baseUrl: "http://books.test", enabled: true,
      enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery], effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.catalogDiscovery, operations: [INTEGRATION_OPERATION.browse], entityKinds: [ENTITY_KIND.book] }],
      settings: {}, configuredSecretKeys: [], revision: 1, status: CONNECTION_STATUS.ready, remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null };
    render(RequestDiscoverHarness, { connections: [connection] });
    await fireEvent.click(screen.getByRole("button", { name: "Reading collection Find and import media" }));
    await screen.findByText("Source books");
    expect(fetchConnectionCatalog).toHaveBeenCalledWith(connection.id, expect.objectContaining({ entityKind: ENTITY_KIND.book }));
    expect(screen.queryByText("What would you like to find?")).not.toBeInTheDocument();
    expect(searchRequestsByPlugin).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("button", { name: "Back to Request" }));
    expect(await screen.findByText("What would you like to find?")).toBeInTheDocument();
  });

  it("offers only sources that can browse the selected kind", async () => {
    const bookCatalog = connection("book-catalog", "Book catalog", ENTITY_KIND.book);
    const artistCatalog = connection("artist-catalog", "Artist catalog", ENTITY_KIND.musicArtist);
    const unrelatedArtistManager = connection("artist-manager", "Artist manager", ENTITY_KIND.book, {
      enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery, PLUGIN_CAPABILITY.externalManager],
      effectiveCapabilities: [
        { kind: PLUGIN_CAPABILITY.catalogDiscovery, operations: [INTEGRATION_OPERATION.browse], entityKinds: [ENTITY_KIND.book] },
        { kind: PLUGIN_CAPABILITY.externalManager, operations: [INTEGRATION_OPERATION.managerOptions], entityKinds: [ENTITY_KIND.musicArtist] },
      ],
    });
    render(RequestDiscoverHarness, { connections: [bookCatalog, artistCatalog, unrelatedArtistManager] });
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());

    await fireEvent.click(screen.getByRole("button", { name: "Artists" }));
    expect(await screen.findByRole("button", { name: "Source" })).toBeInTheDocument();
    expect(screen.queryByText("No compatible provider")).not.toBeInTheDocument();
    expect(screen.getByText("Browse a compatible source")).toBeInTheDocument();
    await fireEvent.keyDown(screen.getByRole("button", { name: "Source" }), { key: "ArrowDown" });

    const listbox = await screen.findByRole("listbox");
    expect(within(listbox).getByText("Artist catalog")).toBeInTheDocument();
    expect(within(listbox).queryByText("Book catalog")).not.toBeInTheDocument();
    expect(within(listbox).queryByText("Artist manager")).not.toBeInTheDocument();
  });

  it("does not render a directly linked source that cannot browse the selected kind", async () => {
    const bookCatalog = connection("book-catalog", "Book catalog", ENTITY_KIND.book);
    render(RequestDiscoverHarness, {
      connections: [bookCatalog],
      initialConnectionId: bookCatalog.id,
      initialKind: REQUEST_MEDIA_KIND.artist,
    });

    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());
    expect(fetchConnectionCatalog).not.toHaveBeenCalled();
    expect(screen.queryByText("Book catalog")).not.toBeInTheDocument();
    expect(await screen.findByText("No compatible provider")).toBeInTheDocument();
  });

  it("resets a same-path source view when the Request URL loses its kind and connection", async () => {
    const bookCatalog = connection("book-catalog", "Book catalog", ENTITY_KIND.book);
    const view = render(RequestDiscoverHarness, {
      connections: [bookCatalog], initialConnectionId: bookCatalog.id, initialKind: REQUEST_MEDIA_KIND.book,
    });
    await screen.findByText("Source books");

    await view.rerender({ connections: [bookCatalog], initialConnectionId: null, initialKind: null });

    expect(await screen.findByText("What would you like to find?")).toBeInTheDocument();
    expect(screen.queryByText("Source books")).not.toBeInTheDocument();
  });

  it("keeps the current search draft when its selected kind is clicked again", async () => {
    render(RequestDiscoverHarness);
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());
    await fireEvent.click(screen.getByRole("button", { name: "Series" }));
    const title = await screen.findByLabelText("Series title");
    await fireEvent.input(title, { target: { value: "A planned search" } });
    const selectedKind = screen.getByRole("radio", { name: "Series" });
    await fireEvent.click(selectedKind);
    expect(title).toHaveValue("A planned search");
    expect(selectedKind).toHaveAttribute("aria-checked", "true");
    expect(searchRequestsByPlugin).not.toHaveBeenCalled();
  });

  it("requires a kind, filters its providers, and swaps to the selected provider's schema", async () => {
    render(RequestDiscoverHarness);

    expect(screen.queryByRole("button", { name: "Source" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "All" })).not.toBeInTheDocument();
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());

    await fireEvent.click(screen.getByRole("button", { name: "Series" }));

    expect(await screen.findByLabelText("Series title")).toBeInTheDocument();
    expect(screen.queryByLabelText("Year")).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "More filters" }));
    expect(screen.getByLabelText("Year")).toBeInTheDocument();
    const providerTrigger = screen.getByRole("button", { name: "Source" });
    await fireEvent.keyDown(providerTrigger, { key: "ArrowDown" });

    const listbox = await screen.findByRole("listbox");
    expect(within(listbox).getByText("Beta TV Database")).toBeInTheDocument();
    expect(within(listbox).queryByText("Open Library")).not.toBeInTheDocument();
    await fireEvent.pointerUp(within(listbox).getByRole("option", { name: /beta tv database/i }));

    expect(await screen.findByLabelText("Show name")).toBeInTheDocument();
    expect(screen.getByLabelText("Episode title")).toBeInTheDocument();
    expect(screen.queryByLabelText("Series title")).not.toBeInTheDocument();
  });

  it("starts Request discovery with the configured provider for the selected EntityKind", async () => {
    fetchSettingsValues.mockResolvedValue({
      values: {
        "identify.defaultProviders": {
          [ENTITY_KIND.videoSeries]: "tv-database",
        },
      },
    });
    render(RequestDiscoverHarness);
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());

    await fireEvent.click(screen.getByRole("button", { name: "Series" }));

    expect(await screen.findByRole("button", { name: "Source" }))
      .toBeInTheDocument();
    expect(await screen.findByLabelText("Show name")).toBeInTheDocument();
  });

  it("submits exactly the selected plugin's trimmed schema fields with the NSFW boundary", async () => {
    render(RequestDiscoverHarness);
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());
    await fireEvent.click(screen.getByRole("button", { name: "Series" }));

    await fireEvent.input(await screen.findByLabelText("Series title"), {
      target: { value: "  Andor  " },
    });
    await fireEvent.click(screen.getByRole("button", { name: "More filters" }));
    await fireEvent.input(screen.getByLabelText("Year"), { target: { value: "2022" } });
    await fireEvent.click(screen.getByRole("button", { name: "Search" }));

    await waitFor(() => {
      expect(searchRequestsByPlugin).toHaveBeenCalledWith({
        kind: REQUEST_MEDIA_KIND.series,
        pluginId: "cinema-metadata",
        fields: { seriesTitle: "Andor", year: "2022" },
        limit: 25,
        hideNsfw: true,
      });
    });
  });

  it("keeps provider ranking, skips identity-less rows, and navigates with the candidate identity", async () => {
    searchRequestsByPlugin.mockResolvedValue({
      results: [
        result("Ranked first", "first", "cinema-metadata", "tmdb"),
        result("Ranked second", "Show:01/part?x", "cinema-metadata", "tmdb"),
        { ...result("Missing route", "missing", "cinema-metadata", "tmdb"), pluginId: null },
      ],
      providerErrors: [],
    });

    render(RequestDiscoverHarness, { back: "q=andor&kind=series" });
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());
    await fireEvent.click(screen.getByRole("button", { name: "Series" }));
    await fireEvent.input(await screen.findByLabelText("Series title"), { target: { value: "Andor" } });
    await fireEvent.click(screen.getByRole("button", { name: "Search" }));

    const candidateButtons = await screen.findAllByRole("button", { name: /^Ranked / });
    expect(candidateButtons.map((button) => button.getAttribute("aria-label"))).toEqual([
      "Ranked first",
      "Ranked second",
    ]);
    expect(screen.queryByText("Best")).not.toBeInTheDocument();
    expect(screen.queryByText("Missing route")).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Ranked second" }));

    expect(goto).toHaveBeenCalledWith(
      "/request/series/Show%3A01%2Fpart%3Fx?plugin=cinema-metadata&namespace=tmdb&back=q%3Dandor%26kind%3Dseries",
    );
  });

  it("preserves audiobook intent when a Book provider returns a Book-shaped result", async () => {
    searchRequestsByPlugin.mockResolvedValue({
      results: [result(
        "Project Hail Mary",
        "works/OL:Project:Hail:Mary",
        "openlibrary",
        "openlibrary",
        REQUEST_MEDIA_KIND.book,
      )],
      providerErrors: [],
    });

    render(RequestDiscoverHarness);
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());
    await fireEvent.click(screen.getByRole("button", { name: "Audiobooks" }));

    expect(await screen.findByRole("button", { name: "Source" })).toBeInTheDocument();
    await fireEvent.input(screen.getByLabelText("Book title"), {
      target: { value: "  Project Hail Mary  " },
    });
    await fireEvent.click(screen.getByRole("button", { name: "Search" }));

    await waitFor(() => {
      expect(searchRequestsByPlugin).toHaveBeenCalledWith({
        kind: REQUEST_MEDIA_KIND.audiobook,
        pluginId: "openlibrary",
        fields: { title: "Project Hail Mary" },
        limit: 25,
        hideNsfw: true,
      });
    });
    await fireEvent.click(await screen.findByRole("button", { name: "Project Hail Mary" }));

    expect(goto).toHaveBeenCalledWith(
      "/request/audiobook/works%2FOL%3AProject%3AHail%3AMary?plugin=openlibrary&namespace=openlibrary",
    );
  });

  it("shows a direct no-provider state for a selected kind", async () => {
    fetchPluginProviders.mockResolvedValue([tmdb()]);
    render(RequestDiscoverHarness);
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());

    await fireEvent.click(screen.getByRole("button", { name: "Books" }));

    expect(await screen.findByText("No compatible provider")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Source" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Search" })).not.toBeInTheDocument();
  });

  it("loads a larger ranked candidate window from providers that support it", async () => {
    searchRequestsByPlugin
      .mockResolvedValueOnce({
        results: Array.from({ length: 25 }, (_, index) =>
          result(`Match ${index + 1}`, `match-${index + 1}`, "cinema-metadata", "tmdb")),
        providerErrors: [],
      })
      .mockResolvedValueOnce({
        results: Array.from({ length: 50 }, (_, index) =>
          result(`Match ${index + 1}`, `match-${index + 1}`, "cinema-metadata", "tmdb")),
        providerErrors: [],
      });

    render(RequestDiscoverHarness);
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());
    await fireEvent.click(screen.getByRole("button", { name: "Series" }));
    await fireEvent.input(await screen.findByLabelText("Series title"), { target: { value: "Andor" } });
    await fireEvent.click(screen.getByRole("button", { name: "Search" }));
    await screen.findByText("Match 25");

    await fireEvent.click(screen.getByRole("button", { name: "Load more" }));

    await waitFor(() => expect(searchRequestsByPlugin).toHaveBeenLastCalledWith({
      kind: REQUEST_MEDIA_KIND.series,
      pluginId: "cinema-metadata",
      fields: { seriesTitle: "Andor" },
      limit: 50,
      hideNsfw: true,
    }));
    expect(await screen.findByText("Match 50")).toBeInTheDocument();
  });

  it("invalidates candidates and reselects an eligible provider when the NSFW boundary changes", async () => {
    fetchPluginProviders.mockResolvedValue([adultTvdb(), tmdb()]);
    searchRequestsByPlugin.mockResolvedValue({
      results: [result("Old boundary result", "old-result", "cinema-metadata", "tmdb")],
      providerErrors: [],
    });

    render(RequestDiscoverHarness);
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());
    await fireEvent.click(screen.getByRole("button", { name: "Series" }));
    await fireEvent.input(await screen.findByLabelText("Series title"), { target: { value: "Andor" } });
    await fireEvent.click(screen.getByRole("button", { name: "Search" }));
    expect(await screen.findByText("Old boundary result")).toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Show NSFW" }));

    expect(await screen.findByRole("button", { name: "Source" })).toBeInTheDocument();
    expect(screen.queryByText("Old boundary result")).not.toBeInTheDocument();
    expect(screen.queryByText("1 found")).not.toBeInTheDocument();
    expect(await screen.findByLabelText("Adult series title")).toHaveValue("");
  });
});

function tmdb(): PluginProvider {
  return provider("cinema-metadata", "Alpha TV Metadata", ENTITY_KIND.videoSeries, [
    { key: "seriesTitle", label: "Series title", type: PLUGIN_SEARCH_FIELD_TYPE.text, required: true },
    { key: "year", label: "Year", type: PLUGIN_SEARCH_FIELD_TYPE.year, required: false },
  ], ["tmdb"]);
}

function tvdb(): PluginProvider {
  return provider("tv-database", "Beta TV Database", ENTITY_KIND.videoSeries, [
    { key: "showName", label: "Show name", type: PLUGIN_SEARCH_FIELD_TYPE.text, required: true },
    { key: "episodeTitle", label: "Episode title", type: PLUGIN_SEARCH_FIELD_TYPE.text, required: false },
  ], ["tvdb"]);
}

function adultTvdb(): PluginProvider {
  return {
    ...provider("adult-tv", "Adult TV Metadata", ENTITY_KIND.videoSeries, [
      { key: "adultTitle", label: "Adult series title", type: PLUGIN_SEARCH_FIELD_TYPE.text, required: true },
    ], ["adult-tv"]),
    isNsfw: true,
  };
}

function openLibrary(): PluginProvider {
  return provider("openlibrary", "Open Library", ENTITY_KIND.book, [
    { key: "title", label: "Book title", type: PLUGIN_SEARCH_FIELD_TYPE.text, required: true },
  ], ["openlibrary"]);
}

function connection(
  id: string,
  name: string,
  entityKind: EntityKind,
  overrides: Partial<ConnectionResponse> = {},
): ConnectionResponse {
  return {
    id, name, pluginId: "fixture-catalog", baseUrl: `http://${id}.test`, enabled: true,
    enabledCapabilities: [PLUGIN_CAPABILITY.catalogDiscovery],
    effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.catalogDiscovery, operations: [INTEGRATION_OPERATION.browse], entityKinds: [entityKind] }],
    settings: {}, configuredSecretKeys: [], revision: 1, status: CONNECTION_STATUS.ready,
    remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
    ...overrides,
  };
}

function provider(
  id: string,
  name: string,
  entityKind: string,
  fields: NonNullable<PluginProvider["supports"][number]["search"]>["fields"],
  identityNamespaces: string[],
): PluginProvider {
  return {
    id,
    name,
    version: "2.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{
      entityKind,
      actions: [IDENTIFY_ACTION.search, IDENTIFY_ACTION.lookupId],
      identityNamespaces,
      search: { fields },
    }],
    auth: [],
    missingAuthKeys: [],
  };
}

function result(
  title: string,
  value: string,
  pluginId: string,
  namespace: string,
  kind: RequestSearchResult["kind"] = REQUEST_MEDIA_KIND.series,
): RequestSearchResult {
  return {
    serviceId: pluginId,
    source: REQUEST_PROVIDER_KIND.plugin,
    kind,
    externalId: value,
    title,
    subtitle: null,
    year: 2022,
    overview: `${title} overview`,
    posterUrl: null,
    backdropUrl: null,
    rating: null,
    runtimeMinutes: null,
    certification: null,
    trackCount: null,
    tags: [],
    tracked: false,
    upstreamId: null,
    monitored: null,
    requestable: true,
    providerName: "Alpha TV Metadata",
    pluginId,
    externalIdentity: { namespace, value },
  };
}
