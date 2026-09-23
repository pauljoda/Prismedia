import {
  commitManagedComicIssue,
  reviewManagedComicIssue,
} from "$lib/api/generated/prismedia";
import type {
  CommitManagedComicIssueInput,
  CommitManagedComicIssueResponse,
  ReviewManagedComicIssueInput,
  ReviewedManagedComicIssue,
} from "$lib/api/generated/model";
import { acceptManagedIntent } from "$lib/api/managed-acceptance";
import { unwrapGenerated } from "$lib/api/generated-response";
import { ManagedRequestRejectedError } from "$lib/api/managed-requests";

/** Reads current run, issue, and mapped destination before any local or manager mutation. */
export const fetchManagedComicIssueReview = (
  connectionId: string,
  input: ReviewManagedComicIssueInput,
): Promise<ReviewedManagedComicIssue> =>
  reviewManagedComicIssue(connectionId, input)
    .then(response => unwrapGenerated(response, "Could not review this comic issue"));

/** Retains the operation ID after an uncertain response so retry checks the same accepted intent. */
export const saveManagedComicIssueRequest = (
  connectionId: string,
  input: CommitManagedComicIssueInput,
): Promise<CommitManagedComicIssueResponse> =>
  acceptManagedIntent(
    commitManagedComicIssue(connectionId, input),
    (message, problemCode) => new ManagedRequestRejectedError(message, problemCode),
    "Could not confirm acceptance. Retry this same issue request to check.",
  );
