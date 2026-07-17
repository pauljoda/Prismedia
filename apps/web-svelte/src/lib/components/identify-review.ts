import type {
  EntityCard,
  EntityGroup,
  EntityThumbnail,
} from "$lib/api/generated/model";
import { getCapability, getDescription, getImagesCapability } from "$lib/api/capabilities";
import type {
  CreditPatch,
  EntityMetadataPatch,
  EntityMetadataProposal,
  ImageCandidate,
} from "$lib/api/identify-types";
import {
  CAPABILITY_KIND,
  ENTITY_KIND,
  ENTITY_KINDS_ENUMERATING_IDENTIFY_CHILDREN,
  MEDIA_IMAGE_KIND,
  METADATA_PATCH_FIELD,
} from "$lib/entities/entity-codes";

/** Reviewable patch fields in display order ("flags" is applied, never reviewed). */
export const reviewFieldKeys = [
  METADATA_PATCH_FIELD.title,
  METADATA_PATCH_FIELD.description,
  METADATA_PATCH_FIELD.externalIds,
  METADATA_PATCH_FIELD.urls,
  METADATA_PATCH_FIELD.tags,
  METADATA_PATCH_FIELD.studio,
  METADATA_PATCH_FIELD.credits,
  METADATA_PATCH_FIELD.dates,
  METADATA_PATCH_FIELD.stats,
  METADATA_PATCH_FIELD.positions,
  METADATA_PATCH_FIELD.classification,
  METADATA_PATCH_FIELD.images,
] as const;

const fieldKeys = reviewFieldKeys;

export const reviewDetailedFieldKeys = [
  METADATA_PATCH_FIELD.tags,
  METADATA_PATCH_FIELD.studio,
  METADATA_PATCH_FIELD.credits,
  METADATA_PATCH_FIELD.images,
] as const;

export const reviewDiffFieldKeys = reviewFieldKeys.filter(
  (field) => !(reviewDetailedFieldKeys as readonly string[]).includes(field),
);

export const reviewFieldLabels: Record<string, string> = {
  title: "Title",
  description: "Description",
  externalIds: "Provider IDs",
  urls: "Links",
  tags: "Tags",
  studio: "Studio",
  credits: "Credits",
  dates: "Dates",
  stats: "Stats",
  positions: "Sort order",
  classification: "Classification",
  images: "Artwork",
};

export interface IdentifyReviewSelectionState {
  selectedFieldsByProposal: Record<string, Record<string, boolean>>;
  selectedImagesByProposal: Record<string, Record<string, string | null>>;
  selectedCreditsByProposal: Record<string, Record<string, boolean>>;
  selectedTagsByProposal: Record<string, Record<string, boolean>>;
  selectedCascade: Record<string, boolean>;
}

export interface IdentifyRelationshipTitles {
  tags: string[];
  credits: string[];
}

export interface IdentifyProposalRow {
  id: string;
  label: string;
  proposals: EntityMetadataProposal[];
}

export interface IdentifyRootReviewApplyInput {
  selectedFields: Record<string, boolean>;
  selectedImages: Record<string, string | null>;
  selectedTags?: Record<string, boolean>;
  selectedCredits?: Record<string, boolean>;
  selectedCascade?: Record<string, boolean>;
  selectedFieldsByProposal?: Record<string, Record<string, boolean>>;
  selectedImagesByProposal?: Record<string, Record<string, string | null>>;
  selectedTagsByProposal?: Record<string, Record<string, boolean>>;
}

export interface IdentifyRootReviewApplyPayload {
  proposal: EntityMetadataProposal;
  selectedFields: string[];
  selectedImages: Record<string, string>;
}

export function structuralChildProposals(result: EntityMetadataProposal): EntityMetadataProposal[] {
  const seen = new Set<string>();
  return (result.children ?? []).filter((child) => {
    if (isRelationshipKind(child.targetKind)) return false;
    if (seen.has(child.proposalId)) return false;
    seen.add(child.proposalId);
    return true;
  });
}

/**
 * Depth-first flatten of a proposal's structural (non-relationship) descendants. The cascade can
 * nest resolved children inside provider containers — a flat-scanned book's chapters arrive inside
 * the volume nodes the provider proposes — so any surface that matches local entities against the
 * proposal must search the whole subtree, never just the top level.
 */
