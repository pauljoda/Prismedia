import {
  commitManagedBookRequest,
  reviewManagedBookRequest,
} from "$lib/api/generated/prismedia";
import type {
  CommitManagedBookRequestInput,
  CommitManagedBookRequestResponse,
  ReviewManagedBookRequestInput,
  ReviewedManagedBookRequest,
} from "$lib/api/generated/model";
import { acceptManagedIntent } from "$lib/api/managed-acceptance";
import { unwrapGenerated } from "$lib/api/generated-response";
import { ManagedRequestRejectedError } from "$lib/api/managed-requests";

/** Reviews selected Book formats against one connected manager without saving work. */
export async function reviewManagedBook(
  connectionId: string,
  input: ReviewManagedBookRequestInput,
): Promise<ReviewedManagedBookRequest> {
  return unwrapGenerated(
    await reviewManagedBookRequest(connectionId, input),
    "Could not review the connected Book request",
  );
}

/** Accepts one Book work and its selected format intents with a stable retry identity. */
export async function saveManagedBook(
  connectionId: string,
  input: CommitManagedBookRequestInput,
): Promise<CommitManagedBookRequestResponse> {
  return acceptManagedIntent(
    commitManagedBookRequest(connectionId, input),
    (message, problemCode) => new ManagedRequestRejectedError(message, problemCode),
    "Could not confirm acceptance. Retry this same Book request to check.",
  );
}
