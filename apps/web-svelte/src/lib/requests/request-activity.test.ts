import { describe, expect, it } from "vitest";
import {
  ENTITY_KIND,
  INTEGRATION_TRANSFER_MODE,
  INTEGRATION_TRANSFER_PHASE,
  MANAGED_REQUEST_PHASE,
  MANAGED_TRACKING_STATUS,
} from "$lib/api/generated/codes";
import type { IntegrationTransferResponse, ManagedRequestResponse, ManagedTrackingResponse } from "$lib/api/generated/model";
import {
  managedRequestActivityGroup,
  managedTrackingActivityGroup,
  removeTrackedRequests,
  REQUEST_ACTIVITY_GROUP,
  transferActivityGroup,
} from "./request-activity";

const transfer = (values: Partial<IntegrationTransferResponse> = {}): IntegrationTransferResponse => ({
  id: "transfer", connectionId: "source", title: "Arrival", entityKind: ENTITY_KIND.movie, libraryRootId: "root",
  mode: INTEGRATION_TRANSFER_MODE.sourceDownload, phase: INTEGRATION_TRANSFER_PHASE.transferring,
  createdAt: "2026-09-17T12:00:00Z", updatedAt: "2026-09-17T12:00:00Z", artifactCount: 0,
  importedEntityIds: [], lastError: null, canCancel: true, cancellationRequested: false, ...values,
});
const request = (values: Partial<ManagedRequestResponse> = {}): ManagedRequestResponse => ({
  id: "request", connectionId: "source", entityId: "entity", libraryRootId: "root", title: "Dune",
  phase: MANAGED_REQUEST_PHASE.awaitingFiles, revision: 1, remoteId: "remote", monitored: true, search: true,
  reviewRequired: false, canCancel: true, createdAt: "2026-09-17T12:00:00Z", updatedAt: "2026-09-17T12:00:00Z",
  problem: null, ...values,
});
const holding = (values: Partial<ManagedTrackingResponse> = {}): ManagedTrackingResponse => ({
  id: "request", connectionId: "source", libraryRootId: "root", title: "Dune", status: MANAGED_TRACKING_STATUS.tracking,
  revision: 1, lastCheckedAt: "2026-09-17T12:00:00Z", problem: null, bindings: [], targets: [],
  item: { entityKind: ENTITY_KIND.movie, remoteId: "remote", expectedExternalIds: {} }, ...values,
});

describe("request activity presentation", () => {
  it("puts errors and explicit review states before ordinary active work", () => {
    expect(transferActivityGroup(transfer({ lastError: "Checksum mismatch" }))).toBe(REQUEST_ACTIVITY_GROUP.attention);
    expect(transferActivityGroup(transfer({ phase: INTEGRATION_TRANSFER_PHASE.failed }))).toBe(REQUEST_ACTIVITY_GROUP.attention);
    expect(managedRequestActivityGroup(request({ reviewRequired: true }))).toBe(REQUEST_ACTIVITY_GROUP.attention);
    expect(managedTrackingActivityGroup(holding({ status: MANAGED_TRACKING_STATUS.stale }))).toBe(REQUEST_ACTIVITY_GROUP.attention);
    expect(transferActivityGroup(transfer())).toBe(REQUEST_ACTIVITY_GROUP.progress);
  });

  it("keeps completed and released records in recent history", () => {
    expect(transferActivityGroup(transfer({ phase: INTEGRATION_TRANSFER_PHASE.completed }))).toBe(REQUEST_ACTIVITY_GROUP.recent);
    expect(managedRequestActivityGroup(request({ phase: MANAGED_REQUEST_PHASE.completed }))).toBe(REQUEST_ACTIVITY_GROUP.recent);
    expect(managedTrackingActivityGroup(holding({ status: MANAGED_TRACKING_STATUS.released, problem: "Old warning" }))).toBe(REQUEST_ACTIVITY_GROUP.recent);
    expect(transferActivityGroup(transfer({ phase: INTEGRATION_TRANSFER_PHASE.cancelled, lastError: "Old warning" }))).toBe(REQUEST_ACTIVITY_GROUP.recent);
    expect(managedTrackingActivityGroup(holding())).toBe(REQUEST_ACTIVITY_GROUP.following);
  });

  it("removes a manager request once its tracked holding represents the same operation", () => {
    expect(removeTrackedRequests([request(), request({ id: "other" })], [holding()]).map(item => item.id)).toEqual(["other"]);
  });
});