export function structuralDescendantProposals(result: EntityMetadataProposal): EntityMetadataProposal[] {
  const out: EntityMetadataProposal[] = [];
  const seen = new Set<string>();
  const walk = (node: EntityMetadataProposal) => {
    for (const child of node.children ?? []) {
      if (isRelationshipKind(child.targetKind) || seen.has(child.proposalId)) continue;
      seen.add(child.proposalId);
      out.push(child);
      walk(child);
    }
  };
  walk(result);
  return out;
}

/**
 * Container nodes the provider proposes that have no local entity yet — the volumes (or seasons)
 * applying will create. Mirrors the backend materialization rule: only a container kind that
 * adopts at least one matched local descendant is ever created, so only those are surfaced.
 */
export function newStructuralContainerProposals(result: EntityMetadataProposal): EntityMetadataProposal[] {
  return structuralChildProposals(result).filter((child) =>
    !child.targetEntityId &&
    kindEnumeratesIdentifyChildren(child.targetKind) &&
    structuralDescendantProposals(child).some((node) => Boolean(node.targetEntityId)),
  );
}

/**
 * Local entity ids the proposal relocates into new containers — children matched inside a
 * container that applying will create (a flat-scanned chapter filed into its volume). The
 * review removes these from the flat children list and shows them inside their container,
 * so each child visibly "moves" as the cascade resolves it.
 */
export function adoptedLocalChildIds(result: EntityMetadataProposal): Set<string> {
  const adopted = new Set<string>();
  for (const container of newStructuralContainerProposals(result)) {
    for (const node of structuralDescendantProposals(container)) {
      if (node.targetEntityId) adopted.add(node.targetEntityId);
    }
  }
  return adopted;
}

export function relationshipProposals(result: EntityMetadataProposal): EntityMetadataProposal[] {
  const proposals = [
    ...(result.relationships ?? []),
    ...(result.children ?? []).filter((child) => isRelationshipKind(child.targetKind)),
  ];
  const seen = new Set<string>();
  return proposals.filter((child) => {
    if (seen.has(child.proposalId)) return false;
    seen.add(child.proposalId);
    return true;
  });
}

export function reviewChildProposals(result: EntityMetadataProposal): EntityMetadataProposal[] {
  return [
    ...structuralChildProposals(result),
    ...relationshipProposals(result),
  ];
}

export function groupProposalRows(proposals: EntityMetadataProposal[]): IdentifyProposalRow[] {
  const groups = new Map<string, EntityMetadataProposal[]>();
  for (const proposal of proposals) {
    const id = proposal.targetKind;
    groups.set(id, [...(groups.get(id) ?? []), proposal]);
  }

  return Array.from(groups, ([id, rows]) => ({
    id,
    label: entityKindLabel(id),
    proposals: rows,
  }));
}

export function findRelationshipImage(
  result: EntityMetadataProposal,
  targetKind: string,
  name: string,
): string | null {
  const child = relationshipProposals(result).find(
    (candidate) =>
      candidate.targetKind === targetKind &&
      (candidate.patch.title ?? "").localeCompare(name, undefined, { sensitivity: "accent" }) === 0,
  );
  if (!child?.images.length) return null;
  const preferred = child.images.find((img) => img.kind === MEDIA_IMAGE_KIND.poster) ??
    child.images.find((img) => img.kind === MEDIA_IMAGE_KIND.logo) ??
    child.images[0];
  return preferred?.url ?? null;
}

export function relationshipTitlesFromEntityThumbnails(
  entity: Pick<EntityCard, "relationships">,
  thumbnails: EntityThumbnail[],
): IdentifyRelationshipTitles {
  const byId = new Map(thumbnails.map((thumbnail) => [thumbnail.id, thumbnail.title]));
  return {
    tags: titlesForRelationship(entity, byId, ENTITY_KIND.tag),
    credits: titlesForRelationship(entity, byId, ENTITY_KIND.person),
  };
}

export function isNewRelationshipTitle(title: string, existingTitles: string[]): boolean {
  return !existingTitles.some((existing) => existing.localeCompare(title, undefined, { sensitivity: "accent" }) === 0);
}

export function reviewableImages(images: ImageCandidate[], targetKind?: string | null): ImageCandidate[] {
  const allowsLogo = targetKind?.toLowerCase() === ENTITY_KIND.studio;
  return images.filter((image) => allowsLogo || image.kind.toLowerCase() !== MEDIA_IMAGE_KIND.logo);
}

