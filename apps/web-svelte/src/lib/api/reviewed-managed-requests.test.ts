import { beforeEach, describe, expect, it, vi } from "vitest";
import { ENTITY_KIND, PROBLEM_CODE, REQUEST_MEDIA_KIND } from "$lib/api/generated/codes";
import type {
  CommitReviewedManagedRequestInput,
  ReviewManagedRequestInput,
  ReviewedManagedRequest,
  ReviewedManagedRequestCommitResponse,
} from "$lib/api/generated/model";
import {
  fetchReviewedManagedRequest,
  ManagedRequestRejectedError,
  saveReviewedManagedRequest,
} from "$lib/api/reviewed-managed-requests";

const generated = vi.hoisted(() => ({
  reviewManagedRequest: vi.fn(),
  commitReviewedManagedRequest: vi.fn(),
}));

vi.mock("$lib/api/generated/prismedia", () => generated);

const request = {
  kind: REQUEST_MEDIA_KIND.movie,
  pluginId: "metadata",
  rootExternalIdentity: { namespace: "tmdb", value: "12" },
  proposalRevision: "revision-one",
  selectedProposalIds: ["movie"],
};

const review: ReviewedManagedRequest = {
  connectionRevision: 4,
  managerDiscoveryRevision: null,
  request,
  title: "Reviewed movie",
  scopes: [{
    rendition: null,
    work: { entityKind: ENTITY_KIND.movie, externalIds: { tmdb: "12" } },
    mount: {
      id: "mount",
      connectionId: "connection",
      libraryRootId: "library",
      remoteRootId: "remote-root",
      remotePath: "/remote/movies",
      localPath: "/media/movies",
      label: "Movies",
    },
    options: { profiles: [{ id: "profile", label: "Balanced" }], roots: [] },
    existing: null,
    existingFulfillments: [],
  }],
};

describe("reviewed managed requests API", () => {
  beforeEach(() => vi.clearAllMocks());

  it("uses only the read-only review endpoint during preflight", async () => {
    const input: ReviewManagedRequestInput = {
      scopes: [{ libraryRootId: null }],
      request,
      managerDiscoveryRevision: null,
    };
    generated.reviewManagedRequest.mockResolvedValue({ data: review, status: 200, headers: new Headers() });

    await expect(fetchReviewedManagedRequest("connection", input)).resolves.toBe(review);

    expect(generated.reviewManagedRequest).toHaveBeenCalledWith("connection", input);
    expect(generated.commitReviewedManagedRequest).not.toHaveBeenCalled();
  });

  it("accepts the atomic reviewed commit response", async () => {
    const response: ReviewedManagedRequestCommitResponse = {
      entityId: "entity",
      scopes: [{ rendition: null, targetEntityIds: null, managedRequest: null }],
    };
    const input: CommitReviewedManagedRequestInput = {
      operationId: "operation",
      expectedConnectionRevision: review.connectionRevision,
      scopes: [{ libraryRootId: review.scopes[0]!.mount.libraryRootId }],
      profileId: "profile",
      monitored: false,
      search: true,
      request,
      managerDiscoveryRevision: null,
    };
    generated.commitReviewedManagedRequest.mockResolvedValue({ data: response, status: 202, headers: new Headers() });

    await expect(saveReviewedManagedRequest("connection", input)).resolves.toBe(response);
    expect(generated.commitReviewedManagedRequest).toHaveBeenCalledWith("connection", input);
  });

  it("preserves a typed problem code when the reviewed commit is definitely rejected", async () => {
    generated.commitReviewedManagedRequest.mockResolvedValue({
      data: { code: PROBLEM_CODE.requestProposalChanged, message: "Review changed" },
      status: 409,
      headers: new Headers(),
    });
    const input: CommitReviewedManagedRequestInput = {
      operationId: "operation",
      expectedConnectionRevision: review.connectionRevision,
      scopes: [{ libraryRootId: review.scopes[0]!.mount.libraryRootId }],
      profileId: "profile",
      monitored: false,
      search: true,
      request,
      managerDiscoveryRevision: null,
    };

    await expect(saveReviewedManagedRequest("connection", input)).rejects.toEqual(
      expect.objectContaining<Partial<ManagedRequestRejectedError>>({
        problemCode: PROBLEM_CODE.requestProposalChanged,
      }),
    );
  });
});
