import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, MANAGED_REQUEST_PHASE, PLUGIN_CAPABILITY } from "$lib/api/generated/codes";
import type { ConnectionResponse, ManagedRequestPreview, ManagedRequestResponse } from "$lib/api/generated/model";
import ManagedRequests from "./ManagedRequests.svelte";

const api = vi.hoisted(() => ({ fetchManagedRequests: vi.fn(), fetchManagedRequestPreview: vi.fn(), saveManagedRequest: vi.fn(), refreshRequest: vi.fn(), cancelRequest: vi.fn(), fetchLibraryMounts: vi.fn(), fetchEntities: vi.fn() }));
vi.mock("$lib/api/managed-requests", () => ({ ...api, ManagedRequestRejectedError: class extends Error {} }));
vi.mock("$lib/api/managed-libraries", () => api);
vi.mock("$lib/api/entities", () => api);
const connection: ConnectionResponse = {
  id: "connection", pluginId: "fixture-manager", name: "Movie manager", baseUrl: "http://manager.test", enabled: true,
  enabledCapabilities: [PLUGIN_CAPABILITY.externalManager], settings: {}, configuredSecretKeys: [], revision: 1,
  status: CONNECTION_STATUS.ready, remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
  effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.externalManager, entityKinds: [ENTITY_KIND.movie],
    operations: [INTEGRATION_OPERATION.lookupManaged, INTEGRATION_OPERATION.ensureManaged] }],
};
const seriesConnection: ConnectionResponse = {
  ...connection,
  name: "Series manager",
  effectiveCapabilities: [{
    kind: PLUGIN_CAPABILITY.externalManager,
    entityKinds: [ENTITY_KIND.videoSeries],
    operations: [INTEGRATION_OPERATION.lookupManaged, INTEGRATION_OPERATION.ensureManaged],
  }],
};
const mount = { id: "mount", connectionId: connection.id, libraryRootId: "root", remoteRootId: "1", remotePath: "/movies", localPath: "/local/movies", label: "Movie library" };
const preview: ManagedRequestPreview = { entityId: "wanted", title: "Wanted film", work: { entityKind: ENTITY_KIND.movie, externalIds: { tmdb: "42" } },
  mount, options: { profiles: [{ id: "7", label: "Preferred quality" }], roots: [{ id: "1", path: "/movies", accessible: true }] }, existing: null };