export function reviewImagePreviewUrl(image: ImageCandidate, targetKind?: string | null): string {
  return tmdbPreviewUrl(image.url, image.kind, targetKind)
    ?? googlePreviewUrl(image.url, image.kind)
    ?? image.url;
}

export function defaultImageSelectionForReview(result: EntityMetadataProposal): Record<string, string | null> {
  const selected: Record<string, string | null> = {};
  for (const image of reviewableImages(result.images ?? [], result.targetKind)) {
    selected[image.kind] ??= image.url;
  }
  return selected;
}

export function groupReviewImages(result: EntityMetadataProposal): Array<{ kind: string; images: ImageCandidate[] }> {
  const groups: Record<string, ImageCandidate[]> = {};
  const seenUrls: Record<string, Set<string>> = {};
  for (const image of reviewableImages(result.images ?? [], result.targetKind)) {
    // De-duplicate by URL within each kind: providers occasionally return the same
    // artwork twice in one kind, and the review grid keys its `{#each}` on the URL,
    // so a repeat would otherwise crash rendering with `each_key_duplicate`.
    const seen = seenUrls[image.kind] ?? (seenUrls[image.kind] = new Set());
    if (seen.has(image.url)) continue;
    seen.add(image.url);
    groups[image.kind] = [...(groups[image.kind] ?? []), image];
  }
  return Object.entries(groups).map(([kind, images]) => ({ kind, images }));
}

export function defaultFieldSelectionForReview(result: EntityMetadataProposal): Record<string, boolean> {
  return Object.fromEntries(fieldKeys.map((field) => [field, proposalHasField(result, field)]));
}

export function proposalHasField(result: EntityMetadataProposal, field: string): boolean {
  return proposalFieldValue(result, field).trim().length > 0;
}

export function proposalFieldValue(result: EntityMetadataProposal, field: string): string {
  const patch = result.patch;
  if (field === METADATA_PATCH_FIELD.title) return patch.title ?? "";
  if (field === METADATA_PATCH_FIELD.description) return patch.description ?? "";
  if (field === METADATA_PATCH_FIELD.externalIds) return entries(patch.externalIds).join(", ");
  if (field === METADATA_PATCH_FIELD.urls) return patch.urls.join(", ");
  if (field === METADATA_PATCH_FIELD.tags) return patch.tags.join(", ");
  if (field === METADATA_PATCH_FIELD.studio) return patch.studio ?? "";
  if (field === METADATA_PATCH_FIELD.credits) return patch.credits.map((credit) =>
    credit.character ? `${credit.name} as ${credit.character}` : credit.name,
  ).join(", ");
  if (field === METADATA_PATCH_FIELD.dates) return entries(patch.dates).join(", ");
  if (field === METADATA_PATCH_FIELD.stats) return entries(patch.stats).join(", ");
  if (field === METADATA_PATCH_FIELD.positions) return reviewPositionValue(patch.positions, result.targetKind);
  if (field === METADATA_PATCH_FIELD.classification) return patch.classification ?? "";
  if (field === METADATA_PATCH_FIELD.images) return groupReviewImages(result).map((group) => `${group.kind} (${group.images.length})`).join(", ");
  return "";
}

