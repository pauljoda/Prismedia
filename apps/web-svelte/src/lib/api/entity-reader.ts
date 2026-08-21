import {
  getEntityReaderManifest,
  getGetEntityReaderPageUrl,
} from "$lib/api/generated/prismedia";
import type { EntityReaderManifestResponse } from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import { apiPath } from "$lib/api/orval-fetch";

/** Loads the generic ordered-page manifest advertised by an Entity's page-sequence capability. */
export async function fetchEntityReaderManifest(
  entityId: string,
  options?: RequestOptions,
): Promise<EntityReaderManifestResponse> {
  return unwrapGenerated(
    await getEntityReaderManifest(entityId, requestInit(options)),
    `Failed to load reader manifest for ${entityId}`,
  );
}

/** Authenticated same-origin URL for one ordinal in an Entity reader manifest. */
export function entityReaderPageUrl(entityId: string, ordinal: number): string {
  return apiPath(getGetEntityReaderPageUrl(entityId, ordinal));
}
