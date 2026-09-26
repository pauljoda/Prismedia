import { previewManagedRelease, releaseManagedHolding } from "$lib/api/generated/prismedia";
import type { ManagedReleasePreview, ManagedTrackingResponse, ReleaseManagedHoldingRequest } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";
import { acceptManagedIntent } from "$lib/api/managed-acceptance";

/** A definite refusal permits a fresh review; response loss must reuse the same operation. */
export class ManagedReleaseRejectedError extends Error {}
/** Inspects monitoring and complete remote activity without changing ownership. */
export const fetchReleasePreview = (connectionId: string, holdingId: string): Promise<ManagedReleasePreview> =>
  previewManagedRelease(connectionId, holdingId).then(response => unwrapGenerated(response, "Could not review ownership release"));
/** Accepts a durable handoff that reserves ownership until a fresh worker observation succeeds. */
export async function saveOwnershipRelease(connectionId: string, holdingId: string, request: ReleaseManagedHoldingRequest): Promise<ManagedTrackingResponse> {
  return acceptManagedIntent(releaseManagedHolding(connectionId, holdingId, request), message => new ManagedReleaseRejectedError(message),
    "Could not confirm acceptance. Retry this same handoff to check.");
}
