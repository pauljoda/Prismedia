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
const searchRequestsByPlugin = vi.fn();
const goto = vi.fn();

vi.mock("$lib/api/plugins", () => ({
  fetchPluginProviders: (...args: unknown[]) => fetchPluginProviders(...args),
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
    searchRequestsByPlugin.mockReset();
    goto.mockReset();
    fetchPluginProviders.mockResolvedValue([tmdb(), tvdb(), openLibrary()]);
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

  it("shows a direct no-provider state for a selected kind", async () => {
    fetchPluginProviders.mockResolvedValue([tmdb()]);
    render(RequestDiscoverHarness);
    await waitFor(() => expect(fetchPluginProviders).toHaveBeenCalledOnce());

    await fireEvent.click(screen.getByRole("button", { name: "Book" }));

    expect(await screen.findByText(/No installed provider can search and review books/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Source:/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Search" })).not.toBeInTheDocument();
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
): RequestSearchResult {
  return {
    serviceId: pluginId,
    source: REQUEST_PROVIDER_KIND.plugin,
    kind: REQUEST_MEDIA_KIND.series,
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
