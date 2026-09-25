import { listManagedRequests, refreshManagedRequest, cancelManagedRequest } from "$lib/api/generated/prismedia";
import type { ManagedRequestResponse } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

/** A definite acceptance refusal permits editing the request after another review. */
export class ManagedRequestRejectedError extends Error {
  constructor(message: string, public readonly problemCode?: string) {
    super(message);
    this.name = "ManagedRequestRejectedError";
  }
}
/** Reads retained intent independently of connection health. */
export const fetchManagedRequests = (connectionId: string): Promise<ManagedRequestResponse[]> =>
  listManagedRequests(connectionId).then(response => unwrapGenerated(response, "Could not read manager requests"));
/** Queues observation without repeating an uncertain creation. */
export const refreshRequest = (connectionId: string, id: string) =>
  refreshManagedRequest(connectionId, id).then(response => unwrapGenerated(response, "Could not refresh this request", [202]));
/** Cancels only safely unsent or definitely rejected creation at the reviewed revision. */
export const cancelRequest = (connectionId: string, request: ManagedRequestResponse): Promise<ManagedRequestResponse> =>
  cancelManagedRequest(connectionId, request.id, { expectedRevision: request.revision }).then(response => unwrapGenerated(response, "Could not cancel this request"));