export function currentFieldValueForReview(
  entity: EntityThumbnail,
  detail: EntityCard | null | undefined,
  field: string,
): string {
  if (field === METADATA_PATCH_FIELD.title) return detail?.title ?? entity.title ?? "";
  if (!detail) return "";

  const capabilities = detail.capabilities ?? [];
  if (field === METADATA_PATCH_FIELD.description) return getDescription(capabilities) ?? "";
  if (field === METADATA_PATCH_FIELD.externalIds) {
    const links = getCapability(capabilities, CAPABILITY_KIND.links);
    return (links?.externalIds ?? []).map((externalId) => `${externalId.provider}: ${externalId.value}`).join(", ");
  }
  if (field === METADATA_PATCH_FIELD.urls) {
    const links = getCapability(capabilities, CAPABILITY_KIND.links);
    return (links?.urls ?? []).map((url) => url.value).join(", ");
  }
  if (field === METADATA_PATCH_FIELD.tags) return relationshipTitlesForDetail(detail, ENTITY_KIND.tag).join(", ");
  if (field === METADATA_PATCH_FIELD.studio) return relationshipTitlesForDetail(detail, ENTITY_KIND.studio)[0] ?? "";
  if (field === METADATA_PATCH_FIELD.credits) return relationshipTitlesForDetail(detail, ENTITY_KIND.person).join(", ");
  if (field === METADATA_PATCH_FIELD.dates) {
    const dates = getCapability(capabilities, CAPABILITY_KIND.dates);
    return (dates?.items ?? []).map((item) => `${item.code}: ${item.value}`).join(", ");
  }
  if (field === METADATA_PATCH_FIELD.stats) {
    const stats = getCapability(capabilities, CAPABILITY_KIND.stats);
    return (stats?.items ?? []).map((item) => `${item.code}: ${item.value}`).join(", ");
  }
  if (field === METADATA_PATCH_FIELD.positions) {
    const positions = getCapability(capabilities, CAPABILITY_KIND.position);
    return reviewPositionValue(
      Object.fromEntries((positions?.items ?? []).map((item) => [item.code, item.value])),
      detail.kind ?? entity.kind,
    );
  }
  if (field === METADATA_PATCH_FIELD.classification) {
    const classification = getCapability(capabilities, CAPABILITY_KIND.classification);
    return classification?.value ?? "";
  }
  if (field === METADATA_PATCH_FIELD.images) {
    const images = getImagesCapability(capabilities);
    return (images?.items ?? [])
      .filter((image) => image.kind !== "source")
      .map((image) => String(image.kind))
      .join(", ");
  }
  return "";
}

export function reviewPositionValue(
  positions: Record<string, number | string>,
  targetKind?: string | null,
): string {
  return Object.entries(positions)
    .map(([code, value]) => reviewPositionEntry(code, value, targetKind))
    .join(", ");
}

export function relationshipTitlesForDetail(detail: Pick<EntityCard, "relationships"> | null | undefined, kind: string): string[] {
  return (detail?.relationships ?? [])
    .filter((group) => group.kind === kind)
    .flatMap((group) => group.entities)
    .map((item) => item.title)
    .filter((title): title is string => Boolean(title));
}

export function scopedCreditForProposal(
  scope: EntityMetadataProposal,
  personProposal: EntityMetadataProposal,
): CreditPatch | null {
  const title = personProposal.patch.title ?? "";
  if (title) {
    const scoped = scope.patch.credits.find((credit) =>
      credit.name.localeCompare(title, undefined, { sensitivity: "accent" }) === 0,
    );
    if (scoped) return scoped;
  }

  return personProposal.patch.credits[0] ?? null;
}

export function buildRootReviewApplyPayload(
  result: EntityMetadataProposal,
  input: IdentifyRootReviewApplyInput,
): IdentifyRootReviewApplyPayload {
  const fields = Object.fromEntries(
    fieldKeys.map((field) => [field, input.selectedFields[field] === true]),
  );
  const selectedImages = selectedReviewImages(input.selectedImages);
  const selectedFieldsByProposal = {
    ...(input.selectedFieldsByProposal ?? {}),
    [result.proposalId]: fields,
  };
  const selectedImagesByProposal = {
    ...(input.selectedImagesByProposal ?? {}),
    [result.proposalId]: selectedImages,
  };
  const selectedTagsByProposal = {
    ...(input.selectedTagsByProposal ?? {}),
    [result.proposalId]: input.selectedTags ?? {},
  };
  const proposal = buildProposalForApply(result, {
    selectedFieldsByProposal,
    selectedImagesByProposal,
    selectedCreditsByProposal: { [result.proposalId]: input.selectedCredits ?? {} },
    selectedTagsByProposal,
    selectedCascade: input.selectedCascade ?? {},
  });

  return {
    proposal,
    selectedFields: Object.entries(fields)
      .filter(([, selected]) => selected)
      .map(([field]) => field),
    selectedImages,
  };
}

