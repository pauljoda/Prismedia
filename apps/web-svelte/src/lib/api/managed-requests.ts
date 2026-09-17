import { previewManagedRequest, listManagedRequests, createManagedRequest, refreshManagedRequest, cancelManagedRequest } from "$lib/api/generated/prismedia";
import type { CreateManagedRequestInput, ManagedRequestResponse, ManagedRequestPreview, PreviewManagedRequestInput } from "$lib/api/generated/model";
import { problemMessage, unwrapGenerated } from "$lib/api/generated-response";

/** A definite acceptance refusal permits editing the request after another review. */
export class ManagedRequestRejectedError extends Error {}
/** Reads exact work identity, existing holdings, and the selected library boundary. */
export const fetchManagedRequestPreview = (connectionId: string, input: PreviewManagedRequestInput): Promise<ManagedRequestPreview> =>
  previewManagedRequest(connectionId, input).then(response => unwrapGenerated(response, "Could not review the manager request"));
/** Reads retained intent independently of connection health. */
export const fetchManagedRequests = (connectionId: string): Promise<ManagedRequestResponse[]> =>
  listManagedRequests(connectionId).then(response => unwrapGenerated(response, "Could not read manager requests"));
/** Retains the same operation identity after response loss to reconcile acceptance safely. */
export async function saveManagedRequest(connectionId: string, input: CreateManagedRequestInput): Promise<ManagedRequestResponse> {
  const response = await createManagedRequest(connectionId, input);
  if ([400, 401, 403, 404, 409].includes(response.status)) throw new ManagedRequestRejectedError(problemMessage(response.data) ?? "The request was not accepted");
  return unwrapGenerated(response, "Could not confirm acceptance. Retry this same request to check.", [202]);
}
/** Queues observation without repeating an uncertain creation. */
export const refreshRequest = (connectionId: string, id: string) =>
  refreshManagedRequest(connectionId, id).then(response => unwrapGenerated(response, "Could not refresh this request", [202]));
/** Cancels only safely unsent or definitely rejected creation at the reviewed revision. */
export const cancelRequest = (connectionId: string, request: ManagedRequestResponse): Promise<ManagedRequestResponse> =>
  cancelManagedRequest(connectionId, request.id, { expectedRevision: request.revision }).then(response => unwrapGenerated(response, "Could not cancel this request"));
