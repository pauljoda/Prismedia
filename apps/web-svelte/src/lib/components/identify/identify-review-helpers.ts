import {
  ENTITY_KIND,
  MEDIA_IMAGE_KIND,
  THUMBNAIL_HOVER_KIND,
  THUMBNAIL_META_ICON,
} from "$lib/api/generated/codes";
import type {
  CreditPatch,
  EntityMetadataProposal,
  ImageCandidate,
} from "$lib/api/identify-types";
import { proposalKindToEntityKind } from "$lib/entities/entity-codes";
import type { EntityCard } from "$lib/api/entities";
import type { EntityThumbnailCard, EntityThumbnailMetaIcon } from "$lib/entities/entity-thumbnail";
import { aspectRatioForKind, iconForKind } from "$lib/entities/entity-thumbnail";
import {
  reviewableImages,
  reviewImagePreviewUrl,
  isNewRelationshipTitle,
  relationshipTitlesForDetail,
  relationshipProposals,
  scopedCreditForProposal,
} from "$lib/components/identify-review";

export function roleLabel(credit: CreditPatch | null | undefined): string {
  const role = credit?.role?.trim();
  if (!role) return "Cast";
  return role.replaceAll("-", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export function proposalTitle(result: EntityMetadataProposal): string {
  return result.patch?.title?.trim() || result.targetKind;
}

export function relationshipKindLabel(kind: string): string {
  return kind.replaceAll("-", " ").replace(/\b\w/g, (letter) => letter.toUpperCase());
}

export function relationshipIcon(kind: string): EntityThumbnailMetaIcon {
  return iconForKind(kind);
}

export function proposalImageUrl(
  proposal: EntityMetadataProposal,
  kinds: string[],
): string | null {
  const images = reviewableImages(proposal.images ?? [], proposal.targetKind);
  for (const kind of kinds) {
    const image = images.find((candidate) => candidate.kind === kind);
    if (image) return reviewImagePreviewUrl(image, proposal.targetKind);
  }
  return images[0] ? reviewImagePreviewUrl(images[0], proposal.targetKind) : null;
}

export function preferredProposalImage(
  result: EntityMetadataProposal,
  selectedImages: Record<string, string | null>,
  rootProposalId: string,
  store: { getReviewImageSelections: (id: string) => Record<string, string | null> | null | undefined },
): ImageCandidate | null {
  const selected = selectedProposalImage(
    result,
    [MEDIA_IMAGE_KIND.poster, MEDIA_IMAGE_KIND.thumbnail, MEDIA_IMAGE_KIND.cover, MEDIA_IMAGE_KIND.logo],
    selectedImages,
    rootProposalId,
    store,
  );
  if (selected) return selected;
  const images = reviewableImages(result.images ?? [], result.targetKind);
  return images.find((image) => image.kind === MEDIA_IMAGE_KIND.poster) ??
    images.find((image) => image.kind === MEDIA_IMAGE_KIND.thumbnail) ??
    images[0] ??
    null;
}

export function preferredRelationshipImage(
  result: EntityMetadataProposal,
  selectedImages: Record<string, string | null>,
  rootProposalId: string,
  store: { getReviewImageSelections: (id: string) => Record<string, string | null> | null | undefined },
): ImageCandidate | null {
  const selected = selectedProposalImage(
    result,
    [MEDIA_IMAGE_KIND.poster, MEDIA_IMAGE_KIND.thumbnail, MEDIA_IMAGE_KIND.logo, MEDIA_IMAGE_KIND.cover],
    selectedImages,
    rootProposalId,
    store,
  );
  if (selected) return selected;
  return result.images.find((image) => image.kind === MEDIA_IMAGE_KIND.poster) ??
    result.images.find((image) => image.kind === MEDIA_IMAGE_KIND.thumbnail) ??
    result.images.find((image) => image.kind === MEDIA_IMAGE_KIND.logo) ??
    result.images[0] ??
    null;
}

export function selectedProposalImage(
  result: EntityMetadataProposal,
  kinds: string[],
  selectedImages: Record<string, string | null>,
  rootProposalId: string,
  store: { getReviewImageSelections: (id: string) => Record<string, string | null> | null | undefined },
): ImageCandidate | null {
  const images = reviewableImages(result.images ?? [], result.targetKind);
  const selections = result.proposalId === rootProposalId
    ? selectedImages
    : store.getReviewImageSelections(result.proposalId);
  if (!selections) return null;

  for (const kind of kinds) {
    const url = selections[kind];
    if (!url) continue;
    const image = images.find((candidate) => candidate.kind === kind && candidate.url === url);
    if (image) return image;
  }

  return null;
}

export function selectedProposalImageUrl(
  result: EntityMetadataProposal,
  kinds: string[],
  selectedImages: Record<string, string | null>,
  rootProposalId: string,
  store: { getReviewImageSelections: (id: string) => Record<string, string | null> | null | undefined },
): string | null {
  const selected = selectedProposalImage(result, kinds, selectedImages, rootProposalId, store);
  return selected ? reviewImagePreviewUrl(selected, result.targetKind) : null;
}

export function relationshipStatusLabel(
  result: EntityMetadataProposal,
  existingTitles: string[],
): string {
  if (result.targetEntityId) return "Merge";
  return isNewRelationshipTitle(proposalTitle(result), existingTitles) ? "New" : "Merge";
}

export function proposalStatusCustom(
  result: EntityMetadataProposal,
  existingTitles: string[],
): EntityThumbnailCard["custom"] {
  const label = relationshipStatusLabel(result, existingTitles);
  return { bottomLeft: { label, title: `${label} ${relationshipKindLabel(result.targetKind)}` } };
}

export function childStatusCustom(child: EntityMetadataProposal): EntityThumbnailCard["custom"] {
  const label = isLocalUnmatchedProposal(child) ? "No match" : "Matched";
  return { bottomLeft: { label, title: `${label} ${relationshipKindLabel(child.targetKind)}` } };
}

export function isLocalUnmatchedProposal(child: EntityMetadataProposal): boolean {
  return child.matchReason === "local-unmatched" || child.proposalId.startsWith("local-unmatched:");
}

export function relationshipCard(
  result: EntityMetadataProposal,
  existingTitles: string[],
  selectedImages: Record<string, string | null>,
  rootProposalId: string,
  store: { getReviewImageSelections: (id: string) => Record<string, string | null> | null | undefined },
): EntityThumbnailCard {
  const image = preferredRelationshipImage(result, selectedImages, rootProposalId, store);
  const title = proposalTitle(result);
  const entityKind = proposalKindToEntityKind(result.targetKind);
  return {
    entity: { id: result.proposalId, kind: entityKind, title, parentEntityId: null, sortOrder: null, capabilities: [], childrenByKind: [], relationships: [] },
    aspectRatio: aspectRatioForKind(entityKind),
    cover: image ? { src: reviewImagePreviewUrl(image, result.targetKind), alt: title } : null,
    hover: { kind: THUMBNAIL_HOVER_KIND.none },
    subtitle: relationshipKindLabel(result.targetKind),
    custom: proposalStatusCustom(result, existingTitles),
    meta: [{ icon: relationshipIcon(result.targetKind), label: relationshipKindLabel(result.targetKind) }],
  };
}

export function childMeta(child: EntityMetadataProposal): EntityThumbnailCard["meta"] {
  const meta: EntityThumbnailCard["meta"] = [];
  const positions = child.patch?.positions ?? {};
  const episode = positions.episode ?? positions.episodeNumber;
  const season = positions.season ?? positions.seasonNumber;
  // Track sort order is 0-based (track 1 → 0), so present it as a 1-based track number.
  const sortOrder = positions.sortOrder ?? positions.sort;
  const track = positions.track ?? positions.trackNumber ?? (sortOrder != null ? sortOrder + 1 : undefined);
  if (episode) {
    meta.push({ icon: THUMBNAIL_META_ICON.count, label: `E${String(episode).padStart(2, "0")}` });
  } else if (child.targetKind === ENTITY_KIND.audioTrack && track) {
    meta.push({ icon: THUMBNAIL_META_ICON.count, label: String(track).padStart(2, "0") });
  } else if (season) {
    meta.push({ icon: THUMBNAIL_META_ICON.count, label: `S${String(season).padStart(2, "0")}` });
  }
  return meta;
}

export function tagRelationshipForTitle(
  tag: string,
  relationships: EntityMetadataProposal[],
): EntityMetadataProposal | null {
  return relationships.find((relationship) =>
    relationship.targetKind === ENTITY_KIND.tag &&
    proposalTitle(relationship).localeCompare(tag, undefined, { sensitivity: "accent" }) === 0,
  ) ?? null;
}

export function creditCard(
  credit: EntityMetadataProposal,
  scope: EntityMetadataProposal,
  existingTitles: string[],
  selectedImages: Record<string, string | null>,
  rootProposalId: string,
  store: { getReviewImageSelections: (id: string) => Record<string, string | null> | null | undefined },
): EntityThumbnailCard {
  const scopedCredit = scopedCreditForProposal(scope, credit);
  const image = preferredProposalImage(credit, selectedImages, rootProposalId, store);
  return {
    entity: { id: credit.proposalId, kind: ENTITY_KIND.person, title: credit.patch?.title ?? "", parentEntityId: null, sortOrder: null, capabilities: [], childrenByKind: [], relationships: [] },
    aspectRatio: aspectRatioForKind(ENTITY_KIND.person),
    cover: image ? { src: reviewImagePreviewUrl(image, credit.targetKind), alt: credit.patch?.title ?? "" } : null,
    hover: { kind: THUMBNAIL_HOVER_KIND.none } as const,
    subtitle: scopedCredit?.character ? `as ${scopedCredit.character}` : roleLabel(scopedCredit),
    custom: proposalStatusCustom(credit, existingTitles),
    meta: [{ icon: THUMBNAIL_META_ICON.person, label: roleLabel(scopedCredit) }],
  };
}

export function childCard(
  child: EntityMetadataProposal,
  index: number,
  defaultLabel: string,
  aspectRatio: EntityThumbnailCard["aspectRatio"],
  selectedImages: Record<string, string | null>,
  rootProposalId: string,
  store: { getReviewImageSelections: (id: string) => Record<string, string | null> | null | undefined },
  localChild?: EntityCard | null,
): EntityThumbnailCard {
  const childImage = preferredProposalImage(child, selectedImages, rootProposalId, store);
  const localCover = localChild?.coverUrl;
  return {
    entity: { id: child.proposalId, kind: proposalKindToEntityKind(child.targetKind), title: child.patch?.title ?? `${defaultLabel} ${index + 1}`, parentEntityId: null, sortOrder: index, capabilities: [], childrenByKind: [], relationships: [] },
    aspectRatio,
    cover: childImage
      ? { src: reviewImagePreviewUrl(childImage, child.targetKind), alt: child.patch?.title ?? "" }
      : localCover ? { src: localCover, alt: localChild.title } : null,
    hover: { kind: THUMBNAIL_HOVER_KIND.none } as const,
    custom: childStatusCustom(child),
    meta: childMeta(child),
  };
}
