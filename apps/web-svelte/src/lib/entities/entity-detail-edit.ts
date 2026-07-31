import type { KeyValuePair } from "$lib/components/forms";
import type { EntityPickerItem } from "$lib/components/forms/EntityPicker.svelte";
import { CREDIT_ROLE } from "$lib/entities/entity-codes";
import type { EntityDetailCard, EntityDetailCardFull } from "$lib/entities/entity-detail";

/**
 * One credited person in the edit draft. Saves use full-replace semantics, so the draft
 * carries every role and character linked to the person — not just the primary pair —
 * and re-emits them all when building the patch.
 */
export interface EntityCreditDraft {
  name: string;
  thumbnailUrl: string | null;
  /** Distinct credit role codes, primary first. Empty falls back to the generic person role. */
  roles: string[];
  /** Primary character, editable in the credits editor. Empty means no character. */
  character: string;
  /** Additional characters preserved through the round-trip but not surfaced for editing. */
  extraCharacters: string[];
}

export interface EntityDetailEditDraft {
  title: string;
  description: string;
  externalIds: KeyValuePair[];
  links: string[];
  tagPicks: EntityPickerItem[];
  studioPick: EntityPickerItem[];
  credits: EntityCreditDraft[];
  dates: KeyValuePair[];
  stats: KeyValuePair[];
  positions: KeyValuePair[];
  classification: string;
  ratingText: string;
  isFavorite: boolean;
  isNsfw: boolean;
  isOrganized: boolean;
}

export interface EntityMetadataPatch {
  title?: string | null;
  description?: string | null;
  externalIds: Record<string, string>;
  urls: string[];
  tags: string[];
  studio?: string | null;
  credits: Array<{ name: string; role: string; character?: string | null; sortOrder?: number | null }>;
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

/** Creates the stable empty shape used before an entity edit session begins. */
export function createEmptyEntityDetailEditDraft(): EntityDetailEditDraft {
  return {
    title: "",
    description: "",
    externalIds: [],
    links: [],
    tagPicks: [],
    studioPick: [],
    credits: [],
    dates: [],
    stats: [],
    positions: [],
    classification: "",
    ratingText: "",
    isFavorite: false,
    isNsfw: false,
    isOrganized: false,
  };
}

export interface EditableSection {
  id: string;
  editable?: boolean;
  hidden?: boolean;
}

export function hasProvider(link: EntityDetailCard["links"][number]): link is EntityDetailCard["links"][number] & { provider: string } {
  return Boolean(link.provider);
}

export function externalIdValue(label: string, provider: string): string {
  const prefix = `${provider}:`;
  return label.startsWith(prefix) ? label.slice(prefix.length).trim() : label;
}

export function draftFromCard(
  card: EntityDetailCard,
  flags: { isFavorite: boolean; isNsfw: boolean; isOrganized: boolean },
): EntityDetailEditDraft {
  const cardFull = card as EntityDetailCard & Partial<EntityDetailCardFull>;
  return {
    title: card.entity.title,
    description: card.description ?? "",
    externalIds: card.links
      .filter(hasProvider)
      .map((link) => ({ key: link.provider!, value: externalIdValue(link.label, link.provider!) })),
    links: card.links
      .filter((link) => !link.provider && link.url)
      .map((link) => link.url!),
    tagPicks: card.tags.map((tag) => ({
      id: tag.id,
      title: tag.title,
      thumbnailUrl: null,
    })),
    studioPick: cardFull.studio
      ? [{ id: cardFull.studio.id, title: cardFull.studio.title, thumbnailUrl: cardFull.studio.thumbnail }]
      : [],
    credits: (cardFull.credits ?? []).map((c) => ({
      name: c.title,
      thumbnailUrl: c.thumbnail,
      roles: [...(c.roles ?? [])],
      character: c.characters?.[0] ?? "",
      extraCharacters: c.characters?.slice(1) ?? [],
    })),
    dates: "dates" in card
      ? ((card as EntityDetailCard & { dates?: Array<{ code: string; value: string }> }).dates ?? []).map((d) => ({ key: d.code, value: d.value }))
      : [],
    stats: (cardFull.stats ?? []).map((s) => ({ key: s.code, value: String(s.value) })),
    positions: (cardFull.positions ?? []).map((p) => ({ key: p.code, value: String(p.value) })),
    classification: cardFull.classification?.value ?? "",
    ratingText: card.rating?.value == null ? "" : String(card.rating.value),
    isFavorite: flags.isFavorite,
    isNsfw: flags.isNsfw,
    isOrganized: flags.isOrganized,
  };
}

export function serializeDraft(draft: EntityDetailEditDraft | null): string {
  return JSON.stringify(draft);
}

export function validateUrl(url: string): string | null {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? null : "Must be http or https";
  } catch {
    return "Invalid URL";
  }
}

