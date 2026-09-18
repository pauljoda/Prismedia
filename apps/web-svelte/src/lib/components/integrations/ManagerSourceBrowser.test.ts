import { fireEvent, render, screen } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { page } from "$app/state";
import { CONNECTION_STATUS, ENTITY_KIND, EXTERNAL_ID_PROVIDER, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse } from "$lib/api/generated/model";
import ManagerSourceBrowser from "./ManagerSourceBrowser.svelte";

const mocks = vi.hoisted(() => ({ searchManagerTitles: vi.fn(), fetchManagedLibrary: vi.fn(), goto: vi.fn(async (_url: string) => {}) }));
vi.mock("$lib/api/managed-discovery", () => ({ searchManagerTitles: mocks.searchManagerTitles }));
vi.mock("$lib/api/managed-libraries", () => ({ fetchManagedLibrary: mocks.fetchManagedLibrary }));
vi.mock("$app/navigation", () => ({ goto: mocks.goto }));

const connection: ConnectionResponse = {
  id: "movie-manager", pluginId: "fixture", name: "Movie source", baseUrl: "http://manager.test", enabled: true,
  enabledCapabilities: [PLUGIN_CAPABILITY.externalManager, PLUGIN_CAPABILITY.connectedLibrary],
  settings: {}, configuredSecretKeys: [], revision: 1, status: CONNECTION_STATUS.ready,
  remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
  effectiveCapabilities: [
    { kind: PLUGIN_CAPABILITY.externalManager, entityKinds: [ENTITY_KIND.movie], operations: [INTEGRATION_OPERATION.discoverManaged] },
    { kind: PLUGIN_CAPABILITY.connectedLibrary, entityKinds: [ENTITY_KIND.movie], operations: [INTEGRATION_OPERATION.searchLibrary] },
  ],
};

describe("Manager source discovery", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    page.url = new URL("http://localhost/request?connection=movie-manager") as unknown as typeof page.url;
    mocks.searchManagerTitles.mockResolvedValue({ items: [{ entityKind: ENTITY_KIND.movie, title: "A new film", year: 2025,
      externalIdentity: { namespace: EXTERNAL_ID_PROVIDER.tmdb, value: "42" }, metadata: { posterUrl: "https://example.test/poster.jpg" } }] });
    mocks.fetchManagedLibrary.mockResolvedValue({ items: [], nextCursor: null });
  });

  it("searches the selected manager's catalog and preserves source identity and query through review", async () => {
    const { container } = render(ManagerSourceBrowser, { connection });
    expect(mocks.fetchManagedLibrary).not.toHaveBeenCalled();
    await fireEvent.input(screen.getByRole("textbox", { name: "Find new titles in Movie source" }), { target: { value: "new film" } });
    await fireEvent.click(screen.getByRole("button", { name: "Search" }));
    await fireEvent.click(await screen.findByRole("button", { name: "A new film" }));
    expect(mocks.searchManagerTitles).toHaveBeenCalledWith(connection.id, { entityKind: ENTITY_KIND.movie, query: "new film", limit: 50 });
    expect(container.querySelector('img[src="https://example.test/poster.jpg"]')).toBeInTheDocument();
    const url = new URL(mocks.goto.mock.calls[0]![0], "http://localhost");
    expect(url.pathname).toBe("/request/movie/42");
    expect(url.searchParams.get("connection")).toBe(connection.id);
    expect(url.searchParams.get("namespace")).toBe(EXTERNAL_ID_PROVIDER.tmdb);
    expect(new URLSearchParams(url.searchParams.get("back")!).get("managerQuery")).toBe("new film");
    expect(mocks.fetchManagedLibrary).not.toHaveBeenCalled();
    await fireEvent.click(screen.getByRole("tab", { name: "In your library" }));
    await screen.findByText("No matching titles");
    expect(mocks.fetchManagedLibrary).toHaveBeenCalledWith(connection.id, expect.objectContaining({ entityKind: ENTITY_KIND.movie }));
  });
});
