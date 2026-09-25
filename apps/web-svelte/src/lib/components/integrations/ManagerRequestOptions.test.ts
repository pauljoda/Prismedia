import { fireEvent, render, screen, waitFor } from "@testing-library/svelte";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  CONNECTION_STATUS,
  ENTITY_KIND,
  FULFILLMENT_OWNER_KIND,
  INTEGRATION_OPERATION,
  MANAGED_REQUEST_PHASE,
  PLUGIN_CAPABILITY,
  REQUEST_MEDIA_KIND,
} from "$lib/api/generated/codes";
import type {
  ConnectionResponse,
  ReviewManagedRequestInput,
  ReviewedManagedRequest,
  ReviewedManagedRequestScope,
  ReviewedRequestCommitRequest,
} from "$lib/api/generated/model";
import ManagerRequestOptions from "./ManagerRequestOptions.svelte";

const mocks = vi.hoisted(() => ({
  fetchConnections: vi.fn(),
  fetchLibraryMounts: vi.fn(),
  fetchAccessibleLibraryRoots: vi.fn(),
  fetchReviewedManagedRequest: vi.fn(),
}));

vi.mock("$lib/api/connections", () => ({ fetchConnections: mocks.fetchConnections }));
vi.mock("$lib/api/managed-libraries", () => ({ fetchLibraryMounts: mocks.fetchLibraryMounts }));
vi.mock("$lib/api/settings", () => ({ fetchAccessibleLibraryRoots: mocks.fetchAccessibleLibraryRoots }));
vi.mock("$lib/api/reviewed-managed-requests", () => ({
  fetchReviewedManagedRequest: mocks.fetchReviewedManagedRequest,
  reviewScope: (review: ReviewedManagedRequest) => review.scopes[0],
}));

const connection: ConnectionResponse = {
  id: "manager-one",
  pluginId: "fixture-manager",
  name: "Movie manager",
  baseUrl: "http://manager.test/",
  enabled: true,
  enabledCapabilities: [PLUGIN_CAPABILITY.externalManager, PLUGIN_CAPABILITY.connectedLibrary],
  settings: {},
  configuredSecretKeys: [],
  revision: 7,
  status: CONNECTION_STATUS.ready,
  remoteInstanceId: null,
  hasPersistentRemoteIdentity: false,
  lastCheckedAt: null,
  lastError: null,
  effectiveCapabilities: [
    {
      kind: PLUGIN_CAPABILITY.externalManager,
      operations: [
        INTEGRATION_OPERATION.lookupManaged,
        INTEGRATION_OPERATION.ensureManaged,
        INTEGRATION_OPERATION.requestManaged,
        INTEGRATION_OPERATION.configureManaged,
        INTEGRATION_OPERATION.reconcileManaged,
      ],
      entityKinds: [ENTITY_KIND.movie],
    },
    {
      kind: PLUGIN_CAPABILITY.connectedLibrary,
      operations: [INTEGRATION_OPERATION.getLibraryItem],
      entityKinds: [ENTITY_KIND.movie],
    },
  ],
};

const request = (value: string): ReviewedRequestCommitRequest => ({
  kind: REQUEST_MEDIA_KIND.movie,
  pluginId: "metadata",
  rootExternalIdentity: { namespace: "tmdb", value },
  proposalRevision: `revision-${value}`,
  selectedProposalIds: [value],
}) as ReviewedRequestCommitRequest;

function reviewed(
  input: ReviewedRequestCommitRequest,
  libraryRootId = "library-one",
  profiles = [{ id: "profile-one", label: "Balanced" }, { id: "profile-two", label: "Archive" }],
): ReviewedManagedRequest {
  return {
    connectionRevision: connection.revision,
    managerDiscoveryRevision: null,
    request: input,
    title: `Movie ${input.rootExternalIdentity.value}`,
    scopes: [{
      rendition: null,
      work: { entityKind: ENTITY_KIND.movie, externalIds: { tmdb: input.rootExternalIdentity.value } },
      mount: {
        id: `mount-${libraryRootId}`,
        connectionId: connection.id,
        libraryRootId,
        remoteRootId: `remote-${libraryRootId}`,
        remotePath: `/remote/${libraryRootId}`,
        localPath: `/media/${libraryRootId}`,
        label: libraryRootId,
      },
      options: { profiles, roots: [] },
      existing: null,
      existingFulfillments: [],
    }],
  };
}

/** A whole-work review with its one scope's evidence changed. */
function withScope(review: ReviewedManagedRequest, scope: Partial<ReviewedManagedRequestScope>): ReviewedManagedRequest {
  return { ...review, scopes: [{ ...review.scopes[0]!, ...scope }] };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((accept) => { resolve = accept; });
  return { promise, resolve };
}