const seriesPreview: ManagedRequestPreview = {
  entityId: "series",
  title: "Wanted series",
  work: {
    entityKind: ENTITY_KIND.videoSeries,
    externalIds: { tvdb: "42" },
    targets: [
      { entityKind: ENTITY_KIND.videoEpisode, externalIds: { tvdb: "101" }, seasonNumber: 1, episodeNumber: 1 },
      { entityKind: ENTITY_KIND.videoEpisode, externalIds: { tvdb: "102" }, seasonNumber: 1, episodeNumber: 2 },
    ],
  },
  mount: { ...mount, remoteRootId: "tv", remotePath: "/series", label: "Series library" },
  options: { profiles: [{ id: "7", label: "Preferred quality" }], roots: [{ id: "tv", path: "/series", accessible: true }] },
  existing: null,
  targetEntityIds: ["episode-1", "episode-2"],
};
function request(values: Partial<ManagedRequestResponse> = {}): ManagedRequestResponse {
  return { id: "request", connectionId: connection.id, entityId: "wanted", libraryRootId: "root", title: "Wanted film", phase: MANAGED_REQUEST_PHASE.pendingCreation,
    revision: 1, remoteId: null, monitored: false, search: true, reviewRequired: false, canCancel: true,
    createdAt: "2026-09-16T12:00:00Z", updatedAt: "2026-09-16T12:00:00Z", problem: null,
    holdingId: "request", ...values };
}
async function review() {
  render(ManagedRequests, { connection });
  await fireEvent.click(await screen.findByRole("button", { name: "Request a wanted movie" }));
  const picker = await screen.findByRole("button", { name: "Wanted movie: Find a wanted movie" });
  await waitFor(() => expect(picker).toBeEnabled()); await fireEvent.click(picker);
  await fireEvent.click((await screen.findByText("Wanted film")).closest('[role="option"]')!);
  await fireEvent.click(screen.getByRole("button", { name: "Review manager request" }));
  await screen.findByRole("button", { name: "Request through manager" });
}
describe("Managed requests", () => {
  beforeEach(() => {
    vi.resetAllMocks(); api.fetchManagedRequests.mockResolvedValue([]); api.fetchLibraryMounts.mockResolvedValue([mount]);
    api.fetchEntities.mockResolvedValue({ items: [{ id: "wanted", title: "Wanted film", coverThumbUrl: null }] });
    api.fetchManagedRequestPreview.mockResolvedValue(preview); api.saveManagedRequest.mockResolvedValue(request());
  });
  it("does not repeat a request already represented by a followed library item", async () => {
    api.fetchManagedRequests.mockResolvedValue([request({ phase: MANAGED_REQUEST_PHASE.completed, canCancel: false }), request({ id: "still-pending", title: "Another film" })]);
    render(ManagedRequests, { connection, excludeIds: ["request"] });
    await screen.findByText("Another film");
    expect(screen.queryByText("Wanted film")).not.toBeInTheDocument();
    expect(screen.getByText("Request queued")).toBeInTheDocument();
  });

  it("submits the exact reviewed identity and explicit choices", async () => {
    await review(); await fireEvent.click(screen.getByRole("checkbox", { name: "Search now" }));
    await fireEvent.click(screen.getByRole("button", { name: "Request through manager" }));
    await waitFor(() => expect(api.saveManagedRequest).toHaveBeenCalledWith(connection.id, expect.objectContaining({
      entityId: "wanted", libraryRootId: "root", reviewedWork: preview.work, profileId: "7", monitored: false, search: false,
    })));
    expect(api.fetchEntities).toHaveBeenCalledWith(expect.objectContaining({ kind: ENTITY_KIND.movie, wanted: true, hasFile: false }));
    await screen.findByText("Request queued");
  });
  it("retains the operation ID and reviewed choices after a lost response", async () => {
    api.saveManagedRequest.mockRejectedValueOnce(new Error("Response interrupted"));
    await review(); await fireEvent.click(screen.getByRole("button", { name: "Request through manager" }));
    await screen.findByText("Response interrupted");
    expect(screen.getByRole("checkbox", { name: "Search now" })).toBeDisabled();
    await fireEvent.click(screen.getByRole("button", { name: "Retry same request" }));
    await waitFor(() => expect(api.saveManagedRequest).toHaveBeenCalledTimes(2));
    expect(api.saveManagedRequest.mock.calls[1][1]).toEqual(api.saveManagedRequest.mock.calls[0][1]);
  });
  it("keeps an existing profile fixed during initial delegation", async () => {
    api.fetchManagedRequestPreview.mockResolvedValue({ ...preview, existing: { item: { profileId: "7", monitored: true } } });
    await review();
    expect(screen.getByRole("button", { name: "Request profile" })).toBeDisabled();
    expect(screen.getByRole("checkbox", { name: "Monitor this movie" })).toBeChecked();
  });
  it("shows retained uncertainty without offering unsafe cancellation", async () => {
    api.fetchManagedRequests.mockResolvedValue([request({ phase: MANAGED_REQUEST_PHASE.creationUncertain, canCancel: false, reviewRequired: true, problem: "Inspect the manager before refreshing." })]);
    render(ManagedRequests, { connection: { ...connection, enabled: false } });
    await screen.findByText("Needs review");
    expect(screen.queryByRole("button", { name: "Cancel request" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Request a wanted movie" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Refresh request" })).toBeEnabled();
  });
  it("reopens a prepared movie's retained request instead of offering a duplicate after navigation", async () => {
    api.fetchManagedRequests.mockResolvedValue([request({ phase: MANAGED_REQUEST_PHASE.creationUncertain, canCancel: false, reviewRequired: true })]);
    render(ManagedRequests, { connection, initialEntity: { id: "wanted", title: "Wanted film", thumbnailUrl: null } });
    await screen.findByText("Needs review");
    expect(screen.queryByRole("button", { name: "Review manager request" })).not.toBeInTheDocument();
    expect(api.fetchEntities).not.toHaveBeenCalled();
  });

  it("submits only the prepared finite episode scope without broad series monitoring", async () => {
    api.fetchManagedRequestPreview.mockResolvedValue(seriesPreview);
    api.saveManagedRequest.mockResolvedValue(request({
      entityId: "series",
      title: "Wanted series",
      targetEntityIds: ["episode-1", "episode-2"],
    }));
    render(ManagedRequests, {
      connection: seriesConnection,
      entityKind: ENTITY_KIND.videoSeries,
      initialEntity: { id: "series", title: "Wanted series", thumbnailUrl: null },
      initialTargetEntityIds: ["episode-2", "episode-1"],
    });

    await fireEvent.click(await screen.findByRole("button", { name: "Review manager request" }));
    expect(screen.queryByRole("checkbox", { name: "Monitor this movie" })).not.toBeInTheDocument();
    expect(screen.queryByRole("checkbox", { name: "Search now" })).not.toBeInTheDocument();
    expect(await screen.findByText(/Only the 2 selected episodes will be searched/)).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Request through manager" }));

    await waitFor(() => expect(api.saveManagedRequest).toHaveBeenCalledWith(seriesConnection.id, expect.objectContaining({
      entityId: "series",
      targetEntityIds: ["episode-1", "episode-2"],
      monitored: false,
      search: true,
    })));
    expect(api.fetchManagedRequestPreview).toHaveBeenCalledWith(seriesConnection.id, {
      entityId: "series",
      libraryRootId: "root",
      targetEntityIds: ["episode-2", "episode-1"],
    });
  });

  it("shows an explicit conflict when the active series request owns another episode scope", async () => {
    api.fetchManagedRequests.mockResolvedValue([request({
      entityId: "series",
      title: "Wanted series",
      targetEntityIds: ["episode-9"],
      phase: MANAGED_REQUEST_PHASE.completed,
      canCancel: false,
    })]);
    render(ManagedRequests, {
      connection: seriesConnection,
      entityKind: ENTITY_KIND.videoSeries,
      initialEntity: { id: "series", title: "Wanted series", thumbnailUrl: null },
      initialTargetEntityIds: ["episode-1"],
    });

    expect(await screen.findByText(/already follows a different episode selection/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Review manager request" })).not.toBeInTheDocument();
  });
});
