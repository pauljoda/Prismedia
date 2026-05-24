<script lang="ts">
  import type { Snippet } from "svelte";
  import { goto } from "$app/navigation";
  import {
    Badge,
    BarChart3,
    Building2,
    Calendar,
    Database,
    Fingerprint,
    Star,
    Heart,
    Flame,
    CheckCircle,
    ExternalLink,
    Link,
    ListOrdered,
    MonitorCog,
    Pencil,
    PencilOff,
    Play,
    Save,
    Users,
    X,
  } from "@lucide/svelte";
  import type { LucideIcon } from "@lucide/svelte";
  import type { EntityDetailCard, EntityDetailCardFull, EntityDetailCredit } from "$lib/entities/entity-detail";
  import { renderEntityDescriptionMarkdown } from "$lib/entities/entity-detail-markdown";
  import { hasHero, hasPoster } from "$lib/entities/entity-detail";
  import { entityReferenceToThumbnailCard, placeholderGradient } from "$lib/entities/entity-thumbnail";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import EntityTagChips from "./EntityTagChips.svelte";
  import MarkdownEditor from "$lib/components/forms/MarkdownEditor.svelte";
  import EntityPicker from "$lib/components/forms/EntityPicker.svelte";
  import type { EntityPickerItem } from "$lib/components/forms/EntityPicker.svelte";
  import ListEditor from "$lib/components/forms/ListEditor.svelte";
  import KeyValueEditor from "$lib/components/forms/KeyValueEditor.svelte";
  import type { KeyValuePair } from "$lib/components/forms";
  import ToggleChip from "$lib/components/forms/ToggleChip.svelte";
  import TextField from "$lib/components/forms/TextField.svelte";
  import { isNsfw as hasNsfwCapability } from "$lib/api/capabilities";
  import { listTags, listPeople, listStudios } from "$lib/api/generated/prismedia";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  export type EntityDetailPosterSize = "none" | "small" | "medium" | "large";

  export interface EntityDetailTab {
    id: string;
    label: string;
    count?: number;
    icon?: LucideIcon;
    sections: string[];
    layout?: "stack" | "grid";
  }

  export interface EntityDetailSection {
    id: string;
    label?: string;
    count?: number;
    icon?: LucideIcon;
    editable?: boolean;
    hidden?: boolean;
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

  interface EntityDetailEditDraft {
    title: string;
    description: string;
    externalIds: KeyValuePair[];
    links: string[];
    tagPicks: EntityPickerItem[];
    studioPick: EntityPickerItem[];
    creditPicks: EntityPickerItem[];
    dates: KeyValuePair[];
    stats: KeyValuePair[];
    positions: KeyValuePair[];
    classification: string;
    ratingText: string;
    isFavorite: boolean;
    isNsfw: boolean;
    isOrganized: boolean;
  }

  interface Props {
    card: EntityDetailCard;
    onRatingChange?: (value: number | null) => void;
    onFavoriteToggle?: () => void;
    onOrganizedToggle?: () => void;
    posterSize?: EntityDetailPosterSize;
    ratingBusy?: boolean;
    showHero?: boolean;
    tabs?: EntityDetailTab[];
    onMetadataSave?: (request: EntityMetadataUpdateRequest) => void | Promise<void>;
    /** Route-provided sections that can be assigned to any tab. */
    sections?: EntityDetailSection[];
    /** Inline metadata rendered below the title (e.g. studio link · date · count). */
    heroMeta?: Snippet;
    /** Badge row rendered below the rating stars (e.g. Season 1, Episode 2). */
    heroBadges?: Snippet;
    /** Action buttons rendered in the right-aligned actions group (e.g. identify). */
    extraActions?: Snippet;
    /** Content rendered between the detail body and the metadata sections (e.g. studio, credits). */
    afterBody?: Snippet;
    /** Extra metadata sections appended inside the lower metadata area. */
    extraSections?: Snippet;
    /** Custom content for route-provided sections. Core section IDs render built-in detail content. */
    sectionContent?: Snippet<[EntityDetailSection]>;
  }

  let {
    card,
    onRatingChange,
    onFavoriteToggle,
    onOrganizedToggle,
    posterSize = "medium",
    ratingBusy = false,
    showHero = true,
    tabs = [],
    onMetadataSave,
    sections = [],
    heroMeta,
    heroBadges,
    extraActions,
    afterBody,
    extraSections,
    sectionContent,
  }: Props = $props();

  let favoriteAnimating = $state(false);
  let organizedAnimating = $state(false);
  let ratingAnim = $state<"fill" | "clear" | null>(null);
  let ratingAnimCount = $state(0);
  let activeTabId = $state("");
  let editingTabId = $state<string | null>(null);
  let pendingTabId = $state<string | null>(null);
  let savingEdit = $state(false);
  let editError = $state<string | null>(null);
  let initialDraft = $state<EntityDetailEditDraft | null>(null);
  let editDraft = $state<EntityDetailEditDraft>({
    title: "",
    description: "",
    externalIds: [],
    links: [],
    tagPicks: [],
    studioPick: [],
    creditPicks: [],
    dates: [],
    stats: [],
    positions: [],
    classification: "",
    ratingText: "",
    isFavorite: false,
    isNsfw: false,
    isOrganized: false,
  });

  const isFavorite = $derived(card.flags.find((f) => f.code === "favorite")?.active ?? false);
  const isNsfw = $derived(
    card.flags.find((f) => f.code === "nsfw")?.active ??
      hasNsfwCapability(card.entity.capabilities),
  );
  const isOrganized = $derived(card.flags.find((f) => f.code === "organized")?.active ?? false);
  const nsfw = useNsfw();

  $effect(() => {
    if (nsfw.mode === "off" && isNsfw) {
      void goto("/", { replaceState: true });
    }
  });

  function handleFavoriteClick(e: MouseEvent) {
    if (!onFavoriteToggle) return;
    (e.currentTarget as HTMLElement).blur();
    favoriteAnimating = true;
    onFavoriteToggle();
    setTimeout(() => (favoriteAnimating = false), 400);
  }

  function handleOrganizedClick(e: MouseEvent) {
    if (!onOrganizedToggle) return;
    (e.currentTarget as HTMLElement).blur();
    organizedAnimating = true;
    onOrganizedToggle();
    setTimeout(() => (organizedAnimating = false), 400);
  }

  type HeroMode = "image" | "poster-blur" | "gradient";

  const heroMode = $derived.by((): HeroMode => {
    if (!showHero) return "gradient";
    if (hasHero(card)) return "image";
    if (hasPoster(card)) return "poster-blur";
    return "gradient";
  });

  const posterCard = $derived(card.posterCard ?? null);
  const posterVisible = $derived(posterSize !== "none" && posterCard !== null);

  const renderedDescription = $derived(renderEntityDescriptionMarkdown(card.description));
  const hasTabs = $derived(tabs.length > 0);
  const activeTab = $derived(tabs.find((tab) => tab.id === activeTabId) ?? tabs[0] ?? null);
  const coreSections = $derived.by((): EntityDetailSection[] => [
    { id: "description", label: "Description" },
    { id: "tags", label: "Tags" },
    { id: "links", label: "Links", icon: Link },
    { id: "studio", label: "Studio", icon: Building2 },
    { id: "credits", label: "Credits", icon: Users },
    { id: "stats", label: "Stats", icon: BarChart3 },
    { id: "dates", label: "Dates", icon: Calendar },
    { id: "technical", label: "Technical", icon: MonitorCog },
    { id: "progress", label: "Progress", icon: Play },
    { id: "positions", label: "Positions", icon: ListOrdered },
    { id: "classification", label: "Classification", icon: Badge },
    { id: "rating", label: "Rating", icon: Star },
    { id: "flags", label: "Flags", icon: CheckCircle },
    { id: "source", label: "Source", icon: Database },
    { id: "sources", label: "Sources", icon: Database },
    { id: "fingerprints", label: "Fingerprints", icon: Fingerprint },
  ]);
  const availableSections = $derived([...coreSections, ...sections]);
  const activeTabSections = $derived(activeTab ? sectionsForTab(activeTab) : []);
  const cardFull = $derived(card as EntityDetailCard & Partial<EntityDetailCardFull>);
  const standaloneMetadataSectionIds = [
    "studio",
    "credits",
    "stats",
    "dates",
    "technical",
    "progress",
    "positions",
    "classification",
    "sources",
    "fingerprints",
    "links",
  ];
  const standaloneMetadataSections = $derived.by(() =>
    standaloneMetadataSectionIds
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .filter(sectionHasContent),
  );
  const isEditingActiveTab = $derived(
    hasTabs ? Boolean(activeTab && editingTabId === activeTab.id) : editingTabId === "__standalone__",
  );
  const canEdit = $derived(Boolean(onMetadataSave));
  const editActionLabel = $derived(activeTab ? `Edit ${activeTab.label}` : "Edit details");
  const cancelEditActionLabel = $derived(activeTab ? `Cancel ${activeTab.label}` : "Cancel editing");
  const standaloneEditSections = $derived.by(() => {
    const ids = ["description", "tags", ...standaloneMetadataSectionIds];
    return ids
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .filter(sectionEditable);
  });
  const currentEditSections = $derived(hasTabs ? activeTabSections : standaloneEditSections);
  const editValidationErrors = $derived.by(() => validateDraft(currentEditSections, editDraft));
  const editDirty = $derived(Boolean(initialDraft && serializeDraft(initialDraft) !== serializeDraft(editDraft)));
  const saveDisabled = $derived(!editDirty || editValidationErrors.length > 0 || savingEdit);

  function handleRatingClick(e: MouseEvent, value: number) {
    if (!onRatingChange || ratingBusy || !card.rating) return;
    (e.currentTarget as HTMLElement).blur();
    const clearing = card.rating.value === value;
    const nextValue = clearing ? null : value;

    ratingAnim = clearing ? "clear" : "fill";
    ratingAnimCount = clearing ? card.rating.value : value;
    onRatingChange(nextValue);

    const duration = clearing ? 350 : 80 * value + 200;
    setTimeout(() => (ratingAnim = null), duration);
  }

  function findSection(sectionId: string): EntityDetailSection | null {
    return availableSections.find((section) => section.id === sectionId) ?? null;
  }

  function sectionEditable(section: EntityDetailSection): boolean {
    if (section.editable != null) return section.editable;
    return [
      "description",
      "tags",
      "studio",
      "credits",
      "links",
      "dates",
      "stats",
      "positions",
      "classification",
      "rating",
      "flags",
    ].includes(section.id);
  }

  function sectionHasContent(section: EntityDetailSection): boolean {
    if (section.hidden) return false;
    if (isEditingActiveTab && sectionEditable(section)) return true;

    switch (section.id) {
      case "description":
        return Boolean(renderedDescription);
      case "tags":
        return card.tags.length > 0;
      case "links":
        return card.links.length > 0;
      case "studio":
        return Boolean(cardFull.studio);
      case "credits":
        return (cardFull.credits?.length ?? 0) > 0;
      case "stats":
        return (cardFull.stats?.length ?? 0) > 0;
      case "dates":
        return (cardFull.dates?.length ?? 0) > 0;
      case "technical":
        return (cardFull.technical?.length ?? 0) > 0;
      case "progress":
        return Boolean(cardFull.progress);
      case "positions":
        return (cardFull.positions?.length ?? 0) > 0;
      case "classification":
        return Boolean(cardFull.classification);
      case "rating":
        return Boolean(card.rating);
      case "flags":
        return card.flags.length > 0;
      case "source":
      case "sources":
        return (cardFull.sources?.length ?? 0) > 0 || (cardFull.fingerprints?.length ?? 0) > 0;
      case "fingerprints":
        return (cardFull.fingerprints?.length ?? 0) > 0;
      default:
        return Boolean(sectionContent);
    }
  }

  function sectionsForTab(tab: EntityDetailTab): EntityDetailSection[] {
    return tab.sections
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .filter(sectionHasContent);
  }

  function draftFromCard(): EntityDetailEditDraft {
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
      creditPicks: (cardFull.credits ?? []).map((c) => ({
        id: c.id,
        title: c.title,
        thumbnailUrl: c.thumbnail,
      })),
      dates: "dates" in card
        ? ((card as EntityDetailCard & { dates?: Array<{ code: string; value: string }> }).dates ?? []).map((d) => ({ key: d.code, value: d.value }))
        : [],
      stats: (cardFull.stats ?? []).map((s) => ({ key: s.code, value: String(s.value) })),
      positions: (cardFull.positions ?? []).map((p) => ({ key: p.code, value: String(p.value) })),
      classification: cardFull.classification?.value ?? "",
      ratingText: card.rating?.value == null ? "" : String(card.rating.value),
      isFavorite,
      isNsfw,
      isOrganized,
    };
  }

  function hasProvider(link: EntityDetailCard["links"][number]): link is EntityDetailCard["links"][number] & { provider: string } {
    return Boolean(link.provider);
  }

  function externalIdValue(label: string, provider: string): string {
    const prefix = `${provider}:`;
    return label.startsWith(prefix) ? label.slice(prefix.length).trim() : label;
  }

  function serializeDraft(draft: EntityDetailEditDraft | null): string {
    return JSON.stringify(draft);
  }

  function startEdit(tab?: EntityDetailTab) {
    const nextDraft = draftFromCard();
    initialDraft = { ...nextDraft };
    editDraft = { ...nextDraft };
    editingTabId = tab?.id ?? "__standalone__";
    editError = null;
  }

  function cancelEdit() {
    editingTabId = null;
    initialDraft = null;
    editError = null;
  }

  function requestTab(tabId: string) {
    if (tabId === activeTab?.id) return;
    if (editDirty) {
      pendingTabId = tabId;
      return;
    }
    activeTabId = tabId;
    cancelEdit();
  }

  function stayOnDirtyTab() {
    pendingTabId = null;
  }

  function discardDirtyTab() {
    if (pendingTabId) activeTabId = pendingTabId;
    pendingTabId = null;
    cancelEdit();
  }

  function validateUrl(url: string): string | null {
    try {
      const parsed = new URL(url);
      return parsed.protocol === "http:" || parsed.protocol === "https:" ? null : "Must be http or https";
    } catch {
      return "Invalid URL";
    }
  }

  function validateDraft(activeSections: EntityDetailSection[], draft: EntityDetailEditDraft): string[] {
    const errors: string[] = [];
    if (activeSections.some((section) => section.id === "links")) {
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
    if (activeSections.some((section) => section.id === "stats")) {
      const invalid = draft.stats.some((s) => !Number.isFinite(Number(s.value)));
      if (invalid) errors.push("Stat values must be numbers.");
    }
    if (activeSections.some((section) => section.id === "positions")) {
      const invalid = draft.positions.some((p) => !Number.isFinite(Number(p.value)));
      if (invalid) errors.push("Position values must be numbers.");
    }
    if (activeSections.some((section) => section.id === "rating") || activeSections.some((section) => section.id === "description")) {
      if (draft.ratingText.trim()) {
        const rating = Number(draft.ratingText.trim());
        const max = card.rating?.max ?? 5;
        if (!Number.isFinite(rating) || rating < 0 || rating > max) {
          errors.push(`Rating must be a number from 0 to ${max}.`);
        }
      }
    }
    return errors;
  }

  function emptyPatch(): EntityMetadataPatch {
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

  function buildMetadataUpdate(activeSections: EntityDetailSection[], draft: EntityDetailEditDraft): EntityMetadataUpdateRequest {
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
      patch.credits = draft.creditPicks.map((p, i) => ({
        name: p.title,
        role: "performer",
        sortOrder: i,
      }));
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

  async function saveEdit() {
    if (!onMetadataSave || saveDisabled) return;
    savingEdit = true;
    editError = null;
    try {
      await onMetadataSave(buildMetadataUpdate(currentEditSections, editDraft));
      cancelEdit();
    } catch (err) {
      editError = err instanceof Error ? err.message : String(err);
    } finally {
      savingEdit = false;
    }
  }

  function creditToThumbnailCard(credit: EntityDetailCredit) {
    return entityReferenceToThumbnailCard({
      id: credit.id,
      kind: credit.kind,
      title: credit.title,
      thumbnailUrl: credit.thumbnail,
    });
  }

  async function searchTags(query: string): Promise<EntityPickerItem[]> {
    const params = query ? { query, limit: 20 } : { limit: 20 };
    const response = await listTags(params);
    return response.data.items.map((item) => ({
      id: item.id,
      title: item.title,
      thumbnailUrl: item.coverUrl ?? null,
    }));
  }

  async function searchPeople(query: string): Promise<EntityPickerItem[]> {
    const params = query ? { query, limit: 20 } : { limit: 20 };
    const response = await listPeople(params);
    return response.data.items.map((item) => ({
      id: item.id,
      title: item.title,
      thumbnailUrl: item.coverUrl ?? null,
    }));
  }

  async function searchStudios(query: string): Promise<EntityPickerItem[]> {
    const params = query ? { query, limit: 20 } : { limit: 20 };
    const response = await listStudios(params);
    return response.data.items.map((item) => ({
      id: item.id,
      title: item.title,
      thumbnailUrl: item.coverUrl ?? null,
    }));
  }
</script>

{#snippet descriptionContent()}
  {#if renderedDescription}
    <div class="description-content markdown-body">
      {@html renderedDescription}
    </div>
  {/if}
{/snippet}

{#snippet tagsContent()}
  <EntityTagChips tags={card.tags} />
{/snippet}

{#snippet descriptionSection()}
  <div class="detail-body">
    {@render descriptionContent()}
  </div>
{/snippet}

{#snippet tagsSection()}
  <div class="detail-body">
    {@render tagsContent()}
  </div>
{/snippet}

{#snippet linksSection()}
  {#if card.links.length > 0}
    <section class="detail-section">
      <h2 class="section-label">
        <Link class="h-4 w-4" />
        Links
      </h2>
      <div class="link-list">
        {#each card.links as link (link.label)}
          {#if link.url}
            <a href={link.url} target="_blank" rel="noopener noreferrer" class="link-item">
              <ExternalLink class="h-3.5 w-3.5" />
              {link.label}
            </a>
          {:else}
            <span class="link-item no-url">
              <Link class="h-3.5 w-3.5" />
              {link.label}
            </span>
          {/if}
        {/each}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet descriptionEditSection()}
  <section class="detail-section edit-section">
    <TextField
      value={editDraft.title}
      onChange={(v) => (editDraft.title = v)}
      label="Title"
      placeholder="Entity title"
    />
    <div class="edit-inline-grid">
      <TextField
        value={editDraft.ratingText}
        onChange={(v) => (editDraft.ratingText = v)}
        label="Rating"
        helper="0 to {card.rating?.max ?? 5}; empty clears"
        type="number"
        min="0"
        max={card.rating?.max ?? 5}
      />
      <div class="edit-field">
        <span class="edit-field-label">Flags</span>
        <div class="edit-flag-chips">
          <ToggleChip value={editDraft.isFavorite} onChange={(v) => (editDraft.isFavorite = v)} onLabel="Favorite" icon={Heart} />
          <ToggleChip value={editDraft.isNsfw} onChange={(v) => (editDraft.isNsfw = v)} onLabel="NSFW" variant="warning" icon={Flame} />
          <ToggleChip value={editDraft.isOrganized} onChange={(v) => (editDraft.isOrganized = v)} onLabel="Organized" icon={CheckCircle} />
        </div>
      </div>
    </div>
    <MarkdownEditor
      value={editDraft.description}
      onChange={(v) => (editDraft.description = v)}
      label="Description"
      placeholder="Write a description…"
    />
  </section>
{/snippet}

{#snippet tagsEditSection()}
  <section class="detail-section edit-section">
    <EntityPicker
      values={editDraft.tagPicks}
      onChange={(v) => {
        editDraft.tagPicks = v;
      }}
      onSearch={searchTags}
      label="Tags"
      placeholder="Search tags…"
      canAddNew={true}
      addNewLabel="tag"
      mode="multi"
    />
  </section>
{/snippet}

{#snippet studioEditSection()}
  <section class="detail-section edit-section">
    <EntityPicker
      values={editDraft.studioPick}
      onChange={(v) => {
        editDraft.studioPick = v;
      }}
      onSearch={searchStudios}
      label="Studio"
      placeholder="Search studios…"
      canAddNew={true}
      addNewLabel="studio"
      mode="single"
    />
  </section>
{/snippet}

{#snippet creditsEditSection()}
  <section class="detail-section edit-section">
    <EntityPicker
      values={editDraft.creditPicks}
      onChange={(v) => {
        editDraft.creditPicks = v;
      }}
      onSearch={searchPeople}
      label="Cast & Crew"
      placeholder="Search people…"
      canAddNew={true}
      addNewLabel="person"
      mode="multi"
    />
  </section>
{/snippet}

{#snippet linksEditSection()}
  <section class="detail-section edit-section">
    <ListEditor
      values={editDraft.links}
      onChange={(v) => (editDraft.links = v)}
      label="Links"
      placeholder="https://example.com"
      icon={Link}
      validate={validateUrl}
    />
    <KeyValueEditor
      values={editDraft.externalIds}
      onChange={(v) => (editDraft.externalIds = v)}
      label="External IDs"
      icon={ExternalLink}
      keyPlaceholder="provider"
      valuePlaceholder="id"
      keyLabel="Provider"
      valueLabel="ID"
    />
  </section>
{/snippet}

{#snippet datesEditSection()}
  <section class="detail-section edit-section">
    <KeyValueEditor
      values={editDraft.dates}
      onChange={(v) => (editDraft.dates = v)}
      label="Dates"
      icon={Calendar}
      keyPlaceholder="date_aired"
      valuePlaceholder="2025-01-15"
      keyLabel="Code"
      valueLabel="Date"
    />
  </section>
{/snippet}

{#snippet statsEditSection()}
  <section class="detail-section edit-section">
    <KeyValueEditor
      values={editDraft.stats}
      onChange={(v) => (editDraft.stats = v)}
      label="Stats"
      icon={BarChart3}
      keyPlaceholder="runtime"
      valuePlaceholder="120"
      keyLabel="Stat"
      valueLabel="Value"
      valueInputMode="decimal"
      validateValue={(v) => Number.isFinite(Number(v)) ? null : "Must be a number"}
    />
  </section>
{/snippet}

{#snippet positionsEditSection()}
  <section class="detail-section edit-section">
    <KeyValueEditor
      values={editDraft.positions}
      onChange={(v) => (editDraft.positions = v)}
      label="Positions"
      icon={ListOrdered}
      keyPlaceholder="season"
      valuePlaceholder="1"
      keyLabel="Position"
      valueLabel="Value"
      valueInputMode="decimal"
      validateValue={(v) => Number.isFinite(Number(v)) ? null : "Must be a number"}
    />
  </section>
{/snippet}

{#snippet classificationEditSection()}
  <section class="detail-section edit-section">
    <TextField
      value={editDraft.classification}
      onChange={(v) => (editDraft.classification = v)}
      label="Classification"
      icon={Badge}
      placeholder="e.g. TV-MA, PG-13"
      helper="Empty clears the value"
    />
  </section>
{/snippet}

{#snippet ratingEditSection()}
  <section class="detail-section edit-section">
    <TextField
      value={editDraft.ratingText}
      onChange={(v) => (editDraft.ratingText = v)}
      label="Rating"
      icon={Star}
      helper="0 to {card.rating?.max ?? 5}; empty clears"
      type="number"
      min="0"
      max={card.rating?.max ?? 5}
    />
  </section>
{/snippet}

{#snippet flagsEditSection()}
  <section class="detail-section edit-section">
    <div class="edit-field">
      <span class="edit-field-label">Flags</span>
      <div class="edit-flag-chips">
        <ToggleChip value={editDraft.isFavorite} onChange={(v) => (editDraft.isFavorite = v)} onLabel="Favorite" icon={Heart} />
        <ToggleChip value={editDraft.isNsfw} onChange={(v) => (editDraft.isNsfw = v)} onLabel="NSFW" variant="warning" icon={Flame} />
        <ToggleChip value={editDraft.isOrganized} onChange={(v) => (editDraft.isOrganized = v)} onLabel="Organized" icon={CheckCircle} />
      </div>
    </div>
  </section>
{/snippet}

{#snippet referenceItem(credit: EntityDetailCredit)}
  <div class="reference-thumbnail">
    <EntityThumbnail
      card={creditToThumbnailCard(credit)}
      selectable={false}
      titleAlign="center"
      titleSize="compact"
    />
  </div>
{/snippet}

{#snippet studioSection()}
  {#if cardFull.studio}
    <section class="detail-section">
      <h2 class="section-label">
        <Building2 class="h-4 w-4" />
        Studio
      </h2>
      <div class="reference-list is-horizontal-rail">
        {@render referenceItem(cardFull.studio)}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet creditsSection()}
  {#if (cardFull.credits?.length ?? 0) > 0}
    <section class="detail-section">
      <h2 class="section-label">
        <Users class="h-4 w-4" />
        Credits
      </h2>
      <div class="reference-list is-horizontal-rail">
        {#each cardFull.credits ?? [] as credit, index (`${credit.id}:${index}`)}
          {@render referenceItem(credit)}
        {/each}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet statsSection()}
  {#if (cardFull.stats?.length ?? 0) > 0}
    <section class="detail-section">
      <h2 class="section-label">
        <BarChart3 class="h-4 w-4" />
        Stats
      </h2>
      <div class="tab-data-list">
        {#each cardFull.stats ?? [] as row (row.code)}
          <div class="tab-data-row">
            <span>{row.label}</span>
            <strong>{row.value}</strong>
          </div>
        {/each}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet datesSection()}
  {#if (cardFull.dates?.length ?? 0) > 0}
    <section class="detail-section">
      <h2 class="section-label">
        <Calendar class="h-4 w-4" />
        Dates
      </h2>
      <div class="tab-data-list">
        {#each cardFull.dates ?? [] as row (row.code)}
          <div class="tab-data-row">
            <span>{row.label}</span>
            <strong>{row.value}</strong>
          </div>
        {/each}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet technicalSection()}
  {#if (cardFull.technical?.length ?? 0) > 0}
    <section class="detail-section">
      <h2 class="section-label">
        <MonitorCog class="h-4 w-4" />
        Technical
      </h2>
      <div class="tab-data-list">
        {#each cardFull.technical ?? [] as row (row.label)}
          <div class="tab-data-row">
            <span>{row.label}</span>
            <strong>{row.value}</strong>
          </div>
        {/each}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet progressSection()}
  {#if cardFull.progress}
    <section class="detail-section">
      <h2 class="section-label">
        <Play class="h-4 w-4" />
        Progress
      </h2>
      <div class="tab-data-list">
        <div class="tab-data-row">
          <span>Progress</span>
          <strong>{cardFull.progress.index} / {cardFull.progress.total} {cardFull.progress.unit}</strong>
        </div>
        <div class="tab-data-row">
          <span>Percent</span>
          <strong>{cardFull.progress.percent}%</strong>
        </div>
        {#if cardFull.progress.mode}
          <div class="tab-data-row">
            <span>Mode</span>
            <strong>{cardFull.progress.mode}</strong>
          </div>
        {/if}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet positionsSection()}
  {#if (cardFull.positions?.length ?? 0) > 0}
    <section class="detail-section">
      <h2 class="section-label">
        <ListOrdered class="h-4 w-4" />
        Positions
      </h2>
      <div class="tab-data-list">
        {#each cardFull.positions ?? [] as row (row.code)}
          <div class="tab-data-row">
            <span>{row.code}</span>
            <strong>{row.label}</strong>
          </div>
        {/each}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet classificationSection()}
  {#if cardFull.classification}
    <section class="detail-section">
      <h2 class="section-label">
        <Badge class="h-4 w-4" />
        Classification
      </h2>
      <div class="tab-data-list">
        <div class="tab-data-row">
          <span>{cardFull.classification.system ?? "classification"}</span>
          <strong>{cardFull.classification.value}</strong>
        </div>
      </div>
    </section>
  {/if}
{/snippet}

{#snippet sourceSection()}
  {#if (cardFull.sources?.length ?? 0) > 0 || (cardFull.fingerprints?.length ?? 0) > 0}
    <section class="detail-section">
      <h2 class="section-label">
        <Database class="h-4 w-4" />
        Source
      </h2>
      <div class="tab-data-list">
        {#each cardFull.sources ?? [] as source (source.code)}
          <div class="tab-data-row">
            <span>{source.code}</span>
            <strong>{source.value}</strong>
          </div>
        {/each}
        {#each cardFull.fingerprints ?? [] as fingerprint (`${fingerprint.algorithm}:${fingerprint.value}`)}
          <div class="tab-data-row">
            <span>{fingerprint.algorithm}</span>
            <strong>{fingerprint.value}</strong>
          </div>
        {/each}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet sourcesSection()}
  {#if (cardFull.sources?.length ?? 0) > 0}
    <section class="detail-section">
      <h2 class="section-label">
        <Database class="h-4 w-4" />
        Sources
      </h2>
      <div class="tab-data-list">
        {#each cardFull.sources ?? [] as source (source.code)}
          <div class="tab-data-row">
            <span>{source.code}</span>
            <strong>{source.value}</strong>
          </div>
        {/each}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet fingerprintsSection()}
  {#if (cardFull.fingerprints?.length ?? 0) > 0}
    <section class="detail-section">
      <h2 class="section-label">
        <Fingerprint class="h-4 w-4" />
        Fingerprints
      </h2>
      <div class="tab-data-list">
        {#each cardFull.fingerprints ?? [] as row (`${row.algorithm}:${row.value}`)}
          <div class="tab-data-row">
            <span>{row.algorithm}</span>
            <strong>{row.value}</strong>
          </div>
        {/each}
      </div>
    </section>
  {/if}
{/snippet}

{#snippet customSection(section: EntityDetailSection)}
  {#if sectionContent}
    <section class="detail-section custom-detail-section" aria-label={section.label ?? section.id}>
      {#if section.label}
        {@const SectionIcon = section.icon}
        <h2 class="section-label">
          {#if SectionIcon}
            <SectionIcon class="h-4 w-4" />
          {/if}
          {section.label}
        </h2>
      {/if}
      {@render sectionContent(section)}
    </section>
  {/if}
{/snippet}

{#snippet renderDetailSection(section: EntityDetailSection)}
  {#if isEditingActiveTab && section.id === "description"}
    {@render descriptionEditSection()}
  {:else if isEditingActiveTab && section.id === "tags"}
    {@render tagsEditSection()}
  {:else if isEditingActiveTab && section.id === "links"}
    {@render linksEditSection()}
  {:else if isEditingActiveTab && section.id === "dates"}
    {@render datesEditSection()}
  {:else if isEditingActiveTab && section.id === "stats"}
    {@render statsEditSection()}
  {:else if isEditingActiveTab && section.id === "positions"}
    {@render positionsEditSection()}
  {:else if isEditingActiveTab && section.id === "classification"}
    {@render classificationEditSection()}
  {:else if isEditingActiveTab && section.id === "rating"}
    {@render ratingEditSection()}
  {:else if isEditingActiveTab && section.id === "flags"}
    {@render flagsEditSection()}
  {:else if section.id === "description"}
    {@render descriptionSection()}
  {:else if section.id === "tags"}
    {@render tagsSection()}
  {:else if section.id === "links"}
    {@render linksSection()}
  {:else if isEditingActiveTab && section.id === "studio"}
    {@render studioEditSection()}
  {:else if isEditingActiveTab && section.id === "credits"}
    {@render creditsEditSection()}
  {:else if section.id === "studio"}
    {@render studioSection()}
  {:else if section.id === "credits"}
    {@render creditsSection()}
  {:else if section.id === "stats"}
    {@render statsSection()}
  {:else if section.id === "dates"}
    {@render datesSection()}
  {:else if section.id === "technical"}
    {@render technicalSection()}
  {:else if section.id === "progress"}
    {@render progressSection()}
  {:else if section.id === "positions"}
    {@render positionsSection()}
  {:else if section.id === "classification"}
    {@render classificationSection()}
  {:else if section.id === "source"}
    {@render sourceSection()}
  {:else if section.id === "sources"}
    {@render sourcesSection()}
  {:else if section.id === "fingerprints"}
    {@render fingerprintsSection()}
  {:else}
    {@render customSection(section)}
  {/if}
{/snippet}

{#snippet defaultDetailContent()}
  {#if isEditingActiveTab}
    <div class="detail-edit-toolbar">
      <div class="detail-edit-actions">
        <button type="button" class="edit-action secondary" onclick={cancelEdit} disabled={savingEdit} aria-label="Cancel editing">
          <X class="h-3.5 w-3.5" />
          Cancel
        </button>
        <button type="button" class="edit-action primary" onclick={() => void saveEdit()} disabled={saveDisabled} aria-label="Save changes">
          <Save class="h-3.5 w-3.5" />
          {savingEdit ? "Saving…" : "Save"}
        </button>
      </div>
    </div>
    {#if editValidationErrors.length > 0 || editError}
      <div class="edit-errors" aria-live="polite">
        {#each editValidationErrors as error (error)}
          <p>{error}</p>
        {/each}
        {#if editError}
          <p>{editError}</p>
        {/if}
      </div>
    {/if}
  {/if}

  {#if isEditingActiveTab}
    <div class="detail-body">
      {@render descriptionEditSection()}
      {@render tagsEditSection()}
    </div>
  {:else}
    <div class="detail-body">
      {@render descriptionContent()}
      {@render tagsContent()}
    </div>
  {/if}

  <!-- Kind-specific content between body and metadata (studio, credits, etc.) -->
  {#if afterBody}
    {@render afterBody()}
  {/if}

  <!-- Lower metadata sections -->
  {#if standaloneMetadataSections.length > 0 || extraSections}
    <div class="metadata-sections">
      {#if extraSections}
        {@render extraSections()}
      {/if}

      {#each standaloneMetadataSections as section (section.id)}
        {@render renderDetailSection(section)}
      {/each}
    </div>
  {/if}
{/snippet}

<article class="entity-detail" data-poster-size={posterSize} data-hero-mode={heroMode}>
  <!-- Hero -->
  <div class="hero" data-hero-mode={heroMode}>

    {#snippet heroContent()}
      <div class="hero-content">
        {#if posterVisible}
          <div class="poster-frame">
            <EntityThumbnail card={posterCard!} linkable={false} mediaOnly={true} />
          </div>
        {/if}

        <div class="hero-text">
          <h1 class="hero-title">{card.entity.title}</h1>

          {#if heroMeta}
            <div class="meta-row">
              {@render heroMeta()}
            </div>
          {/if}

          {#if card.rating}
            <div class="rating-row" role="group" aria-label="Rating">
              {#each { length: card.rating.max } as _, i (i)}
                {@const value = i + 1}
                {@const filling = ratingAnim === "fill" && value <= ratingAnimCount}
                {@const clearing = ratingAnim === "clear" && value <= ratingAnimCount}
                <button
                  type="button"
                  class="rating-star"
                  class:active={card.rating!.value >= value}
                  class:star-fill={filling}
                  class:star-clear={clearing}
                  style:animation-delay={filling ? `${(value - 1) * 70}ms` : "0ms"}
                  disabled={ratingBusy || !onRatingChange}
                  aria-label={`Rate ${value}`}
                  onclick={(e: MouseEvent) => handleRatingClick(e, value)}
                >
                  <Star class="h-5 w-5" />
                </button>
              {/each}
            </div>
          {/if}

          {#if heroBadges}
            <div class="position-badges">
              {@render heroBadges()}
            </div>
          {/if}

          <div class="action-row">
            <div class="action-badges">
              <button
                type="button"
                class="action-badge favorite"
                class:active={isFavorite}
                class:animating={favoriteAnimating}
                disabled={!onFavoriteToggle}
                aria-label={isFavorite ? "Remove from favorites" : "Add to favorites"}
                onclick={(e: MouseEvent) => handleFavoriteClick(e)}
              >
                <Heart class="h-4 w-4" />
              </button>

              {#if isNsfw}
                <span class="action-badge nsfw active" aria-label="NSFW">
                  <Flame class="h-4 w-4" />
                </span>
              {/if}

              <button
                type="button"
                class="action-badge organized"
                class:active={isOrganized}
                class:animating={organizedAnimating}
                disabled={!onOrganizedToggle}
                aria-label={isOrganized ? "Mark as unorganized" : "Mark as organized"}
                onclick={(e: MouseEvent) => handleOrganizedClick(e)}
              >
                <CheckCircle class="h-4 w-4" />
              </button>
            </div>

            {#if canEdit || extraActions}
              <div class="action-group">
                {#if canEdit}
                  {#if isEditingActiveTab}
                    <button
                      type="button"
                      class="action-btn editing"
                      onclick={cancelEdit}
                      disabled={savingEdit}
                      aria-label={cancelEditActionLabel}
                    >
                      <PencilOff class="h-3.5 w-3.5" />
                      <span>Editing</span>
                    </button>
                  {:else}
                    <button
                      type="button"
                      class="action-btn"
                      onclick={() => startEdit(activeTab ?? undefined)}
                      aria-label={editActionLabel}
                    >
                      <Pencil class="h-3.5 w-3.5" />
                      <span>Edit</span>
                    </button>
                  {/if}
                {/if}
                {#if extraActions}
                  {@render extraActions()}
                {/if}
              </div>
            {/if}
          </div>
        </div>
      </div>
    {/snippet}

    {#if heroMode === "image"}
      <!-- Sharp banner, mask fades bottom 10% -->
      <div class="hero-banner">
        <img src={card.hero!.src} alt="Banner" />
      </div>
      <!-- Lower zone: reflection bg + content on top -->
      <div class="hero-lower">
        <div class="hero-reflection">
          <img src={card.hero!.src} alt="" aria-hidden="true" />
        </div>
        <div class="hero-blur-overlay"></div>
        {@render heroContent()}
      </div>
    {:else if heroMode === "poster-blur"}
      <div class="hero-backdrop poster-mode">
        <div class="hero-backdrop-img">
          <img src={card.poster!.src} alt="" aria-hidden="true" />
        </div>
        <div class="hero-backdrop-blur"></div>
      </div>
      {@render heroContent()}
    {:else}
      <div class="hero-gradient-bg" style:background-image={placeholderGradient(card.entity.title)}></div>
      {@render heroContent()}
    {/if}
  </div>

  {#if hasTabs}
    <div class="detail-tabs">
      <div class="detail-tab-list" role="tablist" aria-label="Detail sections">
        {#each tabs as tab (tab.id)}
          {@const active = activeTab?.id === tab.id}
          {@const TabIcon = tab.icon}
          <button
            type="button"
            role="tab"
            id={`entity-detail-tab-${tab.id}`}
            aria-selected={active}
            aria-controls={`entity-detail-panel-${tab.id}`}
            class:active
            onclick={() => requestTab(tab.id)}
          >
            {#if TabIcon}
              <TabIcon class="detail-tab-icon h-3.5 w-3.5" />
            {/if}
            <span>{tab.label}</span>
            {#if tab.count != null && tab.count > 0}
              <strong>{tab.count}</strong>
            {/if}
          </button>
        {/each}
      </div>

      {#if activeTab}
        <div
          class="detail-tab-panel"
          role="tabpanel"
          id={`entity-detail-panel-${activeTab.id}`}
          aria-labelledby={`entity-detail-tab-${activeTab.id}`}
        >
          {#if isEditingActiveTab}
            <div class="detail-edit-toolbar">
              <div class="detail-edit-actions">
                <button type="button" class="edit-action secondary" onclick={cancelEdit} disabled={savingEdit} aria-label={`Cancel ${activeTab.label}`}>
                  <X class="h-3.5 w-3.5" />
                  Cancel
                </button>
                <button type="button" class="edit-action primary" onclick={() => void saveEdit()} disabled={saveDisabled} aria-label={`Save ${activeTab.label}`}>
                  <Save class="h-3.5 w-3.5" />
                  {savingEdit ? "Saving…" : "Save"}
                </button>
              </div>
            </div>
            {#if isEditingActiveTab && (editValidationErrors.length > 0 || editError)}
              <div class="edit-errors" aria-live="polite">
                {#each editValidationErrors as error (error)}
                  <p>{error}</p>
                {/each}
                {#if editError}
                  <p>{editError}</p>
                {/if}
              </div>
            {/if}
          {/if}
          {#key activeTab.id}
            <div class="detail-tab-sections" data-layout={activeTab.layout ?? "stack"}>
              {#each activeTabSections as section (section.id)}
                {@render renderDetailSection(section)}
              {:else}
                <div class="tab-empty-state">No details available.</div>
              {/each}
            </div>
          {/key}
        </div>
      {/if}
    </div>
  {:else}
    {@render defaultDetailContent()}
  {/if}
</article>

{#if pendingTabId}
  <div class="edit-confirm-backdrop">
    <div class="edit-confirm" role="dialog" aria-modal="true" aria-label="Discard unsaved edits?">
      <h2>Discard unsaved edits?</h2>
      <p>Changing tabs will leave the current edit session.</p>
      <div class="edit-confirm-actions">
        <button type="button" class="edit-action secondary" onclick={stayOnDirtyTab}>Stay here</button>
        <button type="button" class="edit-action primary" onclick={discardDirtyTab}>Discard changes</button>
      </div>
    </div>
  </div>
{/if}

<style>
  /* ── Layout ─────────────────────────────────────────────── */

  .entity-detail {
    --detail-accent: #c49a5a;
    --detail-accent-muted: rgba(196, 154, 90, 0.35);
    --detail-accent-glow: rgba(196, 154, 90, 0.15);
    --detail-surface: var(--color-surface-2, #101420);
    --detail-surface-raised: var(--color-surface-3, #151a28);
    --detail-border: var(--color-border, #1c2235);
    --detail-text: var(--color-text-primary, #f2eed8);
    --detail-text-secondary: var(--color-text-secondary, #c4c9d4);
    --detail-text-muted: var(--color-text-muted, #8a93a6);
    --detail-text-disabled: var(--color-text-disabled, #4a5260);
    --detail-glass: rgba(12, 15, 21, 0.72);
    --detail-glass-blur: 12px;
    --hero-banner-max-height: clamp(13rem, 36vw, 20rem);
    --hero-lower-overlap: clamp(-3.75rem, -6vw, -2rem);

    display: grid;
    gap: 0;
    min-width: 0;
    overflow: hidden;
  }

  .entity-detail > * {
    min-width: 0;
  }

  /* ── Hero ────────────────────────────────────────────────── */

  .hero {
    position: relative;
    overflow: hidden;
    border-radius: var(--radius-md, 10px);
  }

  /* ── Sharp banner (mask fades bottom 10% into reflection) ── */

  .hero-banner {
    position: relative;
    z-index: 2;
    line-height: 0;
    mask-image: linear-gradient(to bottom, black 92%, transparent 100%);
    -webkit-mask-image: linear-gradient(to bottom, black 92%, transparent 100%);
  }

  .hero-banner img {
    width: 100%;
    height: auto;
    display: block;
    max-height: var(--hero-banner-max-height);
    object-fit: cover;
    filter: brightness(0.85) saturate(0.9);
  }

  /* ── Lower zone: reflection bg + content ──────────────── */

  .hero-lower {
    position: relative;
    margin-top: var(--hero-lower-overlap);
    overflow: hidden;
  }

  /* Reflection: blurred flipped copy of the banner as a color-wash background */
  .hero-reflection {
    position: absolute;
    inset: 0;
    z-index: 0;
    overflow: hidden;
  }

  .hero-reflection img {
    position: absolute;
    top: 0;
    left: 0;
    width: 100%;
    height: 100%;
    object-fit: cover;
    transform: scaleY(-1) scale(1.25);
    filter: blur(15px) saturate(1.3) brightness(0.5);
    will-change: transform;
  }

  .hero-blur-overlay {
    position: absolute;
    inset: 0;
    z-index: 1;
    background: rgba(7, 8, 11, 0.45);
  }


  /* ── Poster-blur backdrop (no banner) ──────────────────── */

  .hero-backdrop {
    position: absolute;
    inset: 0;
    z-index: 0;
    overflow: hidden;
  }

  .hero-backdrop-img {
    position: absolute;
    inset: -40px;
  }

  .hero-backdrop-img img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    object-position: center center;
    transform: scale(1.3);
    filter: blur(15px) saturate(1.3) brightness(0.5);
    will-change: transform;
  }

  .hero-backdrop-blur {
    position: absolute;
    inset: 0;
    background: rgba(7, 8, 11, 0.45);
  }


  /* Gradient background when no images exist */
  .hero-gradient-bg {
    position: absolute;
    inset: 0;
    z-index: 0;
    background-size: cover;
  }

  /* ── Hero content (poster + text) ──────────────────────── */

  .hero-content {
    position: relative;
    display: flex;
    align-items: end;
    gap: 1.25rem;
    padding: 1.5rem;
    padding-top: 3rem;
    z-index: 3;
  }

  /* ── Poster / cover ────────────────────────────────────── */

  .poster-frame {
    flex-shrink: 0;
    width: var(--poster-width, 7rem);
    border-radius: var(--radius-sm, 6px);
    background: #050505;
    box-shadow:
      0 8px 32px rgba(0, 0, 0, 0.6),
      0 0 0 1px rgba(196, 154, 90, 0.2);
    overflow: hidden;
  }

  [data-poster-size="small"] .poster-frame { --poster-width: 5rem; }
  [data-poster-size="medium"] .poster-frame { --poster-width: 7rem; }
  [data-poster-size="large"] .poster-frame { --poster-width: 10rem; }

  .poster-frame :global(.entity-thumbnail) {
    width: 100%;
    border: 0;
    background: #050505;
    box-shadow: none;
    transform: none;
  }

  .poster-frame :global(.entity-thumbnail::after) {
    display: none;
  }

  .poster-frame :global(.media) {
    border-bottom: 0;
  }

  .hero-text {
    display: grid;
    gap: 0.4rem;
    min-width: 0;
    flex: 1;
  }

  /* ── Action row (flags left, actions right) ──────────── */

  .action-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 0.5rem;
    width: 100%;
  }

  .action-badges {
    display: flex;
    align-items: center;
    gap: 0.35rem;
  }

  .action-group {
    display: flex;
    align-items: center;
    gap: 0.35rem;
  }

  @media (max-width: 480px) {
    .action-row {
      flex-direction: column;
      align-items: flex-start;
    }

    .action-group {
      flex-direction: column;
      align-items: flex-start;
    }
  }

  .action-badge {
    display: grid;
    place-items: center;
    width: 1.75rem;
    height: 1.75rem;
    padding: 0;
    border: 1px solid var(--detail-border);
    border-radius: var(--radius-xs, 4px);
    background: rgba(255, 255, 255, 0.04);
    color: var(--detail-text-disabled);
    cursor: pointer;
    transition: color 0.2s, border-color 0.2s, box-shadow 0.2s, transform 0.2s;
  }

  .action-badge:focus {
    outline: none;
  }

  .action-badge:disabled {
    cursor: default;
    opacity: 0.5;
  }


  /* Favorite — red when active */
  .action-badge.favorite.active {
    color: #e06070;
    border-color: rgba(224, 96, 112, 0.5);
    box-shadow: 0 0 10px rgba(224, 96, 112, 0.2);
  }

  .action-badge.favorite.animating {
    animation: badge-pop 0.35s cubic-bezier(0.175, 0.885, 0.32, 1.275);
  }

  /* NSFW — red fire, display only */
  .action-badge.nsfw {
    cursor: default;
    color: #e06070;
    border-color: rgba(224, 96, 112, 0.5);
    box-shadow: 0 0 8px rgba(224, 96, 112, 0.15);
    user-select: none;
    -webkit-user-select: none;
    pointer-events: none;
  }

  /* ── Action buttons (edit, identify) ────────────────── */

  .action-btn {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    padding: 0.35rem 0.65rem;
    border: 1px solid var(--detail-border);
    border-radius: var(--radius-xs, 4px);
    background: rgba(255, 255, 255, 0.04);
    color: var(--detail-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    font-weight: 600;
    letter-spacing: 0.03em;
    cursor: pointer;
    transition: color 0.2s, border-color 0.2s, box-shadow 0.2s, background 0.2s;
  }

  .action-btn:hover {
    color: var(--detail-accent);
    border-color: var(--detail-accent-muted);
    box-shadow: 0 0 12px var(--detail-accent-glow);
    background: color-mix(in srgb, var(--detail-accent) 6%, transparent);
  }

  .action-btn.editing {
    color: var(--detail-accent);
    border-color: var(--detail-accent-muted);
    box-shadow: 0 0 14px var(--detail-accent-glow);
    background: color-mix(in srgb, var(--detail-accent) 10%, transparent);
  }

  .action-btn:disabled {
    cursor: not-allowed;
    opacity: 0.5;
  }

  /* Organized — green when active */
  .action-badge.organized.active {
    color: #80b898;
    border-color: rgba(78, 138, 98, 0.5);
    box-shadow: 0 0 10px rgba(78, 138, 98, 0.2);
  }

  .action-badge.organized.animating {
    animation: badge-pop 0.35s cubic-bezier(0.175, 0.885, 0.32, 1.275);
  }

  @keyframes badge-pop {
    0% { transform: scale(1); }
    40% { transform: scale(1.3); }
    100% { transform: scale(1); }
  }

  .hero-title {
    margin: 0;
    max-width: 100%;
    min-width: 0;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: clamp(1.4rem, 3vw, 2rem);
    font-weight: 700;
    line-height: 1.15;
    color: var(--detail-text);
    overflow-wrap: anywhere;
    word-break: normal;
  }

  /* ── Meta row (studio · date · count) ─────────────────── */

  .meta-row {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.15rem 0;
    font-size: 0.82rem;
    color: var(--detail-text-muted);
  }

  /* ── Rating (in hero) ──────────────────────────────────── */

  .rating-row {
    display: flex;
    gap: 0.15rem;
  }

  .rating-star {
    display: grid;
    height: 1.75rem;
    width: 1.75rem;
    place-items: center;
    padding: 0;
    border: none;
    background: transparent;
    color: var(--detail-text-disabled);
    cursor: pointer;
    transition: color 0.15s, filter 0.15s;
  }

  .rating-star.active {
    color: var(--detail-accent);
    filter: drop-shadow(0 0 6px var(--detail-accent-glow));
  }

  .rating-star:focus {
    outline: none;
  }

  .rating-star.star-fill {
    animation: star-roll-in 0.3s cubic-bezier(0.175, 0.885, 0.32, 1.275) backwards;
  }

  .rating-star.star-clear {
    animation: star-pop-out 0.3s ease-out;
  }

  @keyframes star-roll-in {
    0% {
      transform: scale(0) rotate(-90deg);
      opacity: 0;
    }
    60% {
      transform: scale(1.25) rotate(10deg);
      opacity: 1;
    }
    100% {
      transform: scale(1) rotate(0deg);
      opacity: 1;
    }
  }

  @keyframes star-pop-out {
    0% {
      transform: scale(1);
    }
    35% {
      transform: scale(1.35);
    }
    100% {
      transform: scale(1);
    }
  }

  .rating-star:disabled {
    cursor: default;
    opacity: 0.7;
  }

  .position-badges {
    display: flex;
    gap: 0.4rem;
    flex-wrap: wrap;
  }

  /* ── Detail Body ────────────────────────────────────────── */

  .detail-tabs {
    min-width: 0;
  }

  .detail-tab-list {
    display: flex;
    gap: 0.35rem;
    min-width: 0;
    overflow-x: auto;
    padding: 0.65rem 1.5rem;
    border-bottom: 1px solid var(--detail-border);
    border-radius: 0 0 var(--radius-md, 10px) var(--radius-md, 10px);
    background: var(--detail-glass);
    backdrop-filter: blur(var(--detail-glass-blur));
    -webkit-backdrop-filter: blur(var(--detail-glass-blur));
    scrollbar-width: thin;
  }

  .detail-tab-list button {
    display: inline-flex;
    align-items: center;
    gap: 0.45rem;
    min-height: 2rem;
    padding: 0.35rem 0.75rem;
    border: 1px solid transparent;
    border-radius: var(--radius-xs, 4px);
    background: transparent;
    color: var(--detail-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.72rem;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    white-space: nowrap;
    cursor: pointer;
    transition: color 0.15s, border-color 0.15s, background 0.15s, box-shadow 0.15s;
  }

  .detail-tab-list button:hover {
    color: var(--detail-text);
    border-color: var(--color-border-subtle, rgba(148, 158, 178, 0.07));
    background: var(--detail-surface-raised);
  }

  .detail-tab-list button.active {
    color: var(--detail-accent);
    border-color: var(--detail-accent-muted);
    background: color-mix(in srgb, var(--detail-accent) 8%, var(--detail-surface-raised));
    box-shadow: 0 0 14px var(--detail-accent-glow);
  }

  .detail-tab-list strong {
    color: var(--detail-text-disabled);
    font-size: 0.65rem;
    font-weight: 600;
  }

  .detail-tab-panel {
    min-width: 0;
  }

  .detail-edit-toolbar {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 0.5rem;
    padding: 0.5rem 1.5rem;
    border-bottom: 1px solid var(--detail-border);
    border-radius: 0 0 var(--radius-md, 10px) var(--radius-md, 10px);
    background: var(--detail-glass);
    backdrop-filter: blur(var(--detail-glass-blur));
    -webkit-backdrop-filter: blur(var(--detail-glass-blur));
  }

  .detail-edit-actions,
  .edit-confirm-actions {
    display: flex;
    align-items: center;
    justify-content: end;
    gap: 0.5rem;
    flex-wrap: wrap;
  }

  .edit-action {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.4rem;
    min-height: 2rem;
    border: 1px solid var(--detail-border);
    border-radius: var(--radius-xs, 4px);
    padding: 0.35rem 0.7rem;
    background: var(--detail-surface-raised);
    color: var(--detail-text-secondary);
    cursor: pointer;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 700;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    transition: color 0.15s, border-color 0.15s, background 0.15s, box-shadow 0.15s, opacity 0.15s;
  }

  .edit-action.primary {
    color: var(--detail-accent);
    border-color: var(--detail-accent-muted);
    background: color-mix(in srgb, var(--detail-accent) 8%, var(--detail-surface-raised));
    box-shadow: 0 0 14px var(--detail-accent-glow);
  }

  .edit-action.secondary {
    color: var(--detail-text-muted);
  }

  .edit-action:disabled {
    cursor: not-allowed;
    opacity: 0.45;
    box-shadow: none;
  }

  .edit-errors {
    display: grid;
    gap: 0.25rem;
    padding: 0.65rem 1.5rem;
    border-bottom: 1px solid color-mix(in srgb, #ef4444 45%, var(--detail-border));
    border-radius: var(--radius-xs, 4px);
    background: color-mix(in srgb, #ef4444 8%, var(--detail-surface));
    color: #fca5a5;
    font-size: 0.78rem;
  }

  .edit-errors p {
    margin: 0;
  }

  .detail-tab-sections {
    display: grid;
    gap: 1rem;
    min-width: 0;
    padding: 1rem 1.5rem 1.5rem;
  }

  .detail-tab-sections[data-layout="grid"] {
    grid-template-columns: repeat(auto-fit, minmax(min(100%, 18rem), 1fr));
  }

  .detail-tab-sections .detail-body {
    padding: 0;
  }

  .detail-tab-sections .detail-section {
    padding: 0 0 1rem;
  }

  .custom-detail-section {
    min-width: 0;
  }

  .tab-empty-state {
    padding: 1rem;
    border: 1px solid var(--detail-border);
    border-radius: var(--radius-xs, 4px);
    background: var(--detail-surface);
    color: var(--detail-text-muted);
    font-size: 0.82rem;
  }

  .detail-body {
    display: grid;
    gap: 0;
    padding: 1rem 1.5rem 1.5rem;
  }

  /* ── Description (markdown) ─────────────────────────────── */

  .description-content {
    color: var(--detail-text-secondary);
    font-size: 0.88rem;
    line-height: 1.65;
    padding: 0.5rem 0 1rem;
  }

  .description-content :global(p) {
    margin: 0 0 0.65rem;
  }

  .description-content :global(p:last-child) {
    margin-bottom: 0;
  }

  .description-content :global(a) {
    color: var(--detail-accent);
    text-decoration: underline;
    text-decoration-color: var(--detail-accent-muted);
    text-underline-offset: 2px;
  }

  .description-content :global(a:hover) {
    text-decoration-color: var(--detail-accent);
  }

  .description-content :global(strong) {
    color: var(--detail-text);
    font-weight: 600;
  }

  .description-content :global(em) {
    font-style: italic;
  }

  .description-content :global(code) {
    padding: 0.1em 0.35em;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.82em;
    color: var(--detail-text);
    background: var(--detail-surface-raised);
    border: 1px solid var(--detail-border);
    border-radius: 3px;
  }

  .description-content :global(pre) {
    margin: 0.65rem 0;
    padding: 0.75rem 1rem;
    background: var(--detail-surface-raised);
    border: 1px solid var(--detail-border);
    border-radius: var(--radius-xs, 4px);
    overflow-x: auto;
  }

  .description-content :global(pre code) {
    padding: 0;
    border: none;
    background: none;
  }

  .description-content :global(ul),
  .description-content :global(ol) {
    margin: 0.5rem 0;
    padding-left: 1.4rem;
  }

  .description-content :global(li) {
    margin-bottom: 0.25rem;
  }

  .description-content :global(blockquote) {
    margin: 0.65rem 0;
    padding: 0.5rem 1rem;
    border-left: 3px solid var(--detail-accent-muted);
    color: var(--detail-text-muted);
    font-style: italic;
  }

  .description-content :global(h1),
  .description-content :global(h2),
  .description-content :global(h3),
  .description-content :global(h4) {
    margin: 1rem 0 0.5rem;
    font-family: var(--font-heading, Geist, sans-serif);
    color: var(--detail-text);
  }

  .description-content :global(h1) { font-size: 1.2rem; }
  .description-content :global(h2) { font-size: 1.05rem; }
  .description-content :global(h3) { font-size: 0.95rem; }

  .description-content :global(hr) {
    border: none;
    border-top: 1px solid var(--detail-border);
    margin: 1rem 0;
  }

  /* ── Metadata sections ──────────────────────────────────── */

  .metadata-sections {
    padding: 0 1.5rem 1.5rem;
    border-top: 1px solid var(--detail-border);
  }

  .detail-section {
    padding: 1rem 0;
    border-bottom: 1px solid var(--detail-border);
  }

  .detail-section:last-child {
    border-bottom: none;
    padding-bottom: 0;
  }

  .edit-section {
    display: grid;
    gap: 0.75rem;
  }

  .edit-field {
    display: grid;
    gap: 0.3rem;
    min-width: 0;
  }

  .edit-field-label {
    color: var(--detail-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 700;
    letter-spacing: 0.06em;
    text-transform: uppercase;
  }

  .edit-inline-grid {
    display: grid;
    gap: 0.75rem;
  }

  @media (min-width: 720px) {
    .edit-inline-grid {
      grid-template-columns: minmax(8rem, 0.55fr) minmax(12rem, 1fr);
      align-items: start;
    }
  }

  .edit-flag-chips {
    display: flex;
    flex-wrap: wrap;
    gap: 0.35rem;
    min-height: 2.55rem;
    align-items: center;
  }

  .edit-confirm-backdrop {
    position: fixed;
    inset: 0;
    z-index: 80;
    display: grid;
    place-items: center;
    padding: 1rem;
    background: rgba(0, 0, 0, 0.58);
    backdrop-filter: blur(8px);
  }

  .edit-confirm {
    width: min(100%, 24rem);
    border: 1px solid var(--detail-border, #1c2235);
    border-radius: var(--radius-sm, 6px);
    background: rgba(12, 15, 21, 0.98);
    backdrop-filter: blur(24px);
    -webkit-backdrop-filter: blur(24px);
    color: var(--detail-text-secondary, #c4c9d4);
    padding: 1rem;
    box-shadow: 0 8px 40px rgba(0, 0, 0, 0.6);
  }

  .edit-confirm h2 {
    margin: 0;
    color: var(--detail-text, #f2eed8);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1rem;
    letter-spacing: 0;
  }

  .edit-confirm p {
    margin: 0.45rem 0 1rem;
    color: var(--detail-text-muted, #8a93a6);
    font-size: 0.82rem;
  }

  .section-label {
    display: flex;
    align-items: center;
    gap: 0.45rem;
    margin: 0 0 0.75rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--detail-text-muted);
  }

  .tab-data-list {
    display: grid;
    gap: 0;
    min-width: 0;
  }

  .tab-data-row {
    display: grid;
    grid-template-columns: minmax(5.5rem, max-content) minmax(0, 1fr);
    gap: 0.8rem;
    align-items: baseline;
    min-width: 0;
    padding: 0.55rem 0;
    border-bottom: 1px solid color-mix(in srgb, var(--detail-border) 56%, transparent);
    font-size: 0.82rem;
  }

  .tab-data-row:last-child {
    border-bottom: none;
  }

  .tab-data-row span {
    color: var(--detail-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.7rem;
    font-weight: 600;
    letter-spacing: 0.04em;
    text-transform: uppercase;
  }

  .tab-data-row strong {
    min-width: 0;
    color: var(--detail-text-secondary);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.76rem;
    font-weight: 500;
    overflow-wrap: anywhere;
  }

  .reference-list {
    display: flex;
    flex-wrap: nowrap;
    gap: 0.75rem;
    width: 100%;
    max-width: 100%;
    min-width: 0;
    overflow-x: auto;
    overflow-y: hidden;
    padding-bottom: 0.35rem;
    scroll-padding-inline: 0.25rem;
    scrollbar-width: thin;
  }

  .reference-list.is-horizontal-rail {
    align-items: stretch;
  }

  .reference-thumbnail {
    flex: 0 0 clamp(7rem, 33vw, 8.75rem);
    min-width: 0;
  }

  /* ── Links ──────────────────────────────────────────────── */

  .link-list {
    display: grid;
    gap: 0.35rem;
  }

  .link-item {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    padding: 0.35rem 0.6rem;
    border: 1px solid var(--detail-border);
    border-radius: var(--radius-xs, 4px);
    background: var(--detail-surface-raised);
    color: var(--detail-text-secondary);
    font-size: 0.82rem;
    text-decoration: none;
    transition: border-color 0.15s, color 0.15s, box-shadow 0.15s;
  }

  a.link-item:hover {
    color: var(--detail-accent);
    border-color: var(--detail-accent-muted);
    box-shadow: 0 0 10px var(--detail-accent-glow);
  }

  .link-item.no-url {
    color: var(--detail-text-muted);
  }

  /* ── Shared ─────────────────────────────────────────────── */

  /* ── Responsive ─────────────────────────────────────────── */

  @media (min-width: 640px) {
    .entity-detail {
      --hero-banner-max-height: clamp(14rem, 32vw, 22rem);
      --hero-lower-overlap: clamp(-4.25rem, -6vw, -2.5rem);
    }

    .hero-content {
      padding: 2rem;
      padding-top: 3rem;
    }

    [data-poster-size="small"] .poster-frame { --poster-width: 6rem; }
    [data-poster-size="medium"] .poster-frame { --poster-width: 9rem; }
    [data-poster-size="large"] .poster-frame { --poster-width: 13rem; }

    .detail-body {
      padding: 1.25rem 2rem 2rem;
    }

    .metadata-sections {
      padding: 0 2rem 2rem;
    }

    .detail-tab-list {
      padding-inline: 2rem;
    }

    .detail-tab-sections {
      padding: 1.25rem 2rem 2rem;
    }
  }

  @media (min-width: 1024px) {
    .entity-detail {
      --hero-banner-max-height: clamp(15rem, 26vw, 24rem);
      --hero-lower-overlap: clamp(-4.75rem, -5vw, -3rem);
    }

    [data-poster-size="small"] .poster-frame { --poster-width: 7rem; }
    [data-poster-size="medium"] .poster-frame { --poster-width: 11rem; }
    [data-poster-size="large"] .poster-frame { --poster-width: 16rem; }

    h1 {
      font-size: 2.2rem;
    }
  }
</style>
