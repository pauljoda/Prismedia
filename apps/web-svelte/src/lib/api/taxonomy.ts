import {
  createPerson,
  createStudio,
  createTag,
  deletePerson,
  deleteStudio,
  deleteTag,
} from "$lib/api/generated/prismedia";
import type { EntityCreateRequest } from "$lib/api/generated/model";
import { requestInit, unwrapGenerated, type RequestOptions } from "$lib/api/generated-response";
import { ENTITY_KIND } from "$lib/entities/entity-codes";

/** Entity kinds the user can create and delete by hand from the taxonomy grids. */
const MANAGEABLE_KINDS = [ENTITY_KIND.tag, ENTITY_KIND.person, ENTITY_KIND.studio] as const;

export type ManageableTaxonomyKind = (typeof MANAGEABLE_KINDS)[number];

/** Whether a kind code is a user-manageable taxonomy kind (tag, person, studio). */
export function isManageableTaxonomyKind(kind: string): kind is ManageableTaxonomyKind {
  return (MANAGEABLE_KINDS as readonly string[]).includes(kind);
}

type GeneratedResponse = { data: unknown; status: number };
type CreateFn = (request: EntityCreateRequest, options?: RequestInit) => Promise<GeneratedResponse>;
type DeleteFn = (id: string, options?: RequestInit) => Promise<GeneratedResponse>;

const creators: Record<ManageableTaxonomyKind, CreateFn> = {
  [ENTITY_KIND.tag]: createTag,
  [ENTITY_KIND.person]: createPerson,
  [ENTITY_KIND.studio]: createStudio,
};

const deleters: Record<ManageableTaxonomyKind, DeleteFn> = {
  [ENTITY_KIND.tag]: deleteTag,
  [ENTITY_KIND.person]: deletePerson,
  [ENTITY_KIND.studio]: deleteStudio,
};

/** Creates a taxonomy entity of the given kind and returns its new identifier. */
export async function createTaxonomyEntity(
  kind: ManageableTaxonomyKind,
  request: EntityCreateRequest,
  options?: RequestOptions,
): Promise<{ id: string }> {
  const response = await creators[kind](request, requestInit(options));
  return unwrapGenerated<{ id: string }>(response, "Failed to create entity", [201]);
}

/** Deletes a taxonomy entity of the given kind by identifier. */
export async function deleteTaxonomyEntity(
  kind: ManageableTaxonomyKind,
  id: string,
  options?: RequestOptions,
): Promise<void> {
  const response = await deleters[kind](id, requestInit(options));
  unwrapGenerated<void>(response, "Failed to delete entity", [204]);
}
