import type {
  EntityCard,
  EntityThumbnail,
} from "$lib/api/generated/model";
import { getCapability, getDescription, getImagesCapability } from "$lib/api/capabilities";
import type {
  CreditPatch,
  EntityMetadataPatch,
  EntityMetadataProposal,
  ImageCandidate,
} from "$lib/api/identify";
import { CAPABILITY_KIND } from "$lib/entities/entity-codes";

export const reviewFieldKeys = [
  "title",
  "description",
  "externalIds",
  "urls",
  "tags",
  "studio",
  "credits",
  "dates",
  "stats",
  "positions",
  "classification",
  "images",
] as const;

const fieldKeys = reviewFieldKeys;

export const reviewDetailedFieldKeys = [
  "tags",
  "studio",
  "credits",
  "images",
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
  return (result.children ?? []).filter((child) => !isRelationshipKind(child.targetKind));
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
  const preferred = child.images.find((img) => img.kind === "poster") ??
    child.images.find((img) => img.kind === "logo") ??
    child.images[0];
  return preferred?.url ?? null;
}

export function relationshipTitlesFromEntityThumbnails(
  entity: Pick<EntityCard, "relationships">,
  thumbnails: EntityThumbnail[],
): IdentifyRelationshipTitles {
  const byId = new Map(thumbnails.map((thumbnail) => [thumbnail.id, thumbnail.title]));
  return {
    tags: titlesForRelationship(entity, byId, "tag"),
    credits: titlesForRelationship(entity, byId, "person"),
  };
}

export function isNewRelationshipTitle(title: string, existingTitles: string[]): boolean {
  return !existingTitles.some((existing) => existing.localeCompare(title, undefined, { sensitivity: "accent" }) === 0);
}

export function reviewableImages(images: ImageCandidate[], targetKind?: string | null): ImageCandidate[] {
  const allowsLogo = targetKind?.toLowerCase() === "studio";
  return images.filter((image) => allowsLogo || image.kind.toLowerCase() !== "logo");
}

export function reviewImagePreviewUrl(image: ImageCandidate, targetKind?: string | null): string {
  return tmdbPreviewUrl(image.url, image.kind, targetKind) ?? image.url;
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
  for (const image of reviewableImages(result.images ?? [], result.targetKind)) {
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
  if (field === "title") return patch.title ?? "";
  if (field === "description") return patch.description ?? "";
  if (field === "externalIds") return entries(patch.externalIds).join(", ");
  if (field === "urls") return patch.urls.join(", ");
  if (field === "tags") return patch.tags.join(", ");
  if (field === "studio") return patch.studio ?? "";
  if (field === "credits") return patch.credits.map((credit) =>
    credit.character ? `${credit.name} as ${credit.character}` : credit.name,
  ).join(", ");
  if (field === "dates") return entries(patch.dates).join(", ");
  if (field === "stats") return entries(patch.stats).join(", ");
  if (field === "positions") return reviewPositionValue(patch.positions, result.targetKind);
  if (field === "classification") return patch.classification ?? "";
  if (field === "images") return groupReviewImages(result).map((group) => `${group.kind} (${group.images.length})`).join(", ");
  return "";
}

export function currentFieldValueForReview(
  entity: EntityThumbnail,
  detail: EntityCard | null | undefined,
  field: string,
): string {
  if (field === "title") return detail?.title ?? entity.title ?? "";
  if (!detail) return "";

  const capabilities = detail.capabilities ?? [];
  if (field === "description") return getDescription(capabilities) ?? "";
  if (field === "externalIds") {
    const links = getCapability(capabilities, CAPABILITY_KIND.links);
    return (links?.externalIds ?? []).map((externalId) => `${externalId.provider}: ${externalId.value}`).join(", ");
  }
  if (field === "urls") {
    const links = getCapability(capabilities, CAPABILITY_KIND.links);
    return (links?.urls ?? []).map((url) => url.value).join(", ");
  }
  if (field === "tags") return relationshipTitlesForDetail(detail, "tag").join(", ");
  if (field === "studio") return relationshipTitlesForDetail(detail, "studio")[0] ?? "";
  if (field === "credits") return relationshipTitlesForDetail(detail, "person").join(", ");
  if (field === "dates") {
    const dates = getCapability(capabilities, CAPABILITY_KIND.dates);
    return (dates?.items ?? []).map((item) => `${item.code}: ${item.value}`).join(", ");
  }
  if (field === "stats") {
    const stats = getCapability(capabilities, CAPABILITY_KIND.stats);
    return (stats?.items ?? []).map((item) => `${item.code}: ${item.value}`).join(", ");
  }
  if (field === "positions") {
    const positions = getCapability(capabilities, CAPABILITY_KIND.position);
    return reviewPositionValue(
      Object.fromEntries((positions?.items ?? []).map((item) => [item.code, item.value])),
      detail.kind ?? entity.kind,
    );
  }
  if (field === "classification") {
    const classification = getCapability(capabilities, CAPABILITY_KIND.classification);
    return classification?.value ?? "";
  }
  if (field === "images") {
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
    .filter((credit) => !isDeselectedRelationshipTitle(result, "person", credit.name, selections.selectedCascade));
  const tags = result.patch.tags
    .filter((tag) => selectedResultTags[tag] !== false)
    .filter((tag) => !isDeselectedRelationshipTitle(result, "tag", tag, selections.selectedCascade));
  const patch = patchForSelectedFields(result, fields, credits, tags, selections.selectedCascade);

  return {
    ...result,
    patch,
    images: imagesForSelectedProposal(result, selections, fields),
    children: structuralChildProposals(result)
      .filter((child) => selections.selectedCascade[child.proposalId] !== false)
      .map((child) => buildProposalForApply(child, selections)),
    relationships: relationshipProposals(result)
      .filter((child) => selections.selectedCascade[child.proposalId] !== false)
      .filter((child) => shouldKeepRelationship(child, patch, fields))
      .map((child) => buildProposalForApply(child, selections)),
  };
}

function shouldKeepRelationship(
  child: EntityMetadataProposal,
  patch: EntityMetadataPatch,
  fields: Record<string, boolean>,
): boolean {
  if (child.targetKind === "person") {
    if (!fields.credits) return false;
    const title = child.patch.title ?? "";
    return patch.credits.some((credit) => credit.name.localeCompare(title, undefined, { sensitivity: "accent" }) === 0);
  }

  if (child.targetKind === "studio") {
    if (!fields.studio || !patch.studio) return false;
    return (child.patch.title ?? "").localeCompare(patch.studio, undefined, { sensitivity: "accent" }) === 0;
  }

  if (child.targetKind === "tag") {
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
  const studio = isDeselectedRelationshipTitle(result, "studio", patch.studio, selectedCascade) ? null : patch.studio;
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
    if (!url || kind.toLowerCase() === "logo") continue;
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

function tmdbPreviewSize(kind: string, targetKind?: string | null): string {
  const normalized = kind.toLowerCase();
  if (targetKind?.toLowerCase() === "person" && normalized !== "backdrop" && normalized !== "logo") return "w185";
  if (normalized === "backdrop") return "w780";
  if (normalized === "logo") return "w300";
  if (normalized === "profile") return "w185";
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
  if (normalized.includes("episode") || normalized === "video") return "episode";
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

function isRelationshipKind(kind: string): boolean {
  const normalized = kind.toLowerCase();
  return normalized === "person" || normalized === "studio" || normalized === "tag";
}

function entityKindLabel(kind: string): string {
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
