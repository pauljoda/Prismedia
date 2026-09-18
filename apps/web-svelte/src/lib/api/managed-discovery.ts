import { prepareManagedDiscovery, reviewManagedDiscovery, searchManagedDiscovery } from "$lib/api/generated/prismedia";
import type { ManagedDiscoveryQuery, ManagedDiscoveryReviewRequest, ManagedDiscoveryReviewResponse, ManagedDiscoverySearchResponse, PrepareManagedDiscoveryRequest, PreparedWantedMovieResponse } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

/** Searches new titles through the selected manager without adding or requesting them. */
export const searchManagerTitles = (connectionId: string, query: ManagedDiscoveryQuery): Promise<ManagedDiscoverySearchResponse> =>
  searchManagedDiscovery(connectionId, query).then(response => unwrapGenerated(response, "Could not search this source"));

/** Resolves an exact manager identity into the shared metadata review. */
export const reviewManagerTitle = (connectionId: string, request: ManagedDiscoveryReviewRequest): Promise<ManagedDiscoveryReviewResponse> =>
  reviewManagedDiscovery(connectionId, request).then(response => unwrapGenerated(response, "Could not review this title"));

/** Saves a reviewed wanted movie; the separate manager request starts acquisition. */
export const prepareManagerTitle = (connectionId: string, request: PrepareManagedDiscoveryRequest): Promise<PreparedWantedMovieResponse> =>
  prepareManagedDiscovery(connectionId, request).then(response => unwrapGenerated(response, "Could not save the reviewed title"));
