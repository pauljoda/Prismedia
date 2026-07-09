import { afterEach, describe, expect, it, vi } from "vitest";
import * as pluginApi from "./plugins";

describe("plugin API", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("uses only the current generated plugin endpoints", async () => {
    const fetchMock = mockFetch((url, init) => {
      if (init.method === "DELETE" || url.endsWith("/auth")) return undefined;
      if (url.endsWith("/stash-scrapers")) {
        return [{ providerId: "stash-example", name: "Stash Example", version: "1.0.0" }];
      }
      return provider();
    });

    await pluginApi.fetchPluginProviders();
    await pluginApi.fetchStashScrapers();
    await pluginApi.installPlugin("tmdb");
    await pluginApi.updatePlugin("tmdb");
    await pluginApi.savePluginAuth("tmdb", { api_key: "secret" });
    await pluginApi.removePlugin("tmdb");

    expect(fetchMock.mock.calls.map(([url, init]) => [url, (init as RequestInit).method])).toEqual([
      ["/api/plugins", "GET"],
      ["/api/plugins/stash-scrapers", "GET"],
      ["/api/plugins/tmdb", "POST"],
      ["/api/plugins/tmdb/update", "POST"],
      ["/api/plugins/tmdb/auth", "PUT"],
      ["/api/plugins/tmdb", "DELETE"],
    ]);
    const authInit = fetchMock.mock.calls[4][1] as RequestInit;
    expect(authInit.body).toBe(JSON.stringify({ values: { api_key: "secret" } }));
  });

  it("exports only clients backed by the current plugin endpoint group", () => {
    expect(Object.keys(pluginApi).sort()).toEqual([
      "fetchPluginProviders",
      "fetchStashScrapers",
      "installPlugin",
      "removePlugin",
      "savePluginAuth",
      "updatePlugin",
    ]);
  });
});

function provider() {
  return {
    id: "tmdb",
    name: "TMDB",
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [],
    auth: [],
    missingAuthKeys: [],
  };
}

function mockFetch(responseFor: (url: string, init: RequestInit) => unknown) {
  const fetchMock = vi.fn(async (url: string, init: RequestInit = {}) => {
    const data = responseFor(url, init);
    return new Response(data === undefined ? null : JSON.stringify(data), {
      headers: data === undefined ? undefined : { "content-type": "application/json" },
      status: data === undefined ? 204 : 200,
    });
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}