export function buildProposalForApply(
  result: EntityMetadataProposal,
  selections: IdentifyReviewSelectionState,
): EntityMetadataProposal {
  const fields = selections.selectedFieldsByProposal[result.proposalId] ??
    defaultFieldSelectionForReview(result);
  const selectedResultCredits = selections.selectedCreditsByProposal[result.proposalId] ?? {};
  const selectedResultTags = selections.selectedTagsByProposal[result.proposalId] ?? {};
  const credits = result.patch.credits
    .filter((credit, index) => selectedResultCredits[creditKey(credit, index)] !== false)
    .filter((credit) => !isDeselectedRelationshipTitle(result, ENTITY_KIND.person, credit.name, selections.selectedCascade));
  const tags = result.patch.tags
    .filter((tag) => selectedResultTags[tag] !== false)
    .filter((tag) => !isDeselectedRelationshipTitle(result, ENTITY_KIND.tag, tag, selections.selectedCascade));
  const patch = patchForSelectedFields(result, fields, credits, tags, selections.selectedCascade);

  return {
    ...result,
    patch,
    images: imagesForSelectedProposal(result, selections, fields),
    children: structuralChildProposals(result)
      .filter((child) => !isLocalUnmatchedProposal(child))
      .filter((child) => selections.selectedCascade[child.proposalId] !== false)
      .map((child) => buildProposalForApply(child, selections)),
    relationships: relationshipProposals(result)
      .filter((child) => selections.selectedCascade[child.proposalId] !== false)
      .filter((child) => shouldKeepRelationship(child, patch, fields))
      .map((child) => buildProposalForApply(child, selections)),
  };
}

function isLocalUnmatchedProposal(result: EntityMetadataProposal): boolean {
  return result.matchReason === "local-unmatched" || result.proposalId.startsWith("local-unmatched:");
}

function shouldKeepRelationship(
  child: EntityMetadataProposal,
  patch: EntityMetadataPatch,
  fields: Record<string, boolean>,
): boolean {
  if (child.targetKind === ENTITY_KIND.person) {
    if (!fields.credits) return false;
    const title = child.patch.title ?? "";
    return patch.credits.some((credit) => credit.name.localeCompare(title, undefined, { sensitivity: "accent" }) === 0);
  }

  if (child.targetKind === ENTITY_KIND.studio) {
    if (!fields.studio || !patch.studio) return false;
    return (child.patch.title ?? "").localeCompare(patch.studio, undefined, { sensitivity: "accent" }) === 0;
  }

  if (child.targetKind === ENTITY_KIND.tag) {
    return fields.tags !== false;
  }

  return true;
}

function patchForSelectedFields(
  result: EntityMetadataProposal,
  fields: Record<string, boolean>,
  credits: CreditPatch[],
  tags: string[],
  selectedCascade: Record<string, boolean>,
): EntityMetadataPatch {
  const patch = result.patch;
  const studio = isDeselectedRelationshipTitle(result, ENTITY_KIND.studio, patch.studio, selectedCascade) ? null : patch.studio;
  return {
    title: fields.title ? patch.title : null,
    description: fields.description ? patch.description : null,
    externalIds: fields.externalIds ? patch.externalIds : {},
    urls: fields.urls ? patch.urls : [],
    tags: fields.tags ? tags : [],
    studio: fields.studio ? studio : null,
    credits: fields.credits ? credits : [],
    dates: fields.dates ? patch.dates : {},
    stats: fields.stats ? patch.stats : {},
    positions: fields.positions ? patch.positions : {},
    classification: fields.classification ? patch.classification : null,
    flags: patch.flags ?? null,
  };
}

function isDeselectedRelationshipTitle(
  result: EntityMetadataProposal,
  targetKind: string,
  title: string | null | undefined,
  selectedCascade: Record<string, boolean>,
): boolean {
  if (!title) return false;
  return relationshipProposals(result).some((child) =>
    child.targetKind === targetKind &&
    selectedCascade[child.proposalId] === false &&
    (child.patch.title ?? "").localeCompare(title, undefined, { sensitivity: "accent" }) === 0,
  );
}

function imagesForSelectedProposal(
  result: EntityMetadataProposal,
  selections: IdentifyReviewSelectionState,
  fields: Record<string, boolean>,
): ImageCandidate[] {
  if (fields.images === false) return [];
  const selected = selections.selectedImagesByProposal[result.proposalId];
  if (!selected) return result.images;
  return result.images.filter((image) => selected[image.kind] === image.url);
}

