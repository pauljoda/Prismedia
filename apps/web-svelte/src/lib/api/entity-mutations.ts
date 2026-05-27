import {
  updateEntity as updateEntityRequest,
  updateEntityByKind,
  updateEntityFlags as updateEntityFlagsRequest,
  updateEntityRating as updateEntityRatingRequest,
} from "$lib/api/generated/prismedia";
import type {
  EntityCard,
  EntityFlagsUpdateRequest,
  EntityMetadataUpdateRequest as GeneratedEntityMetadataUpdateRequest,
} from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import { fetchApi, uploadFile } from "$lib/api/orval-fetch";
import type { EntityFileRoleCode } from "$lib/entities/entity-codes";

export interface EntityMetadataUpdateOptions extends RequestOptions {
  kind?: string | null;
}

export interface EntityMetadataPatch {
  title?: string | null;
  description?: string | null;
  externalIds: Record<string, string>;
  urls: string[];
  tags: string[];
  studio?: string | null;
  credits: unknown[];
  dates: Record<string, string>;
  stats: Record<string, number>;
  positions: Record<string, number>;
  classification?: string | null;
  rating?: number | null;
  flags?: {
    isFavorite?: boolean | null;
    isNsfw?: boolean | null;
    isOrganized?: boolean | null;
  } | null;
}

export interface EntityMetadataUpdateRequest {
  fields: string[];
  patch: EntityMetadataPatch;
}

export function updateEntityRating(
  id: string,
  value: number | null,
  options?: RequestOptions,
): Promise<EntityCard> {
  return updateEntityRatingRequest(id, { value }, requestInit(options)).then((response) =>
    unwrapGenerated<EntityCard>(response, `Failed to update rating for ${id}`),
  );
}

export function updateEntityFlags(
  id: string,
  flags: { isFavorite?: boolean | null; isNsfw?: boolean | null; isOrganized?: boolean | null },
  options?: RequestOptions,
): Promise<EntityCard> {
  const request: EntityFlagsUpdateRequest = {
    isFavorite: flags.isFavorite ?? null,
    isNsfw: flags.isNsfw ?? null,
    isOrganized: flags.isOrganized ?? null,
  };

  return updateEntityFlagsRequest(id, request, requestInit(options)).then((response) =>
    unwrapGenerated<EntityCard>(response, `Failed to update flags for ${id}`),
  );
}

export function updateEntityMetadata(
  id: string,
  request: EntityMetadataUpdateRequest,
  options?: EntityMetadataUpdateOptions,
): Promise<EntityCard> {
  const generatedRequest = request as GeneratedEntityMetadataUpdateRequest;
  const response = options?.kind
    ? updateEntityByKind(options.kind, id, generatedRequest, requestInit(options))
    : updateEntityRequest(id, generatedRequest, requestInit(options));

  return response.then((result) =>
    unwrapGenerated<EntityCard>(result, `Failed to update metadata for ${id}`),
  );
}

export function uploadEntityImageAsset(
  id: string,
  role: EntityFileRoleCode,
  file: File,
  options?: RequestOptions,
): Promise<EntityCard> {
  return uploadFile<EntityCard>(`/entities/${id}/images/${encodeURIComponent(role)}`, file, undefined, {
    signal: options?.signal,
  });
}

export function clearEntityImageAsset(
  id: string,
  role: EntityFileRoleCode,
  options?: RequestOptions,
): Promise<EntityCard> {
  return fetchApi<EntityCard>(`/entities/${id}/images/${encodeURIComponent(role)}`, {
    method: "DELETE",
    signal: options?.signal,
  });
}
