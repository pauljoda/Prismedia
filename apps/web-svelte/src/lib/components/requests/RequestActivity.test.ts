import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  CONNECTION_STATUS, ENTITY_KIND, INTEGRATION_OPERATION, INTEGRATION_TRANSFER_MODE,
  INTEGRATION_TRANSFER_PHASE, MANAGED_REQUEST_PHASE, MANAGED_TRACKING_STATUS, PLUGIN_CAPABILITY, SOURCE_ACQUISITION_STATE,
} from "$lib/api/generated/codes";
import type { ConnectionResponse, IntegrationTransferResponse, ManagedRequestResponse, ManagedTrackingResponse } from "$lib/api/generated/model";
import RequestActivity from "./RequestActivity.svelte";

const api = vi.hoisted(() => ({
  fetchIntegrationTransfers: vi.fn(), cancelPublicationTransfer: vi.fn(), retryPublicationTransfer: vi.fn(),
  fetchManagedRequests: vi.fn(), refreshRequest: vi.fn(), cancelRequest: vi.fn(),
  fetchManagedTracking: vi.fn(), refreshTracking: vi.fn(), resolveEntityHrefById: vi.fn(), goto: vi.fn(),
}));
vi.mock("$lib/api/integration-transfers", () => api);
vi.mock("$lib/api/managed-requests", () => api);
vi.mock("$lib/api/managed-libraries", () => api);
vi.mock("$lib/entities/entity-route-resolver", () => ({ resolveEntityHrefById: api.resolveEntityHrefById }));
vi.mock("$app/navigation", () => ({ goto: api.goto }));

const connection: ConnectionResponse = {
  id: "manager", pluginId: "fixture-manager", name: "Movie manager", baseUrl: "http://manager.test", enabled: true,
  enabledCapabilities: [PLUGIN_CAPABILITY.externalManager], settings: {}, configuredSecretKeys: [], revision: 1,
  status: CONNECTION_STATUS.ready, remoteInstanceId: null, hasPersistentRemoteIdentity: false, lastCheckedAt: null, lastError: null,
  effectiveCapabilities: [{ kind: PLUGIN_CAPABILITY.externalManager, entityKinds: [ENTITY_KIND.movie],
    operations: [INTEGRATION_OPERATION.reconcileManaged, INTEGRATION_OPERATION.inspectManagedRelease] }],
};
const transfer = (id: string, values: Partial<IntegrationTransferResponse> = {}): IntegrationTransferResponse => ({
  id, connectionId: connection.id, title: `Completed ${id}`, entityKind: ENTITY_KIND.book, libraryRootId: "root",
  mode: INTEGRATION_TRANSFER_MODE.sourceDownload, phase: INTEGRATION_TRANSFER_PHASE.completed,
  createdAt: `2026-09-17T12:00:0${id}Z`, updatedAt: `2026-09-17T12:00:0${id}Z`, artifactCount: 1,
  importedEntityIds: [`entity-${id}`], lastError: null, canCancel: false, cancellationRequested: false, ...values,
});
const request = (values: Partial<ManagedRequestResponse> = {}): ManagedRequestResponse => ({
  id: "managed", connectionId: connection.id, entityId: "entity", libraryRootId: "root", title: "Managed request",
  phase: MANAGED_REQUEST_PHASE.awaitingFiles, revision: 1, remoteId: "remote", monitored: true, search: true,
  reviewRequired: false, canCancel: true, createdAt: "2026-09-17T12:00:00Z", updatedAt: "2026-09-17T12:00:00Z",
  problem: null, ...values,
});
const holding = (values: Partial<ManagedTrackingResponse> = {}): ManagedTrackingResponse => ({
  id: "managed", connectionId: connection.id, libraryRootId: "root", title: "Dune", status: MANAGED_TRACKING_STATUS.tracking,
  revision: 1, lastCheckedAt: "2026-09-17T12:00:00Z", problem: null, bindings: [], targets: [],
  item: { entityKind: ENTITY_KIND.movie, remoteId: "remote", expectedExternalIds: { tmdb: "438631" } }, ...values,
});