function selectedReviewImages(selectedImages: Record<string, string | null>): Record<string, string> {
  const selected: Record<string, string> = {};
  for (const [kind, url] of Object.entries(selectedImages)) {
    if (!url || kind.toLowerCase() === MEDIA_IMAGE_KIND.logo) continue;
    selected[kind] = url;
  }
  return selected;
}

function tmdbPreviewUrl(url: string, kind: string, targetKind?: string | null): string | null {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return null;
  }

  if (parsed.hostname !== "image.tmdb.org") return null;
  const parts = parsed.pathname.split("/");
  const sizeIndex = parts.findIndex((part, index) => part === "p" && parts[index - 1] === "t") + 1;
  if (sizeIndex <= 0 || !parts[sizeIndex]) return null;

  parts[sizeIndex] = tmdbPreviewSize(kind, targetKind);
  parsed.pathname = parts.join("/");
  return parsed.toString();
}

/**
 * Right-sizes a googleusercontent image (YouTube cover art, channel logos) for review previews.
 * Providers hand back covers upsized to ~1000px (hundreds of KB to MB each); rendering ~200px grid
 * tiles by hotlinking dozens of those at once makes the browser drop some loads, leaving broken
 * thumbnails. googleusercontent encodes the requested size in a trailing `=wW-hH` / `=sN` segment, so
 * we rewrite it down to a preview size — the same approach `tmdbPreviewUrl` takes for tmdb. The
 * proposal's original full-resolution url is left untouched, so the artwork applied on accept is
 * unchanged; only the on-screen preview shrinks. Returns null for non-googleusercontent urls.
 */
function googlePreviewUrl(url: string, kind: string): string | null {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return null;
  }

  if (!parsed.hostname.endsWith(".googleusercontent.com")) return null;

  const size = kind.toLowerCase() === MEDIA_IMAGE_KIND.backdrop ? 720 : 360;
  // Size hints live in the last path segment as `=wW-hH(-flags)` or `=sN(-flags)`; preserve any flags.
  if (/=w\d+-h\d+/.test(url)) return url.replace(/=w\d+-h\d+/, `=w${size}-h${size}`);
  if (/=s\d+/.test(url)) return url.replace(/=s\d+/, `=s${size}`);
  return `${url}=w${size}-h${size}`;
}

function tmdbPreviewSize(kind: string, targetKind?: string | null): string {
  const normalized = kind.toLowerCase();
  if (targetKind?.toLowerCase() === ENTITY_KIND.person && normalized !== MEDIA_IMAGE_KIND.backdrop && normalized !== MEDIA_IMAGE_KIND.logo) return "w185";
  if (normalized === MEDIA_IMAGE_KIND.backdrop) return "w780";
  if (normalized === MEDIA_IMAGE_KIND.logo) return "w300";
  if (normalized === MEDIA_IMAGE_KIND.profile) return "w185";
  return "w342";
}

function entries(record: Record<string, string | number>): string[] {
  return Object.entries(record).map(([key, value]) => `${key}: ${value}`);
}

function reviewPositionEntry(code: string, value: number | string, targetKind?: string | null): string {
  const normalized = normalizePositionCodeForReview(code);
  const scope = structuralPositionScope(targetKind);
  const label = isSortOrderPosition(normalized, scope)
    ? "Sort order"
    : positionCodeLabel(normalized);
  return `${label}: ${positionValueLabel(normalized, value, scope)}`;
}

function normalizePositionCodeForReview(code: string): string {
  const normalized = code
    .trim()
    .replace(/([a-z0-9])([A-Z])/g, "$1-$2")
    .toLowerCase();

  if (normalized === "season-number") return "season";
  if (normalized === "episode-number") return "episode";
  if (normalized === "absolute-episode-number") return "absolute-episode";
  if (normalized === "volume-number") return "volume";
  if (normalized === "chapter-number") return "chapter";
  if (normalized === "page-number") return "page";
  if (normalized === "track-number") return "track";
  if (normalized === "sort-order") return "sort";
  return normalized;
}

function structuralPositionScope(targetKind?: string | null): "season" | "episode" | null {
  const normalized = targetKind?.toLowerCase() ?? "";
  if (normalized.includes("season")) return "season";
  if (normalized.includes("episode") || normalized === ENTITY_KIND.video) return "episode";
  return null;
}

