import { ENTITY_KIND } from "$lib/api/generated/codes";
import type { ConnectionResponse } from "$lib/api/generated/model";
import { connectionSupports } from "$lib/integrations/connection-features";

/** Reports whether a ready connection can review and track a Book in its mapped library. */
export function supportsBookManager(connection: ConnectionResponse): boolean {
  return connectionSupports(connection, "bookManager", ENTITY_KIND.book);
}