describe("Manager request options", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.fetchConnections.mockResolvedValue([connection]);
    mocks.fetchLibraryMounts.mockResolvedValue([
      reviewed(request("1")).scopes[0]!.mount,
      reviewed(request("1"), "library-two").scopes[0]!.mount,
    ]);
    mocks.fetchAccessibleLibraryRoots.mockResolvedValue([
      { id: "library-one", label: "Movies", scanVideos: true, scanImages: false, scanAudio: false, scanBooks: false, isNsfw: false },
      { id: "library-two", label: "Cold storage", scanVideos: true, scanImages: false, scanAudio: false, scanBooks: false, isNsfw: false },
    ]);
    mocks.fetchReviewedManagedRequest.mockImplementation(
      (_connectionId: string, input: ReviewManagedRequestInput) =>
        Promise.resolve(reviewed(input.request!, input.scopes[0]?.libraryRootId ?? "library-one")),
    );
  });

  it("reviews a fixed manager origin without committing or requiring a prepare action", async () => {
    const onChange = vi.fn();
    render(ManagerRequestOptions, {
      entityKind: ENTITY_KIND.movie,
      fixedConnection: connection,
      request: request("1"),
      managerDiscoveryRevision: 12,
      onChange,
    });

    expect(onChange).toHaveBeenCalledWith(null, true, null);
    await screen.findByRole("button", { name: "Manager quality profile" });

    expect(mocks.fetchReviewedManagedRequest).toHaveBeenCalledWith(connection.id, {
      scopes: [{ libraryRootId: null }],
      request: request("1"),
      managerDiscoveryRevision: 12,
    });
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({
      connectionId: connection.id,
      profileId: "profile-one",
      monitored: false,
      search: true,
    }), true, null);
  });

  it("ignores a delayed response after the reviewed metadata changes", async () => {
    const first = deferred<ReviewedManagedRequest>();
    const second = deferred<ReviewedManagedRequest>();
    const requestOne = request("1");
    const requestTwo = request("2");
    mocks.fetchReviewedManagedRequest
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise)
      .mockImplementation((_connectionId: string, input: ReviewManagedRequestInput) =>
        Promise.resolve(reviewed(input.request!, input.scopes[0]?.libraryRootId ?? "library-one")));
    const onChange = vi.fn();
    const view = render(ManagerRequestOptions, {
      entityKind: ENTITY_KIND.movie,
      fixedConnection: connection,
      request: requestOne,
      onChange,
    });
    await waitFor(() => expect(mocks.fetchReviewedManagedRequest).toHaveBeenCalledTimes(1));

    await view.rerender({
      entityKind: ENTITY_KIND.movie,
      fixedConnection: connection,
      request: requestTwo,
      onChange,
    });
    await waitFor(() => expect(mocks.fetchReviewedManagedRequest).toHaveBeenCalledTimes(2));
    second.resolve(reviewed(requestTwo));
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ review: expect.objectContaining({ title: "Movie 2" }) }),
      true, null,
    ));

    first.resolve(withScope(reviewed(requestOne), {
      existingFulfillments: [{
        entityId: "stale-movie",
        targetEntityIds: null,
        ownerKind: FULFILLMENT_OWNER_KIND.externalManager,
        connectionId: connection.id,
        connectionName: connection.name,
        requestId: "stale-request",
        requestPhase: MANAGED_REQUEST_PHASE.awaitingFiles,
        hasLocalSource: false,
      }],
    }));
    await new Promise((resolve) => window.setTimeout(resolve, 0));
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ review: expect.objectContaining({ title: "Movie 2" }) }),
      true, null,
    );
    expect(screen.queryByText(/already requested/i)).not.toBeInTheDocument();
  });

  it("keeps repeated movie review openable when an existing request owns it", async () => {
    const onChange = vi.fn();
    mocks.fetchReviewedManagedRequest.mockResolvedValue(withScope(reviewed(request("1")), {
      existingFulfillments: [{
        entityId: "existing-movie",
        targetEntityIds: null,
        ownerKind: FULFILLMENT_OWNER_KIND.externalManager,
        connectionId: "other-manager",
        connectionName: "House Radarr",
        requestId: "active-request",
        requestPhase: MANAGED_REQUEST_PHASE.awaitingFiles,
        hasLocalSource: false,
      }],
    }));

    render(ManagerRequestOptions, {
      entityKind: ENTITY_KIND.movie,
      fixedConnection: connection,
      request: request("1"),
      onChange,
    });

    expect(await screen.findByText("Already requested through House Radarr")).toBeInTheDocument();
    expect(screen.getByText("Waiting for files")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Manager quality profile" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Manager library" })).not.toBeInTheDocument();
    expect(onChange).toHaveBeenLastCalledWith(null, true, expect.objectContaining({
      entityId: "existing-movie",
      partialSelection: false,
    }));
  });

  it("explains a partial series overlap so the selection can be changed", async () => {
    const onChange = vi.fn();
    mocks.fetchReviewedManagedRequest.mockResolvedValue(withScope(reviewed(request("series")), {
      work: {
        entityKind: ENTITY_KIND.videoSeries,
        externalIds: { tvdb: "100" },
        targets: [
          { entityKind: ENTITY_KIND.videoEpisode, externalIds: { tvdb: "101" }, seasonNumber: 1, episodeNumber: 1 },
          { entityKind: ENTITY_KIND.videoEpisode, externalIds: { tvdb: "102" }, seasonNumber: 1, episodeNumber: 2 },
        ],
      },
      existingFulfillments: [{
        entityId: "existing-series",
        targetEntityIds: ["existing-episode-one"],
        ownerKind: null,
        connectionId: null,
        connectionName: "Prismedia",
        requestId: "episode-request",
        requestPhase: MANAGED_REQUEST_PHASE.awaitingFiles,
        hasLocalSource: false,
      }],
    }));

    render(ManagerRequestOptions, {
      entityKind: ENTITY_KIND.videoSeries,
      fixedConnection: connection,
      request: request("series"),
      onChange,
    });

    expect(await screen.findByText("Some selected episodes are already requested or in your library")).toBeInTheDocument();
    expect(screen.getByText(/Change the episode selection to request the rest/)).toBeInTheDocument();
    expect(onChange).toHaveBeenLastCalledWith(null, true, expect.objectContaining({
      entityId: "existing-series",
      partialSelection: true,
    }));
  });

  it("submits only the reviewed append when selected episodes share the retained holding", async () => {
    const onChange = vi.fn();
    mocks.fetchReviewedManagedRequest.mockResolvedValue(withScope(reviewed(request("series")), {
      work: {
        entityKind: ENTITY_KIND.videoSeries,
        externalIds: { tvdb: "100" },
        targets: [
          { entityKind: ENTITY_KIND.videoEpisode, externalIds: {}, seasonNumber: 1, episodeNumber: 1 },
          { entityKind: ENTITY_KIND.videoEpisode, externalIds: {}, seasonNumber: 1, episodeNumber: 2 },
        ],
      },
      existingFulfillments: [{
        entityId: "existing-series",
        targetEntityIds: ["existing-episode-one"],
        ownerKind: FULFILLMENT_OWNER_KIND.externalManager,
        connectionId: connection.id,
        connectionName: connection.name,
        requestId: "holding-one",
        requestPhase: MANAGED_REQUEST_PHASE.completed,
        hasLocalSource: true,
      }],
      expansion: {
        holdingId: "holding-one",
        retainedTargetEntityIds: ["existing-episode-one"],
        selectedOwnedTargetCount: 1,
        newTargetCount: 1,
      },
    }));

    render(ManagerRequestOptions, {
      entityKind: ENTITY_KIND.videoSeries,
      fixedConnection: connection,
      request: request("series"),
      onChange,
    });

    expect(await screen.findByText("Request 1 more episode")).toBeInTheDocument();
    expect(screen.getByText(/Only the new selection will be searched/)).toBeInTheDocument();
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ review: expect.objectContaining({
        scopes: [expect.objectContaining({ expansion: expect.objectContaining({ holdingId: "holding-one", newTargetCount: 1 }) })],
      }) }),
      true,
      null,
    ));
  });

  it("re-runs read-only review when a definite rejection requests a refresh", async () => {
    const onChange = vi.fn();
    const view = render(ManagerRequestOptions, {
      entityKind: ENTITY_KIND.movie,
      fixedConnection: connection,
      request: request("1"),
      refreshToken: 0,
      onChange,
    });
    await screen.findByRole("button", { name: "Manager quality profile" });
    const previousCalls = mocks.fetchReviewedManagedRequest.mock.calls.length;

    await view.rerender({
      entityKind: ENTITY_KIND.movie,
      fixedConnection: connection,
      request: request("1"),
      refreshToken: 1,
      onChange,
    });

    await waitFor(() => expect(mocks.fetchReviewedManagedRequest.mock.calls.length).toBeGreaterThan(previousCalls));
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ connectionId: connection.id }),
      true, null,
    ));
  });

  it("publishes reviewed library and quality choices", async () => {
    const onChange = vi.fn();
    render(ManagerRequestOptions, {
      entityKind: ENTITY_KIND.movie,
      fixedConnection: connection,
      request: request("1"),
      onChange,
    });
    const profile = await screen.findByRole("button", { name: "Manager quality profile" });
    profile.focus();
    await fireEvent.keyDown(profile, { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: "Archive" }));
    expect(onChange).toHaveBeenLastCalledWith(expect.objectContaining({ profileId: "profile-two" }), true, null);

    const library = screen.getByRole("button", { name: "Manager library" });
    library.focus();
    await fireEvent.keyDown(library, { key: "ArrowDown" });
    await fireEvent.pointerUp(await screen.findByRole("option", { name: /Cold storage/ }));
    await waitFor(() => expect(mocks.fetchReviewedManagedRequest).toHaveBeenLastCalledWith(
      connection.id,
      expect.objectContaining({ scopes: [{ libraryRootId: "library-two" }] }),
    ));
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({
        review: expect.objectContaining({ scopes: [expect.objectContaining({ mount: expect.objectContaining({ libraryRootId: "library-two" }) })] }),
        profileId: "profile-two",
      }),
      true, null,
    ));
  });
});