function isSortOrderPosition(
  normalizedCode: string,
  scope: "season" | "episode" | null,
): boolean {
  if (normalizedCode === "sort") return true;
  if (scope === "season" && normalizedCode === "season") return true;
  if (scope === "episode" && (normalizedCode === "episode" || normalizedCode === "absolute-episode")) return true;
  return false;
}

function positionCodeLabel(normalizedCode: string): string {
  if (normalizedCode === "season") return "Season";
  if (normalizedCode === "episode" || normalizedCode === "absolute-episode") return "Episode";
  if (normalizedCode === "sort") return "Sort order";
  return normalizedCode
    .split("-")
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function positionValueLabel(
  normalizedCode: string,
  value: number | string,
  scope: "season" | "episode" | null,
): string {
  if (normalizedCode === "sort") {
    if (scope === "season") return `Season ${value}`;
    if (scope === "episode") return `Episode ${value}`;
    return String(value);
  }

  if (normalizedCode === "season") return `Season ${value}`;
  if (normalizedCode === "episode" || normalizedCode === "absolute-episode") return `Episode ${value}`;
  if (normalizedCode === "volume") return `Volume ${value}`;
  if (normalizedCode === "chapter") return `Chapter ${value}`;
  if (normalizedCode === "page") return `Page ${value}`;
  if (normalizedCode === "track") return `Track ${value}`;
  return String(value);
}

function titlesForRelationship(
  entity: Pick<EntityCard, "relationships">,
  byId: Map<string, string>,
  kind: string,
): string[] {
  return (entity.relationships ?? [])
    .filter((group) => group.kind === kind)
    .flatMap((group) => group.entities)
    .map((thumbnail) => byId.get(thumbnail.id) ?? thumbnail.title)
    .filter((title): title is string => Boolean(title));
}

export function isRelationshipKind(kind: string): boolean {
  const normalized = kind.toLowerCase();
  return normalized === ENTITY_KIND.person || normalized === ENTITY_KIND.studio || normalized === ENTITY_KIND.tag;
}

/** A local structural child of an entity that can be identified on its own (album, episode, track). */
export interface StructuralChildEntity {
  id: string;
  kind: string;
  title: string;
  coverUrl: string | null;
}

/**
 * Entity kinds that are identify <em>containers</em>: their local structural children are themselves
 * separately identifiable works, so the cascade walks into them. Projected from the backend
 * `EntityKindRegistry.EnumeratesIdentifyChildren` flag via codegen — never hand-mirrored.
 */
const IDENTIFY_CONTAINER_KINDS = new Set<string>(ENTITY_KINDS_ENUMERATING_IDENTIFY_CHILDREN);

/** Whether an entity of this kind enumerates separately-identifiable structural children. */
export function kindEnumeratesIdentifyChildren(kind: string | null | undefined): boolean {
  return kind != null && IDENTIFY_CONTAINER_KINDS.has(kind.toLowerCase());
}

/**
 * Enumerates an entity's local structural children from its detail `childrenByKind` groups
 * (excluding relationship kinds) so the review screen can identify each one. Returns nothing for
 * leaf-content parents (e.g. a movie), whose single media file is not a separate identify target.
 */
export function structuralChildEntities(
  parentKind: string | null | undefined,
  childrenByKind: EntityGroup[] | null | undefined,
): StructuralChildEntity[] {
  if (!kindEnumeratesIdentifyChildren(parentKind)) {
    return [];
  }

  return (childrenByKind ?? [])
    .filter((group) => !isRelationshipKind(group.kind))
    .flatMap((group) => group.entities)
    .map((thumbnail) => ({
      id: thumbnail.id,
      kind: thumbnail.kind,
      title: thumbnail.title,
      coverUrl: thumbnail.coverUrl ?? null,
    }));
}

export function entityKindLabel(kind: string): string {
  const normalized = kind.toLowerCase();
  if (normalized === "person") return "People";
  if (normalized === "studio") return "Studios";
  if (normalized === "tag") return "Tags";
  if (normalized.includes("episode")) return "Episodes";
  if (normalized.includes("season")) return "Seasons";
  if (normalized.includes("series")) return "Series";
  if (normalized.includes("chapter")) return "Chapters";
  if (normalized.includes("volume")) return "Volumes";
  return "Items";
}

function creditKey(credit: CreditPatch, index: number): string {
  return `${credit.role}:${credit.name}:${credit.character ?? ""}:${index}`;
}
