import { previewManagedRequest, listManagedRequests, createManagedRequest, refreshManagedRequest, cancelManagedRequest } from "$lib/api/generated/prismedia";
import type { CreateManagedRequestInput, ManagedRequestResponse, ManagedRequestPreview, PreviewManagedRequestInput } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";
import { acceptManagedIntent } from "$lib/api/managed-acceptance";

/** A definite acceptance refusal permits editing the request after another review. */
export class ManagedRequestRejectedError extends Error {
  constructor(message: string, public readonly problemCode?: string) {
    super(message);
    this.name = "ManagedRequestRejectedError";
  }
}
/** Reads exact work identity, existing holdings, and the selected library boundary. */
export const fetchManagedRequestPreview = (connectionId: string, input: PreviewManagedRequestInput): Promise<ManagedRequestPreview> =>
  previewManagedRequest(connectionId, input).then(response => unwrapGenerated(response, "Could not review the manager request"));
/** Reads retained intent independently of connection health. */
export const fetchManagedRequests = (connectionId: string): Promise<ManagedRequestResponse[]> =>
  listManagedRequests(connectionId).then(response => unwrapGenerated(response, "Could not read manager requests"));
/** Retains the same operation identity after response loss to reconcile acceptance safely. */
export async function saveManagedRequest(connectionId: string, input: CreateManagedRequestInput): Promise<ManagedRequestResponse> {
  return acceptManagedIntent(createManagedRequest(connectionId, input), (message, problemCode) => new ManagedRequestRejectedError(message, problemCode),
    "Could not confirm acceptance. Retry this same request to check.");
}
/** Queues observation without repeating an uncertain creation. */
export const refreshRequest = (connectionId: string, id: string) =>
  refreshManagedRequest(connectionId, id).then(response => unwrapGenerated(response, "Could not refresh this request", [202]));
/** Cancels only safely unsent or definitely rejected creation at the reviewed revision. */
export const cancelRequest = (connectionId: string, request: ManagedRequestResponse): Promise<ManagedRequestResponse> =>
  cancelManagedRequest(connectionId, request.id, { expectedRevision: request.revision }).then(response => unwrapGenerated(response, "Could not cancel this request"));
