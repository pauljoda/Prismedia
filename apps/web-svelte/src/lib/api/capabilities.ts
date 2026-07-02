import type {
  EntityCapability,
  EntityCapabilityDescriptionCapability,
  EntityCapabilityFlagsCapability,
  EntityCapabilityImagesCapability,
  EntityCapabilityRatingCapability,
  EntityCapabilityTechnicalCapability,
} from "$lib/api/generated/model";

export type EntityCapabilityKind = EntityCapability["kind"];

export type EntityCapabilityFor<K extends EntityCapabilityKind> = Extract<
  EntityCapability,
  { kind: K }
>;

export function getCapability<K extends EntityCapabilityKind>(
  capabilities: EntityCapability[],
  kind: K,
): EntityCapabilityFor<K> | undefined {
  return capabilities.find((capability): capability is EntityCapabilityFor<K> => capability.kind === kind);
}

export function getRatingCapability(
  capabilities: EntityCapability[],
): EntityCapabilityRatingCapability | undefined {
  return getCapability(capabilities, "rating");
}

export function getImagesCapability(
  capabilities: EntityCapability[],
): EntityCapabilityImagesCapability | undefined {
  return getCapability(capabilities, "images");
}

export function getDescriptionCapability(
  capabilities: EntityCapability[],
): EntityCapabilityDescriptionCapability | undefined {
  return getCapability(capabilities, "description");
}

export function getTechnicalCapability(
  capabilities: EntityCapability[],
): EntityCapabilityTechnicalCapability | undefined {
  return getCapability(capabilities, "technical");
}

export function getFlagsCapability(
  capabilities: EntityCapability[],
): EntityCapabilityFlagsCapability | undefined {
  return getCapability(capabilities, "flags");
}

export function getRatingValue(capabilities: EntityCapability[]): number {
  const value = getRatingCapability(capabilities)?.value;
  return typeof value === "number" ? value : Number(value ?? 0);
}

export function getTags(capabilities: EntityCapability[]): string[] {
  return [];
}

export function getThumbnailUrl(capabilities: EntityCapability[]): string | null {
  return getImagesCapability(capabilities)?.thumbnailUrl ?? null;
}

export function getDescription(capabilities: EntityCapability[]): string | null {
  return getDescriptionCapability(capabilities)?.value ?? null;
}

export function isNsfw(capabilities: EntityCapability[]): boolean {
  return getFlagsCapability(capabilities)?.isNsfw === true;
}

/** True for a request-created Wanted placeholder: a library entity with metadata but no file yet. */
export function isWanted(capabilities: EntityCapability[]): boolean {
  return getFlagsCapability(capabilities)?.isWanted === true;
}

/**
 * The entity's first provider identity as a provider-qualified id ("provider:itemId"), or null when
 * the entity has none yet. This is what request commits and monitoring re-resolve the entity from.
 */
export function firstProviderQualifiedId(capabilities: EntityCapability[]): string | null {
  const links = getCapability(capabilities, "links");
  const first = links?.externalIds?.find((id) => id.provider && id.value);
  return first ? `${first.provider}:${first.value}` : null;
}

export function withRatingCapability(
  capabilities: EntityCapability[],
  value: number | null,
): EntityCapability[] {
  const hasRating = capabilities.some((capability) => capability.kind === "rating");
  if (!hasRating) {
    return [
      ...capabilities,
      { kind: "rating", value } as EntityCapabilityRatingCapability,
    ];
  }

  return capabilities.map((capability) =>
    capability.kind === "rating"
      ? {
          ...capability,
          value,
        }
      : capability,
  );
}

export function withFlagCapability(
  capabilities: EntityCapability[],
  flag: "isFavorite" | "isNsfw" | "isOrganized",
  value: boolean,
): EntityCapability[] {
  const hasFlags = capabilities.some((c) => c.kind === "flags");
  if (!hasFlags) {
    return [
      ...capabilities,
      { kind: "flags" as const, isFavorite: null, isNsfw: null, isOrganized: null, [flag]: value } as EntityCapabilityFlagsCapability,
    ];
  }
  return capabilities.map((capability) =>
    capability.kind === "flags"
      ? { ...capability, [flag]: value }
      : capability,
  );
}
