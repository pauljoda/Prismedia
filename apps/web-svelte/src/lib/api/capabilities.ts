import type {
  EntityCapability,
  EntityCapabilityBookMetadataCapability,
  EntityCapabilityCollectionConfigurationCapability,
  EntityCapabilityCoverSelectionCapability,
  EntityCapabilityCreditsCapability,
  EntityCapabilityDescriptionCapability,
  EntityCapabilityEmbeddedAudioMetadataCapability,
  EntityCapabilityFlagsCapability,
  EntityCapabilityGalleryMetadataCapability,
  EntityCapabilityImagesCapability,
  EntityCapabilityPersonProfileCapability,
  EntityCapabilityProviderIdentityCapability,
  EntityCapabilityRatingCapability,
  EntityCapabilitySeriesMetadataCapability,
  EntityCapabilityTagPolicyCapability,
  EntityCapabilityTechnicalCapability,
  ExternalIdentity,
} from "$lib/api/generated/model";
import { BOOK_FORMAT, CAPABILITY_KIND, ENTITY_FILE_ROLE } from "$lib/api/generated/codes";

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
  return getCapability(capabilities, CAPABILITY_KIND.rating);
}

export function getImagesCapability(
  capabilities: EntityCapability[],
): EntityCapabilityImagesCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.images);
}

export function getDescriptionCapability(
  capabilities: EntityCapability[],
): EntityCapabilityDescriptionCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.description);
}

export function getTechnicalCapability(
  capabilities: EntityCapability[],
): EntityCapabilityTechnicalCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.technical);
}

/** Book-specific format and type data, carried by the canonical Entity document. */
export function getBookMetadataCapability(
  capabilities: EntityCapability[],
): EntityCapabilityBookMetadataCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.bookMetadata);
}

/** Collection curation and sharing configuration carried by the Entity document. */
export function getCollectionConfigurationCapability(
  capabilities: EntityCapability[],
): EntityCapabilityCollectionConfigurationCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.collectionConfiguration);
}

/** Explicit cover Entity selection, when the Entity kind supports one. */
export function getCoverSelectionCapability(
  capabilities: EntityCapability[],
): EntityCapabilityCoverSelectionCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.coverSelection);
}

/** Cast/crew role metadata carried alongside the canonical relationship graph. */
export function getCreditsCapability(
  capabilities: EntityCapability[],
): EntityCapabilityCreditsCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.credits);
}

/** Embedded artist and album metadata for an audio-track Entity. */
export function getEmbeddedAudioMetadataCapability(
  capabilities: EntityCapability[],
): EntityCapabilityEmbeddedAudioMetadataCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.embeddedAudioMetadata);
}

export function getGalleryMetadataCapability(
  capabilities: EntityCapability[],
): EntityCapabilityGalleryMetadataCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.galleryMetadata);
}

/** Person profile fields, kept out of the shared Entity root. */
export function getPersonProfileCapability(
  capabilities: EntityCapability[],
): EntityCapabilityPersonProfileCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.personProfile);
}

export function getSeriesMetadataCapability(
  capabilities: EntityCapability[],
): EntityCapabilitySeriesMetadataCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.seriesMetadata);
}

export function getTagPolicyCapability(
  capabilities: EntityCapability[],
): EntityCapabilityTagPolicyCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.tagPolicy);
}

export function getFlagsCapability(
  capabilities: EntityCapability[],
): EntityCapabilityFlagsCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.flags);
}

/** The authoritative plugin + persistent identity binding chosen for this Entity, when present. */
export function getProviderIdentityCapability(
  capabilities: EntityCapability[],
): EntityCapabilityProviderIdentityCapability | undefined {
  return getCapability(capabilities, CAPABILITY_KIND.providerIdentity);
}

/** Managed file actions explicitly supported by the Entity's backend kind descriptor. */
export function getFileManagementCapability(capabilities: EntityCapability[]) {
  return getCapability(capabilities, CAPABILITY_KIND.fileManagement);
}

/** True only when the backend projected the shared managed delete-files capability. */
export function canDeleteEntityFiles(capabilities: EntityCapability[]): boolean {
  return getFileManagementCapability(capabilities)?.canDeleteFiles === true;
}

export function getRatingValue(capabilities: EntityCapability[]): number {
  const value = getRatingCapability(capabilities)?.value;
  return typeof value === "number" ? value : Number(value ?? 0);
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

/** True when the Entity itself owns a source that can use the shared video playback routes. */
export function isPlayableVideo(capabilities: EntityCapability[]): boolean {
  return getCapability(capabilities, CAPABILITY_KIND.playableVideo) !== undefined;
}

/** True when the Entity owns a direct source file, excluding generated previews and cache assets. */
export function hasSourceMedia(capabilities: EntityCapability[]): boolean {
  const files = getCapability(capabilities, CAPABILITY_KIND.files);
  return files?.items.some((file) => file.role === ENTITY_FILE_ROLE.source) === true;
}

/** True only when an EPUB/PDF book owns the direct source file required by the reader. */
export function hasReadableBookFile(capabilities: EntityCapability[]): boolean {
  const format = getBookMetadataCapability(capabilities)?.format;
  return (format === BOOK_FORMAT.epub || format === BOOK_FORMAT.pdf) && hasSourceMedia(capabilities);
}

/**
 * Canonical external identities carried by the Entity's links capability. Values remain opaque and
 * are never joined into delimiter-sensitive strings.
 */
export function externalIdentities(capabilities: EntityCapability[]): ExternalIdentity[] {
  const links = getCapability(capabilities, CAPABILITY_KIND.links);
  return (links?.externalIds ?? [])
    .filter((identity) => identity.provider.trim().length > 0 && identity.value.length > 0)
    .map((identity) => ({ namespace: identity.provider, value: identity.value }));
}

/** The first canonical external identity carried by an Entity, or null when it has none. */
export function firstExternalIdentity(capabilities: EntityCapability[]): ExternalIdentity | null {
  return externalIdentities(capabilities)[0] ?? null;
}

export function withRatingCapability(
  capabilities: EntityCapability[],
  value: number | null,
): EntityCapability[] {
  const hasRating = capabilities.some((capability) => capability.kind === CAPABILITY_KIND.rating);
  if (!hasRating) {
    return [
      ...capabilities,
      { kind: CAPABILITY_KIND.rating, value } as EntityCapabilityRatingCapability,
    ];
  }

  return capabilities.map((capability) =>
    capability.kind === CAPABILITY_KIND.rating
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
  const hasFlags = capabilities.some((capability) => capability.kind === CAPABILITY_KIND.flags);
  if (!hasFlags) {
    return [
      ...capabilities,
      { kind: CAPABILITY_KIND.flags, isFavorite: null, isNsfw: null, isOrganized: null, [flag]: value } as EntityCapabilityFlagsCapability,
    ];
  }
  return capabilities.map((capability) =>
    capability.kind === CAPABILITY_KIND.flags
      ? { ...capability, [flag]: value }
      : capability,
  );
}
