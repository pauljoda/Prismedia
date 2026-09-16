import { previewManagedControls, listManagedControls, createManagedControl, refreshManagedControl, cancelManagedControl, closeUnverifiedManagedControl } from "$lib/api/generated/prismedia";
import type { CreateManagedControlRequest, ManagedControlActionResponse, ManagedControlPreview } from "$lib/api/generated/model";
import { problemMessage, unwrapGenerated } from "$lib/api/generated-response";

/** The server definitely refused acceptance, so the draft may safely be edited. */
export class ManagerActionRejectedError extends Error {}
/** Reads fresh configuration for exactly the saved local associations. */
export const fetchControlPreview = (connectionId: string, holdingId: string): Promise<ManagedControlPreview> =>
  previewManagedControls(connectionId, holdingId).then(response => unwrapGenerated(response, "Could not read manager settings"));
/** Reads retained intent independently of upstream availability. */
export const fetchControlActions = (connectionId: string, holdingId: string): Promise<ManagedControlActionResponse[]> =>
  listManagedControls(connectionId, holdingId).then(response => unwrapGenerated(response, "Could not read manager actions"));
/** Uses the same operation ID after response loss; an accepted action never submits an uncertain remote search again. */
export async function saveControlAction(connectionId: string, holdingId: string, request: CreateManagedControlRequest): Promise<ManagedControlActionResponse> {
  const response = await createManagedControl(connectionId, holdingId, request);
  if ([400, 401, 403, 404, 409].includes(response.status)) throw new ManagerActionRejectedError(problemMessage(response.data) ?? "The manager action was not accepted");
  return unwrapGenerated(response, "Could not confirm whether the action was accepted. Retry this same action to check.", [202]);
}
/** Schedules observation of the saved command or uncertain settings. */
export const refreshControlAction = (connectionId: string, holdingId: string, id: string) =>
  refreshManagedControl(connectionId, holdingId, id).then(response => unwrapGenerated(response, "Could not refresh the action", [202]));
/** Cancels only an unsent stage at the reviewed revision. */
export const cancelControlAction = (connectionId: string, holdingId: string, action: ManagedControlActionResponse): Promise<ManagedControlActionResponse> =>
  cancelManagedControl(connectionId, holdingId, action.id, { expectedRevision: action.revision }).then(response => unwrapGenerated(response, "Could not cancel the unsent action"));
/** Explicitly acknowledges uncertainty; remote work and fulfillment ownership remain intact. */
export const closeControlAction = (connectionId: string, holdingId: string, action: ManagedControlActionResponse): Promise<ManagedControlActionResponse> =>
  closeUnverifiedManagedControl(connectionId, holdingId, action.id, { expectedRevision: action.revision }).then(response => unwrapGenerated(response, "Could not close the unresolved action"));
