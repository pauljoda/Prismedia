import {
  commitReviewedManagedRequest as commitReviewedManagedRequestRequest,
  reviewManagedRequest as reviewManagedRequestRequest,
} from "$lib/api/generated/prismedia";
import type {
  CommitReviewedManagedRequestInput,
  ReviewManagedRequestInput,
  ReviewedManagedRequest,
  ReviewedManagedRequestCommitResponse,
  ReviewedManagedRequestScope,
} from "$lib/api/generated/model";
import { acceptManagedIntent } from "$lib/api/managed-acceptance";
import { unwrapGenerated } from "$lib/api/generated-response";
import { ManagedRequestRejectedError } from "$lib/api/managed-requests";

/** A fully reviewed external-manager choice. The route adds the stable operation identity at commit time. */
export interface ManagedRequestChoice {
  connectionId: string;
  review: ReviewedManagedRequest;
  profileId: string;
  monitored: boolean;
  search: boolean;
}

/** The one scope of a work requested as a whole (movies, series, existing entities). */
export const reviewScope = (review: ReviewedManagedRequest): ReviewedManagedRequestScope => review.scopes[0]!;

/** Reads canonical metadata, current manager choices, and holding evidence without persisting intent. */
export const fetchReviewedManagedRequest = (
  connectionId: string,
  input: ReviewManagedRequestInput,
): Promise<ReviewedManagedRequest> =>
  reviewManagedRequestRequest(connectionId, input)
    .then((response) => unwrapGenerated(response, "Could not review the manager request"));

/** Atomically accepts the reviewed metadata and manager request while preserving uncertain acceptance. */
export async function saveReviewedManagedRequest(
  connectionId: string,
  input: CommitReviewedManagedRequestInput,
): Promise<ReviewedManagedRequestCommitResponse> {
  return acceptManagedIntent(
    commitReviewedManagedRequestRequest(connectionId, input),
    (message, problemCode) => new ManagedRequestRejectedError(message, problemCode),
    "Could not confirm acceptance. Retry this same request to check.",
  );
}

export { ManagedRequestRejectedError };
