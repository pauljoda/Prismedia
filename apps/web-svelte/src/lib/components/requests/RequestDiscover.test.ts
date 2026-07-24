import { fireEvent, render, screen, waitFor, within } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  ENTITY_KIND,
  IDENTIFY_ACTION,
  PLUGIN_SEARCH_FIELD_TYPE,
  REQUEST_MEDIA_KIND,
  REQUEST_PROVIDER_KIND,
} from "$lib/api/generated/codes";
import type { RequestSearchResult } from "$lib/api/generated/model";
import type { PluginProvider } from "$lib/api/identify-types";
import RequestDiscoverHarness from "./RequestDiscover.test-harness.svelte";

const fetchPluginProviders = vi.fn();
const fetchSettingsValues = vi.fn();
const searchRequestsByPlugin = vi.fn();
const goto = vi.fn();

vi.mock("$lib/api/plugins", () => ({
  fetchPluginProviders: (...args: unknown[]) => fetchPluginProviders(...args),
}));

vi.mock("$lib/api/settings", () => ({
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
    fetchPluginProviders.mockResolvedValue([tmdb(), tvdb(), openLibrary()]);
    fetchSettingsValues.mockResolvedValue({ values: { "identify.defaultProviders": {} } });
    searchRequestsByPlugin.mockResolvedValue({ results: [], providerErrors: [] });
  });

  it("requires a kind, filters its providers, and swaps to the selected provider's schema", async () => {
    render(RequestDiscoverHarness);

    expect(screen.queryByRole("button", { name: /Source:/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "All" })).not.toBeInTheDocument();
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());

    await fireEvent.click(screen.getByRole("button", { name: "Series" }));

    expect(await screen.findByLabelText("Series title")).toBeInTheDocument();
    expect(screen.getByLabelText("Year")).toBeInTheDocument();
    const providerTrigger = screen.getByRole("button", { name: "Source: Alpha TV Metadata" });
    await fireEvent.click(providerTrigger);

    const listbox = screen.getByRole("listbox");
    expect(within(listbox).getByText("Beta TV Database")).toBeInTheDocument();
    expect(within(listbox).queryByText("Open Library")).not.toBeInTheDocument();
    await fireEvent.mouseDown(within(listbox).getByRole("option", { name: /beta tv database/i }));

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

    expect(await screen.findByRole("button", { name: "Source: Beta TV Database" }))
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

    const candidateButtons = await screen.findAllByRole("button", { name: /^Use / });
    expect(candidateButtons.map((button) => button.getAttribute("aria-label"))).toEqual([
      "Use Ranked first (2022)",
      "Use Ranked second (2022)",
    ]);
    expect(screen.getByText("Best")).toBeInTheDocument();
    expect(screen.queryByText("Missing route")).not.toBeInTheDocument();

    await fireEvent.click(screen.getByRole("button", { name: "Use Ranked second (2022)" }));

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
    await fireEvent.click(screen.getByRole("button", { name: "Audiobook" }));

    expect(await screen.findByRole("button", { name: "Source: Open Library" })).toBeInTheDocument();
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
    await fireEvent.click(await screen.findByRole("button", { name: "Use Project Hail Mary (2022)" }));

    expect(goto).toHaveBeenCalledWith(
      "/request/audiobook/works%2FOL%3AProject%3AHail%3AMary?plugin=openlibrary&namespace=openlibrary",
    );
  });

  it("shows a direct no-provider state for a selected kind", async () => {
    fetchPluginProviders.mockResolvedValue([tmdb()]);
    render(RequestDiscoverHarness);
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());

    await fireEvent.click(screen.getByRole("button", { name: "Book" }));

    expect(await screen.findByText(/No installed provider can search and review books/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Source:/ })).not.toBeInTheDocument();
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

    expect(await screen.findByRole("button", { name: "Source: Adult TV Metadata" })).toBeInTheDocument();
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