export function validateDraft(activeSections: EditableSection[], draft: EntityDetailEditDraft, ratingMax: number): string[] {
  const errors: string[] = [];
  const hasSection = (sectionId: string) => activeSections.some((section) => section.id === sectionId);

  if (hasSection("links")) {
    const invalid = draft.links.some((url) => {
      try {
        const parsed = new URL(url);
        return parsed.protocol !== "http:" && parsed.protocol !== "https:";
      } catch {
        return true;
      }
    });
    if (invalid) errors.push("Links must be absolute http or https URLs.");
  }
  if (hasSection("stats")) {
    const invalid = draft.stats.some((s) => !Number.isFinite(Number(s.value)));
    if (invalid) errors.push("Stat values must be numbers.");
  }
  if (hasSection("positions")) {
    const invalid = draft.positions.some((p) => !Number.isFinite(Number(p.value)));
    if (invalid) errors.push("Position values must be numbers.");
  }
  if (hasSection("rating") || hasSection("description")) {
    if (draft.ratingText.trim()) {
      const rating = Number(draft.ratingText.trim());
      if (!Number.isFinite(rating) || rating < 0 || rating > ratingMax) {
        errors.push(`Rating must be a number from 0 to ${ratingMax}.`);
      }
    }
  }
  return errors;
}

export function emptyPatch(): EntityMetadataPatch {
  return {
    title: null,
    description: null,
    externalIds: {},
    urls: [],
    tags: [],
    studio: null,
    credits: [],
    dates: {},
    stats: {},
    positions: {},
    classification: null,
    rating: null,
    flags: null,
  };
}

/**
 * Expands one credit draft into the patch rows the backend accumulates back into a single
 * cast link: one row per role (character on the first), plus one row per preserved extra
 * character so the stored distinct lists survive the full-replace save.
 */
function creditPatchRows(
  credit: EntityCreditDraft,
  sortOrder: number,
): EntityMetadataPatch["credits"] {
  const name = credit.name.trim();
  if (!name) return [];

  const roles = credit.roles.filter((role) => role.trim()).length > 0
    ? credit.roles.filter((role) => role.trim())
    : [CREDIT_ROLE.person];
  const character = credit.character.trim() ? credit.character.trim() : null;
  const rows = roles.map((role, index) => ({
    name,
    role,
    character: index === 0 ? character : null,
    sortOrder,
  }));
  for (const extra of credit.extraCharacters) {
    const value = extra.trim();
    if (value && value.toLowerCase() !== character?.toLowerCase()) {
      rows.push({ name, role: roles[0], character: value, sortOrder });
    }
  }
  return rows;
}

function kvToRecord(pairs: KeyValuePair[]): Record<string, string> {
  const result: Record<string, string> = {};
  for (const { key, value } of pairs) {
    if (key.trim() && value.trim()) result[key.trim()] = value.trim();
  }
  return result;
}

function kvToNumberRecord(pairs: KeyValuePair[]): Record<string, number> {
  const result: Record<string, number> = {};
  for (const { key, value } of pairs) {
    const num = Number(value);
    if (key.trim() && Number.isFinite(num)) result[key.trim()] = num;
  }
  return result;
}

export function buildMetadataUpdate(activeSections: EditableSection[], draft: EntityDetailEditDraft): EntityMetadataUpdateRequest {
  const fields: string[] = [];
  const patch = emptyPatch();
  const hasSection = (sectionId: string) => activeSections.some((section) => section.id === sectionId);
  const addField = (field: string) => {
    if (!fields.includes(field)) fields.push(field);
  };
  if (hasSection("description")) {
    addField("title");
    addField("description");
    addField("rating");
    addField("flags");
    patch.title = draft.title.trim() ? draft.title.trim() : null;
    patch.description = draft.description.trim() ? draft.description.trim() : null;
    patch.rating = draft.ratingText.trim() ? Number(draft.ratingText.trim()) : null;
    patch.flags = {
      isFavorite: draft.isFavorite,
      isNsfw: draft.isNsfw,
      isOrganized: draft.isOrganized,
    };
  }
  if (hasSection("links")) {
    addField("urls");
    addField("externalIds");
    patch.urls = draft.links;
    patch.externalIds = kvToRecord(draft.externalIds);
  }
  if (hasSection("tags")) {
    addField("tags");
    patch.tags = draft.tagPicks.map((p) => p.title);
  }
  if (hasSection("studio")) {
    addField("studio");
    patch.studio = draft.studioPick.length > 0 ? draft.studioPick[0].title : null;
  }
  if (hasSection("credits")) {
    addField("credits");
    patch.credits = draft.credits.flatMap(creditPatchRows);
  }
  if (hasSection("dates")) {
    addField("dates");
    patch.dates = kvToRecord(draft.dates);
  }
  if (hasSection("stats")) {
    addField("stats");
    patch.stats = kvToNumberRecord(draft.stats);
  }
  if (hasSection("positions")) {
    addField("positions");
    patch.positions = kvToNumberRecord(draft.positions);
  }
  if (hasSection("classification")) {
    addField("classification");
    patch.classification = draft.classification.trim() ? draft.classification.trim() : null;
  }
  if (hasSection("rating") && !hasSection("description")) {
    addField("rating");
    patch.rating = draft.ratingText.trim() ? Number(draft.ratingText.trim()) : null;
  }
  if (hasSection("flags") && !hasSection("description")) {
    addField("flags");
    patch.flags = {
      isFavorite: draft.isFavorite,
      isNsfw: draft.isNsfw,
      isOrganized: draft.isOrganized,
    };
  }
  return { fields, patch };
}
