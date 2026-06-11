import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
import type {
  CreditPatch,
  EntityMetadataProposal,
  ImageCandidate,
} from "$lib/api/identify-types";
import { proposalKindToEntityKind } from "$lib/entities/entity-codes";
import type { EntityCard } from "$lib/api/entities";
import type { EntityThumbnailCard, EntityThumbnailMetaIcon } from "$lib/entities/entity-thumbnail";
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
  if (kind === "studio") return "studio";
  if (kind === "tag") return "tag";
  if (kind === "person") return "person";
  return "collection";
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
  const selected = selectedProposalImage(result, ["poster", "thumbnail", "cover", "logo"], selectedImages, rootProposalId, store);
  if (selected) return selected;
  const images = reviewableImages(result.images ?? [], result.targetKind);
  return images.find((image) => image.kind === "poster") ??
    images.find((image) => image.kind === "thumbnail") ??
    images[0] ??
    null;
}

export function preferredRelationshipImage(
  result: EntityMetadataProposal,
  selectedImages: Record<string, string | null>,
  rootProposalId: string,
  store: { getReviewImageSelections: (id: string) => Record<string, string | null> | null | undefined },
): ImageCandidate | null {
  const selected = selectedProposalImage(result, ["poster", "thumbnail", "logo", "cover"], selectedImages, rootProposalId, store);
  if (selected) return selected;
  return result.images.find((image) => image.kind === "poster") ??
    result.images.find((image) => image.kind === "thumbnail") ??
    result.images.find((image) => image.kind === "logo") ??
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
  const label = "Matched";
  return { bottomLeft: { label, title: `${label} ${relationshipKindLabel(child.targetKind)}` } };
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
  return {
    entity: { id: result.proposalId, kind: proposalKindToEntityKind(result.targetKind), title, parentEntityId: null, sortOrder: null, capabilities: [], childrenByKind: [], relationships: [] },
    aspectRatio: result.targetKind === "studio" ? "wide" : result.targetKind === "person" ? { width: 4, height: 5 } : "square",
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
    meta.push({ icon: "count", label: `E${String(episode).padStart(2, "0")}` });
  } else if (child.targetKind === "audio-track" && track) {
    meta.push({ icon: "count", label: String(track).padStart(2, "0") });
  } else if (season) {
    meta.push({ icon: "count", label: `S${String(season).padStart(2, "0")}` });
  }
  return meta;
}

export function tagRelationshipForTitle(
  tag: string,
  relationships: EntityMetadataProposal[],
): EntityMetadataProposal | null {
  return relationships.find((relationship) =>
    relationship.targetKind === "tag" &&
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
    entity: { id: credit.proposalId, kind: "person", title: credit.patch?.title ?? "", parentEntityId: null, sortOrder: null, capabilities: [], childrenByKind: [], relationships: [] },
    aspectRatio: { width: 4, height: 5 },
    cover: image ? { src: reviewImagePreviewUrl(image, credit.targetKind), alt: credit.patch?.title ?? "" } : null,
    hover: { kind: THUMBNAIL_HOVER_KIND.none } as const,
    subtitle: scopedCredit?.character ? `as ${scopedCredit.character}` : roleLabel(scopedCredit),
    custom: proposalStatusCustom(credit, existingTitles),
    meta: [{ icon: "person" as const, label: roleLabel(scopedCredit) }],
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
