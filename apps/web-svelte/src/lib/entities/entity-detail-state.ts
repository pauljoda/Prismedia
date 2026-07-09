import type { EntityCapability } from "$lib/api/generated/model";
import { getCapability, withFlagCapability, withRatingCapability } from "$lib/api/capabilities";
import { CAPABILITY_KIND } from "$lib/entities/entity-codes";

export type EntityFlagName = "isFavorite" | "isNsfw" | "isOrganized";

export interface EntityDetailStateTarget {
  id: string;
  capabilities: EntityCapability[];
}

export type EntityDetailStateSetter<T extends EntityDetailStateTarget> = (entity: T) => void;

export type EntityRatingPersist = (id: string, value: number | null) => Promise<unknown>;

export type EntityFlagPersist = (
  id: string,
  flags: { isFavorite?: boolean | null; isNsfw?: boolean | null; isOrganized?: boolean | null },
) => Promise<unknown>;

export function entityFlagValue(entity: EntityDetailStateTarget, flag: EntityFlagName): boolean {
  return getCapability(entity.capabilities, CAPABILITY_KIND.flags)?.[flag] === true;
}

export async function updateOptimisticEntityRating<T extends EntityDetailStateTarget>(
  entity: T,
  value: number | null,
  setEntity: EntityDetailStateSetter<T>,
  persist: EntityRatingPersist,
): Promise<void> {
  const previous = entity;
  setEntity({
    ...entity,
    capabilities: withRatingCapability(entity.capabilities, value),
  });

  try {
    await persist(entity.id, value);
  } catch {
    setEntity(previous);
  }
}

export async function toggleOptimisticEntityFlag<T extends EntityDetailStateTarget>(
  entity: T,
  flag: EntityFlagName,
  setEntity: EntityDetailStateSetter<T>,
  persist: EntityFlagPersist,
): Promise<void> {
  const previous = entity;
  const next = !entityFlagValue(entity, flag);
  setEntity({
    ...entity,
    capabilities: withFlagCapability(entity.capabilities, flag, next),
  });

  try {
    await persist(entity.id, { [flag]: next });
  } catch {
    setEntity(previous);
  }
}
