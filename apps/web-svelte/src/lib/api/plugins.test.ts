import { describe, expect, it, vi } from "vitest";
import {
  fetchPluginProviders,
  installPlugin,
  removePlugin,
  savePluginAuth,
} from "./plugins";

describe("plugin provider API", () => {
  it("lists provider plugins through the generated endpoint", async () => {
    const fetchMock = mockFetch([pluginProvider("tmdb")]);

    const providers = await fetchPluginProviders();

    expect(fetchMock).toHaveBeenCalledWith("/api/plugins", expect.objectContaining({ method: "GET" }));
    expect(providers[0].id).toBe("tmdb");
  });

  it("installs and removes provider plugins through generated routes", async () => {
    const fetchMock = mockFetch(pluginProvider("tmdb"));

    await installPlugin("tmdb");

    expect(fetchMock).toHaveBeenCalledWith("/api/plugins/tmdb", expect.objectContaining({ method: "POST" }));

    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

    await removePlugin("tmdb");

    expect(fetchMock).toHaveBeenLastCalledWith("/api/plugins/tmdb", expect.objectContaining({ method: "DELETE" }));
  });

  it("saves provider credential values through the generated auth route", async () => {
    const fetchMock = mockFetch(undefined, 204);

    await savePluginAuth("tmdb", { apiKey: "secret" });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/plugins/tmdb/auth",
      expect.objectContaining({
        method: "PUT",
        body: JSON.stringify({ values: { apiKey: "secret" } }),
      }),
    );
  });
});

function mockFetch(data: unknown, status = 200) {
  const fetchMock = vi.fn(async () => new Response(
    data === undefined ? null : JSON.stringify(data),
    {
      headers: data === undefined ? undefined : { "Content-Type": "application/json" },
      status,
    },
  ));
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function pluginProvider(id: string) {
  return {
    id,
    name: "The Movie Database",
    version: "1.0.0",
    installed: true,
    enabled: true,
    isNsfw: false,
    supports: [{ entityKind: "video", actions: ["search"] }],
    auth: [],
    missingAuthKeys: [],
  };
}
