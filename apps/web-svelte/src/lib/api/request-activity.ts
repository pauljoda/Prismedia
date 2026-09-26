import { listRequestActivity } from "$lib/api/generated/prismedia";
import type { ListRequestActivityParams, RequestActivityPage } from "$lib/api/generated/model";
import { unwrapGenerated } from "$lib/api/generated-response";

/** Reads one bounded page from Prismedia's retained local activity journals. */
export const fetchRequestActivity = (params: ListRequestActivityParams): Promise<RequestActivityPage> =>
  listRequestActivity(params).then(response => unwrapGenerated(response, "Could not load request activity"));
