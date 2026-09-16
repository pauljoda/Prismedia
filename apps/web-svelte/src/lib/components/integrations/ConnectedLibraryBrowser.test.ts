import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse } from "$lib/api/generated/model";
import ConnectedLibraryBrowser from "./ConnectedLibraryBrowser.svelte";

const api = vi.hoisted(() => ({ fetchManagedTracking: vi.fn(), fetchManagedLibrary: vi.fn(), fetchManagedItem: vi.fn(), fetchManagerOptions: vi.fn(), fetchLibraryMounts: vi.fn(), inspectLocalLibraryAccess: vi.fn() }));
vi.mock("$lib/api/managed-libraries", () => api);
const connection: ConnectionResponse = {
  id: "connection-one", pluginId: "fixture-manager", name: "Existing collection", baseUrl: "http://manager.test/", enabled: true,
  enabledCapabilities: [PLUGIN_CAPABILITY.connectedLibrary, PLUGIN_CAPABILITY.externalManager], settings: {}, configuredSecretKeys: [],
  revision: 1, status: CONNECTION_STATUS.ready, remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
  effectiveCapabilities: [
    { kind: PLUGIN_CAPABILITY.connectedLibrary, operations: [INTEGRATION_OPERATION.searchLibrary, INTEGRATION_OPERATION.getLibraryItem], entityKinds: [ENTITY_KIND.movie] },
    { kind: PLUGIN_CAPABILITY.externalManager, operations: [INTEGRATION_OPERATION.managerOptions], entityKinds: [ENTITY_KIND.movie] },
  ],
};
const item = { remoteId: "1", entityKind: ENTITY_KIND.movie, title: "A film", year: 2024, externalIds: { fixture: "identity" }, monitored: false, profileId: "4", remoteFileCount: 1 };
const snapshot = { item, path: "/remote/film", files: [{ remoteId: "2", path: "/remote/film/film.mkv", sizeBytes: 128, addedAt: null, targets: [{ remoteId: "1", entityKind: ENTITY_KIND.movie, title: "A film", seasonNumber: null, episodeNumber: null, absoluteNumber: null }] }], observedAt: "2026-09-16T12:00:00Z" };

describe("Connected library browser", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.fetchLibraryMounts.mockResolvedValue([]);
    api.fetchManagedTracking.mockResolvedValue([]);
    api.fetchManagedLibrary.mockResolvedValue({ items: [item], nextCursor: null });
    api.fetchManagedItem.mockResolvedValue(snapshot);
    api.fetchManagerOptions.mockResolvedValue({ profiles: [{ id: "4", label: "Existing quality" }], roots: [] });
  });

  it("pins the selected external identity and explains that remote files are not verified local files", async () => {
    render(ConnectedLibraryBrowser, { connection });
    await fireEvent.click(await screen.findByRole("button", { name: "Inspect holding" }));
    await screen.findByText("Profile: Existing quality");
    expect(api.fetchManagedItem).toHaveBeenCalledWith(connection.id, { entityKind: item.entityKind, remoteId: item.remoteId, expectedExternalIds: item.externalIds });
    expect(screen.getByText(/Local access has not been checked/)).toBeInTheDocument();
    expect(screen.getByText("/remote/film/film.mkv")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Download|Import|Request/ })).not.toBeInTheDocument();
  });

  it("does not turn an outage into an empty successful library", async () => {
    api.fetchManagedLibrary.mockRejectedValue(new Error("The manager is unavailable"));
    render(ConnectedLibraryBrowser, { connection });
    await screen.findByText("The manager is unavailable");
    expect(screen.queryByText("No matching holdings")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Retry" })).toBeInTheDocument();
  });

  it("checks local bytes separately and preserves unavailable file evidence", async () => {
    api.inspectLocalLibraryAccess.mockResolvedValue({ remote: snapshot, files: [{ remoteId: "2", libraryRootId: null, localPath: null,
      isReadable: false, sizeMatches: false, problem: "No local mapping covers this remote file." }] });
    render(ConnectedLibraryBrowser, { connection });
    await fireEvent.click(await screen.findByRole("button", { name: "Inspect holding" }));
    await screen.findByText("Profile: Existing quality");
    await fireEvent.click(screen.getByRole("button", { name: "Check local access" }));
    await screen.findByText("No local mapping covers this remote file.");
    expect(screen.queryByText("Readable locally · size matches")).not.toBeInTheDocument();
  });

  it("ignores a late detail response after the dialog was closed and reopened", async () => {
    let finish: (value: typeof snapshot) => void = () => {};
    api.fetchManagedItem.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
    render(ConnectedLibraryBrowser, { connection });
    await fireEvent.click(await screen.findByRole("button", { name: "Inspect holding" }));
    await fireEvent.click(screen.getByRole("button", { name: "Done" }));
    await fireEvent.click(screen.getByRole("button", { name: "Inspect holding" }));
    await screen.findByText("Profile: Existing quality");
    finish({ ...snapshot, path: "/stale/response" });
    await waitFor(() => expect(api.fetchManagedItem).toHaveBeenCalledTimes(2));
    expect(screen.queryByText("Remote folder: /stale/response")).not.toBeInTheDocument();
  });
});