describe("request activity", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    api.fetchIntegrationTransfers.mockResolvedValue([]);
    api.fetchManagedRequests.mockResolvedValue([]);
    api.fetchManagedTracking.mockResolvedValue([]);
    api.refreshTracking.mockResolvedValue(undefined);
    api.refreshRequest.mockResolvedValue(undefined);
  });

  it("deduplicates tracked requests and caps history beneath priority sections", async () => {
    api.fetchIntegrationTransfers.mockResolvedValue(Array.from({ length: 8 }, (_, index) => transfer(String(index + 1))));
    api.fetchManagedRequests.mockResolvedValue([request({ title: "Duplicate request" })]);
    api.fetchManagedTracking.mockResolvedValue([holding()]);
    render(RequestActivity, { connections: [connection] });

    await screen.findByText("Dune");
    expect(screen.getByRole("heading", { name: "Following in your library" })).toBeInTheDocument();
    expect(screen.queryByText("Duplicate request")).not.toBeInTheDocument();
    expect(screen.getByText("6 of 8")).toBeInTheDocument();
    expect(screen.queryByText("Completed 1")).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Show all 8 recent items" }));
    expect(screen.getByText("Completed 1")).toBeInTheDocument();
  });

  it("keeps safe transfer, manager request, and tracking controls on their compact rows", async () => {
    api.fetchIntegrationTransfers.mockResolvedValue([transfer("1", { title: "Downloading book", phase: INTEGRATION_TRANSFER_PHASE.transferring, importedEntityIds: [], canCancel: true })]);
    api.fetchManagedRequests.mockResolvedValue([request({ id: "request-only", title: "Waiting film" })]);
    api.fetchManagedTracking.mockResolvedValue([holding()]);
    render(RequestActivity, { connections: [connection] });

    await screen.findByText("Downloading book");
    expect(screen.getByRole("button", { name: "Cancel download" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: "Refresh" })).toHaveLength(2);
    expect(screen.getAllByRole("button", { name: "Manage" })).toHaveLength(2);
  });

  it("retains last known rows and does not claim all-clear after a partial refresh failure", async () => {
    api.fetchManagedTracking.mockResolvedValueOnce([holding()]);
    render(RequestActivity, { connections: [connection] });
    await screen.findByText("Dune");

    api.fetchManagedTracking.mockRejectedValueOnce(new Error("Tracking endpoint unavailable"));
    await fireEvent.click(screen.getByRole("button", { name: "Refresh" }));
    await screen.findByText(/Tracking endpoint unavailable/);
    expect(screen.getByText("Dune")).toBeInTheDocument();
    expect(screen.queryByText("All caught up.")).not.toBeInTheDocument();
    await waitFor(() => expect(api.refreshTracking).toHaveBeenCalledWith(connection.id, "managed"));
  });

  it("distinguishes stopping a source import from cancelling its remote download", async () => {
    api.fetchIntegrationTransfers.mockResolvedValue([transfer("1", { title: "Requested chapter", mode: INTEGRATION_TRANSFER_MODE.sourceRequest,
      phase: INTEGRATION_TRANSFER_PHASE.awaitingRemote, importedEntityIds: [], canCancel: true })]);
    render(RequestActivity, { connections: [connection] });
    await screen.findByText("Requested chapter");
    expect(screen.getByText(/leaves the source’s download running/)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Cancel download" })).not.toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Stop import" }));
    await waitFor(() => expect(api.cancelPublicationTransfer).toHaveBeenCalledWith("1"));
  });

  it("shows a source failure and offers to check it again without claiming to restart it", async () => {
    api.fetchIntegrationTransfers.mockResolvedValue([transfer("1", { title: "Requested chapter", mode: INTEGRATION_TRANSFER_MODE.sourceRequest,
      phase: INTEGRATION_TRANSFER_PHASE.needsReview, sourceState: SOURCE_ACQUISITION_STATE.failed,
      sourceProblem: "Retry the chapter in its source app, then check again.", importedEntityIds: [], canCancel: true })]);
    render(RequestActivity, { connections: [connection] });
    await screen.findByText("Requested chapter");
    expect(screen.getByText("Retry the chapter in its source app, then check again.")).toBeInTheDocument();
    await fireEvent.click(screen.getByRole("button", { name: "Check again" }));
    await waitFor(() => expect(api.retryPublicationTransfer).toHaveBeenCalledWith("1"));
  });
});
