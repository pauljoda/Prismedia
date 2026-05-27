import {
  getEntity,
  getEntityThumbnails,
  listEntities,
} from "$lib/api/generated/prismedia";
import type {
  EntityCard as GeneratedEntityCard,
  EntityGroup as GeneratedEntityGroup,
  EntityListResponse as GeneratedEntityListResponse,
  EntityThumbnail as GeneratedEntityThumbnail,
  EntityThumbnailBatchResponse as GeneratedEntityThumbnailBatchResponse,
  ListEntitiesParams,
} from "$lib/api/generated/model";

export type EntityCard = GeneratedEntityThumbnail;
export type EntityDetailCard = GeneratedEntityCard;
export type EntityCardFull = GeneratedEntityCard;
export type EntityChildGroup = GeneratedEntityGroup;
export type EntityRelationshipGroup = GeneratedEntityGroup;
export type EntityThumbnail = GeneratedEntityThumbnail;
export type EntityListResponse = GeneratedEntityListResponse;

export interface RequestOptions {
  signal?: AbortSignal;
}

function requestInit(options?: RequestOptions): RequestInit | undefined {
  return options?.signal ? { signal: options.signal } : undefined;
}

function problemMessage(data: unknown): string | null {
  if (data && typeof data === "object") {
    const record = data as Record<string, unknown>;
    if (typeof record.message === "string") return record.message;
    if (typeof record.error === "string") return record.error;
    if (typeof record.detail === "string") return record.detail;
    if (typeof record.title === "string") return record.title;
  }

  if (typeof data === "string" && data.trim()) return data;
  return null;
}

function unwrapGenerated<T>(
  response: { data: unknown; status: number },
  fallback: string,
  okStatuses: readonly number[] = [200],
): T {
  if (!okStatuses.includes(response.status)) {
    throw new Error(problemMessage(response.data) ?? fallback);
  }

  return response.data as T;
}

export function fetchEntities(
  params?: ListEntitiesParams,
  options?: RequestOptions,
): Promise<EntityListResponse> {
  return listEntities(params, requestInit(options)).then((r) =>
    unwrapGenerated(r, "Failed to list entities"),
  );
}

export async function fetchEntityThumbnails(
  ids: string[],
  options?: RequestOptions,
): Promise<EntityThumbnail[]> {
  const uniqueIds = [...new Set(ids.filter(Boolean))];
  if (uniqueIds.length === 0) return [];

  const response = await getEntityThumbnails({ ids: uniqueIds }, undefined, requestInit(options));
  return (response.data as GeneratedEntityThumbnailBatchResponse).items;
}

export function fetchEntity(id: string, options?: RequestOptions): Promise<EntityCardFull> {
  return getEntity(id, undefined, requestInit(options)).then((r) =>
    unwrapGenerated(r, `Failed to fetch entity ${id}`),
  );
}
