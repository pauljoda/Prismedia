import { commitManagedComicRun, reviewManagedComicRun } from "$lib/api/generated/prismedia";
import type { CommitManagedComicRunInput, CommitManagedComicRunResponse, ReviewManagedComicRunInput, ReviewedManagedComicRun } from "$lib/api/generated/model";
import { acceptManagedIntent } from "$lib/api/managed-acceptance";
import { unwrapGenerated } from "$lib/api/generated-response";
import { ManagedRequestRejectedError } from "$lib/api/managed-requests";

/** Reviews one exact Comic Vine run and its mapped destinations without creating it. */
export const fetchManagedComicRunReview = (connectionId: string, input: ReviewManagedComicRunInput): Promise<ReviewedManagedComicRun> =>
  reviewManagedComicRun(connectionId, input).then(response => unwrapGenerated(response, "Could not review this comic run"));

/** Adds a reviewed run with monitoring and automatic search off. */
export const addManagedComicRun = (connectionId: string, input: CommitManagedComicRunInput): Promise<CommitManagedComicRunResponse> =>
  acceptManagedIntent(
    commitManagedComicRun(connectionId, input),
    (message, problemCode) => new ManagedRequestRejectedError(message, problemCode),
    "Could not confirm this comic run. Retry the same add to check its exact identity.",
  );
