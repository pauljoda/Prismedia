<script lang="ts">
  import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
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
    Image as ImageIcon,
    Link,
    ListOrdered,
    LoaderCircle,
    MonitorCog,
    Pencil,
    PencilOff,
    Play,
    Save,
    Trash2,
    Upload,
    Users,
    X,
  } from "@lucide/svelte";
  import type { LucideIcon } from "@lucide/svelte";
  import type { EntityDetailCard, EntityDetailCardFull, EntityDetailCredit, EntityDetailLink } from "$lib/entities/entity-detail";
  import { renderEntityDescriptionMarkdown } from "$lib/entities/entity-detail-markdown";
  import { hasHero, hasPoster } from "$lib/entities/entity-detail";
  import {
    entityReferenceToThumbnailCard,
    placeholderGradient,
    toAspectRatioValue,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-thumbnail";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import MetadataCard from "$lib/components/MetadataCard.svelte";
  import MetadataCardGrid from "$lib/components/MetadataCardGrid.svelte";
  import EntityTagChips from "./EntityTagChips.svelte";
  import EntityDetailReferenceRail from "./EntityDetailReferenceRail.svelte";
  import MarkdownEditor from "$lib/components/forms/MarkdownEditor.svelte";
  import EntityPicker from "$lib/components/forms/EntityPicker.svelte";
  import ListEditor from "$lib/components/forms/ListEditor.svelte";
  import KeyValueEditor from "$lib/components/forms/KeyValueEditor.svelte";
  import FormField from "$lib/components/forms/FormField.svelte";
  import ToggleChip from "$lib/components/forms/ToggleChip.svelte";
  import TextField from "$lib/components/forms/TextField.svelte";
  import { getImagesCapability, isNsfw as hasNsfwCapability } from "$lib/api/capabilities";
  import { clearEntityImageAsset, uploadEntityImageAsset } from "$lib/api/entity-mutations";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { ENTITY_FILE_ROLE, type EntityFileRoleCode } from "$lib/entities/entity-codes";
  import {
    draftFromCard,
    serializeDraft,
    buildMetadataUpdate,
    validateDraft,
    validateUrl,
    hasProvider,
    externalIdValue,
    type EntityDetailEditDraft,
  } from "$lib/entities/entity-detail-edit";
  import { searchTags, searchPeople, searchStudios } from "$lib/entities/entity-detail-search";

  export type EntityDetailPosterSize = "none" | "small" | "medium" | "large";

  export interface EntityDetailTab {
    id: string;
    label: string;
    count?: number;
    icon?: LucideIcon;
    sections: string[];
    layout?: "stack" | "grid";
  }

  export type EntityDetailActionVariant = "default" | "primary" | "danger";

  export interface EntityDetailActionButton {
    id: string;
    label: string;
    icon: LucideIcon;
    onClick?: () => void | Promise<void>;
    href?: string;
    title?: string;
    ariaLabel?: string;
    disabled?: boolean;
    active?: boolean;
    hidden?: boolean;
    variant?: EntityDetailActionVariant;
    iconClass?: string;
    iconFill?: string;
  }

  export interface EntityDetailSection {
    id: string;
    label?: string;
    count?: number;
    icon?: LucideIcon;
    editable?: boolean;
    hidden?: boolean;
  }

  import type { EntityMetadataPatch as _Patch, EntityMetadataUpdateRequest as _Request } from "$lib/entities/entity-detail-edit";
  export type EntityMetadataPatch = _Patch;
  export type EntityMetadataUpdateRequest = _Request;

  const DEFAULT_STANDALONE_METADATA_SECTION_IDS = [
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

  interface Props {
    card: EntityDetailCard;
    onRatingChange?: (value: number | null) => void;
    onFavoriteToggle?: () => void;
    onOrganizedToggle?: () => void;
    peopleLabel?: string;
    posterSize?: EntityDetailPosterSize;
    ratingBusy?: boolean;
    showHero?: boolean;
    tabs?: EntityDetailTab[];
    /** Built-in lower metadata sections used when this route does not provide tabs. */
    standaloneMetadataSectionIds?: string[];
    onMetadataSave?: (request: EntityMetadataUpdateRequest) => void | Promise<void>;
    onImageAssetUpload?: (role: EntityFileRoleCode, file: File) => void | Promise<void>;
    onImageAssetClear?: (role: EntityFileRoleCode) => void | Promise<void>;
    /** Route-provided sections that can be assigned to any tab. */
    sections?: EntityDetailSection[];
    /** Inline metadata rendered below the title (e.g. studio link · date · count). */
    heroMeta?: Snippet;
    /** Badge row rendered below the rating stars (e.g. Season 1, Episode 2). */
    heroBadges?: Snippet;
    /** Action buttons rendered in the right-aligned actions group. */
    actionButtons?: EntityDetailActionButton[];
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
    peopleLabel = "Cast & Crew",
    posterSize = "medium",
    ratingBusy = false,
    showHero = true,
    tabs = [],
    standaloneMetadataSectionIds = DEFAULT_STANDALONE_METADATA_SECTION_IDS,
    onMetadataSave,
    onImageAssetUpload,
    onImageAssetClear,
    sections = [],
    heroMeta,
    heroBadges,
    actionButtons = [],
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
  let assetError = $state<string | null>(null);
  let assetBusyRole = $state<EntityFileRoleCode | null>(null);
  let posterInput: HTMLInputElement | null = $state(null);
  let headerInput: HTMLInputElement | null = $state(null);
  let localPosterAsset = $state<{ src: string | null; empty: boolean } | null>(null);
  let localHeaderAsset = $state<{ src: string | null; empty: boolean } | null>(null);
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

  const renderedDescription = $derived(renderEntityDescriptionMarkdown(card.description));
  const hasStandaloneBodyContent = $derived(Boolean(renderedDescription) || card.tags.length > 0);
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
  const cardFull = $derived(card as EntityDetailCard & Partial<EntityDetailCardFull>);
  const urlLinks = $derived(card.links.filter((link) => !hasProvider(link)));
  const providerIdLinks = $derived(card.links.filter(hasProvider));
  const visibleActionButtons = $derived.by(() => actionButtons.filter((action) => !action.hidden));
  const visibleTabs = $derived.by(() => tabs.filter(tabHasDisplayContent));
  const hasTabs = $derived(visibleTabs.length > 0);
  const activeTab = $derived(visibleTabs.find((tab) => tab.id === activeTabId) ?? visibleTabs[0] ?? null);
  const activeTabSections = $derived(activeTab ? sectionsForTab(activeTab) : []);
  const standaloneMetadataSections = $derived.by(() =>
    standaloneMetadataSectionIds
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .filter(sectionHasContent),
  );
  const isEditingActiveTab = $derived(
    hasTabs ? Boolean(activeTab && editingTabId === activeTab.id) : editingTabId === "__standalone__",
  );
  const effectiveShowHero = $derived(showHero || isEditingActiveTab);
  const displayHero = $derived.by(() => {
    if (localHeaderAsset) return localHeaderAsset.empty || !localHeaderAsset.src ? null : { src: localHeaderAsset.src, alt: "Header" };
    return card.hero;
  });
  const displayPoster = $derived.by(() => {
    if (localPosterAsset) return localPosterAsset.empty || !localPosterAsset.src ? null : { src: localPosterAsset.src, alt: "Poster" };
    return card.poster;
  });
  const heroMode = $derived.by((): HeroMode => {
    if (!effectiveShowHero) return "gradient";
    if (displayHero) return "image";
    if (displayPoster) return "poster-blur";
    return "gradient";
  });
  const effectivePosterSize = $derived(isEditingActiveTab && posterSize === "none" ? "medium" : posterSize);
  const posterCard = $derived.by(() => posterCardForDisplay());
  const posterFrameAspectRatio = $derived(posterCard ? toAspectRatioValue(posterCard.aspectRatio) : undefined);
  const posterVisible = $derived(effectivePosterSize !== "none" && (posterCard !== null || isEditingActiveTab));
  const posterHasAsset = $derived(Boolean(displayPoster));
  const headerHasAsset = $derived(Boolean(displayHero));
  const imagesCapability = $derived(getImagesCapability(card.entity.capabilities));
  const canManageImages = $derived(Boolean(onMetadataSave || onImageAssetUpload || onImageAssetClear));
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
  const editValidationErrors = $derived.by(() => validateDraft(currentEditSections, editDraft, card.rating?.max ?? 5));
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

    return sectionHasDisplayContent(section);
  }

  function sectionHasDisplayContent(section: EntityDetailSection): boolean {
    if (section.hidden) return false;

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

  function tabHasDisplayContent(tab: EntityDetailTab): boolean {
    return tab.sections
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .some(sectionHasDisplayContent);
  }

  function sectionsForTab(tab: EntityDetailTab): EntityDetailSection[] {
    return tab.sections
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .filter(sectionHasContent);
  }

  function urlLinkTitle(link: EntityDetailLink): string {
    const parsed = parseUrl(link.url ?? link.label);
    if (!parsed) return link.label;
    return parsed.hostname.replace(/^www\./i, "");
  }

  function urlLinkSubtitle(link: EntityDetailLink): string {
    return link.url ?? link.label;
  }

  function parseUrl(value: string): URL | null {
    try {
      return new URL(value);
    } catch {
      return null;
    }
  }

  function startEdit(tab?: EntityDetailTab) {
    const nextDraft = draftFromCard(card, { isFavorite, isNsfw, isOrganized });
    initialDraft = { ...nextDraft };
    editDraft = { ...nextDraft };
    editingTabId = tab?.id ?? "__standalone__";
    editError = null;
    assetError = null;
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

  function posterCardForDisplay(): EntityThumbnailCard | null {
    const poster = displayPoster;
    if (poster) {
      return {
        ...(card.posterCard ?? entityReferenceToThumbnailCard(card.entity)),
        cover: { src: poster.src, alt: poster.alt, role: ENTITY_FILE_ROLE.poster },
        hover: { kind: THUMBNAIL_HOVER_KIND.none },
      };
    }

    if (!isEditingActiveTab) return card.posterCard ?? null;
    return {
      ...entityReferenceToThumbnailCard(card.entity, { cover: null }),
      hover: { kind: THUMBNAIL_HOVER_KIND.none },
    };
  }

  function roleSupported(role: EntityFileRoleCode): boolean {
    const supportedKinds = imagesCapability?.supportedKinds ?? [];
    return supportedKinds.length === 0 || supportedKinds.includes(role);
  }

  function inputForRole(role: EntityFileRoleCode): HTMLInputElement | null {
    return role === ENTITY_FILE_ROLE.backdrop ? headerInput : posterInput;
  }

  function openAssetPicker(role: EntityFileRoleCode) {
    inputForRole(role)?.click();
  }

  async function handleAssetInput(role: EntityFileRoleCode, event: Event) {
    const input = event.currentTarget as HTMLInputElement;
    const file = input.files?.[0];
    input.value = "";
    if (file) await uploadAsset(role, file);
  }

  async function handleAssetDrop(role: EntityFileRoleCode, event: DragEvent) {
    event.preventDefault();
    const file = event.dataTransfer?.files?.[0];
    if (file) await uploadAsset(role, file);
  }

  function preventAssetDrag(event: DragEvent) {
    event.preventDefault();
    if (event.dataTransfer) event.dataTransfer.dropEffect = "copy";
  }

  async function uploadAsset(role: EntityFileRoleCode, file: File) {
    if (assetBusyRole) return;
    assetBusyRole = role;
    assetError = null;
    try {
      await (onImageAssetUpload
        ? onImageAssetUpload(role, file)
        : uploadEntityImageAsset(card.entity.id, role, file));
      applyImageAssetResult(role, URL.createObjectURL(file));
    } catch (err) {
      assetError = err instanceof Error ? err.message : String(err);
    } finally {
      assetBusyRole = null;
    }
  }

  async function clearAsset(role: EntityFileRoleCode) {
    if (assetBusyRole) return;
    assetBusyRole = role;
    assetError = null;
    try {
      await (onImageAssetClear
        ? onImageAssetClear(role)
        : clearEntityImageAsset(card.entity.id, role));
      applyImageAssetResult(role, null);
    } catch (err) {
      assetError = err instanceof Error ? err.message : String(err);
    } finally {
      assetBusyRole = null;
    }
  }

  function applyImageAssetResult(role: EntityFileRoleCode, nextAsset: string | null) {
    if (role === ENTITY_FILE_ROLE.backdrop) {
      localHeaderAsset = nextAsset ? { src: nextAsset, empty: false } : { src: null, empty: true };
    } else {
      localPosterAsset = nextAsset ? { src: nextAsset, empty: false } : { src: null, empty: true };
    }
  }

  function assetBusy(role: EntityFileRoleCode): boolean {
    return assetBusyRole === role;
  }
</script>

{#snippet assetBusyOverlay(role: EntityFileRoleCode)}
  {#if assetBusy(role)}
    <div class="asset-busy-overlay" role="status" aria-label="Uploading artwork">
      <LoaderCircle class="asset-busy-spinner h-6 w-6" />
    </div>
  {/if}
{/snippet}

{#snippet imageAssetActions(role: EntityFileRoleCode, label: "poster" | "header", hasAsset: boolean)}
  {#if isEditingActiveTab && canManageImages && roleSupported(role)}
    <div class="image-asset-actions">
      <button
        type="button"
        class="image-asset-btn"
        onclick={() => openAssetPicker(role)}
        disabled={assetBusy(role)}
        aria-label={`Upload ${label}`}
      >
        <Upload class="h-3.5 w-3.5" />
        <span>{assetBusy(role) ? "Uploading" : "Upload"}</span>
      </button>
      {#if hasAsset}
        <button
          type="button"
          class="image-asset-btn"
          onclick={() => void clearAsset(role)}
          disabled={assetBusy(role)}
          aria-label={`Clear ${label}`}
        >
          <Trash2 class="h-3.5 w-3.5" />
          <span>Clear</span>
        </button>
      {/if}
    </div>
  {/if}
{/snippet}

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
    <MetadataCard title="Links & Provider IDs" icon={Link} wide capped>
      {#if urlLinks.length > 0}
        <div class="link-group">
          <div class="link-group-label">URLs</div>
          <div class="link-list">
            {#each urlLinks as link (link.label)}
              {@const title = urlLinkTitle(link)}
              {@const subtitle = urlLinkSubtitle(link)}
              {#if link.url}
                <a href={link.url} target="_blank" rel="noopener noreferrer" class="link-item url-link-item" title={subtitle}>
                  <span class="url-link-icon" aria-hidden="true">
                    <ExternalLink class="h-3.5 w-3.5" />
                  </span>
                  <span class="url-link-copy">
                    <span class="url-link-title">{title}</span>
                    <span class="url-link-subtitle">{subtitle}</span>
                  </span>
                </a>
              {:else}
                <span class="link-item url-link-item no-url" title={subtitle}>
                  <span class="url-link-icon" aria-hidden="true">
                    <Link class="h-3.5 w-3.5" />
                  </span>
                  <span class="url-link-copy">
                    <span class="url-link-title">{title}</span>
                    <span class="url-link-subtitle">{subtitle}</span>
                  </span>
                </span>
              {/if}
            {/each}
          </div>
        </div>
      {/if}
      {#if providerIdLinks.length > 0}
        <div class="link-group">
          <div class="link-group-label">Provider IDs</div>
          <div class="link-list">
            {#each providerIdLinks as link (`${link.provider}:${externalIdValue(link.label, link.provider)}`)}
              {@const externalValue = externalIdValue(link.label, link.provider)}
              {#if link.url}
                <a href={link.url} target="_blank" rel="noopener noreferrer" class="link-item provider-id-item">
                  <ExternalLink class="h-3.5 w-3.5" />
                  <span class="provider-id-provider">{link.provider}</span>
                  <span class="provider-id-value">{externalValue}</span>
                </a>
              {:else}
                <span class="link-item provider-id-item no-url">
                  <Fingerprint class="h-3.5 w-3.5" />
                  <span class="provider-id-provider">{link.provider}</span>
                  <span class="provider-id-value">{externalValue}</span>
                </span>
              {/if}
            {/each}
          </div>
        </div>
      {/if}
    </MetadataCard>
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
      <FormField label="Flags">
        <div class="edit-flag-chips">
          <ToggleChip value={editDraft.isFavorite} onChange={(v) => (editDraft.isFavorite = v)} onLabel="Favorite" icon={Heart} />
          <ToggleChip value={editDraft.isNsfw} onChange={(v) => (editDraft.isNsfw = v)} onLabel="NSFW" variant="warning" icon={Flame} />
          <ToggleChip value={editDraft.isOrganized} onChange={(v) => (editDraft.isOrganized = v)} onLabel="Organized" icon={CheckCircle} />
        </div>
      </FormField>
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
      label={peopleLabel}
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
      keyPlaceholder="date"
      valuePlaceholder="YYYY-MM-DD"
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
      keyPlaceholder="count"
      valuePlaceholder="12"
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
      keyPlaceholder="sort"
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
      placeholder="e.g. complete, draft, archived"
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
    <FormField label="Flags">
      <div class="edit-flag-chips">
        <ToggleChip value={editDraft.isFavorite} onChange={(v) => (editDraft.isFavorite = v)} onLabel="Favorite" icon={Heart} />
        <ToggleChip value={editDraft.isNsfw} onChange={(v) => (editDraft.isNsfw = v)} onLabel="NSFW" variant="warning" icon={Flame} />
        <ToggleChip value={editDraft.isOrganized} onChange={(v) => (editDraft.isOrganized = v)} onLabel="Organized" icon={CheckCircle} />
      </div>
    </FormField>
  </section>
{/snippet}

{#snippet studioSection()}
  {#if cardFull.studio}
    <EntityDetailReferenceRail icon={Building2} title="Studio" references={[cardFull.studio]} />
  {/if}
{/snippet}

{#snippet creditsSection()}
  {#if (cardFull.credits?.length ?? 0) > 0}
    <EntityDetailReferenceRail icon={Users} title="Credits" references={cardFull.credits ?? []} />
  {/if}
{/snippet}

{#snippet statsSection()}
  {#if (cardFull.stats?.length ?? 0) > 0}
    <MetadataCard
      title="Stats"
      icon={BarChart3}
      rows={(cardFull.stats ?? []).map((r) => ({ label: r.label, value: r.value }))}
    />
  {/if}
{/snippet}

{#snippet datesSection()}
  {#if (cardFull.dates?.length ?? 0) > 0}
    <MetadataCard
      title="Dates"
      icon={Calendar}
      rows={(cardFull.dates ?? []).map((r) => ({ label: r.label, value: r.value }))}
    />
  {/if}
{/snippet}

{#snippet technicalSection()}
  {#if (cardFull.technical?.length ?? 0) > 0}
    <MetadataCard
      title="Technical"
      icon={MonitorCog}
      rows={(cardFull.technical ?? []).map((r) => ({ label: r.label, value: r.value }))}
    />
  {/if}
{/snippet}

{#snippet progressSection()}
  {#if cardFull.progress}
    {@const rows = [
      { label: "Progress", value: `${cardFull.progress.index} / ${cardFull.progress.total} ${cardFull.progress.unit}` },
      { label: "Percent", value: `${cardFull.progress.percent}%` },
      ...(cardFull.progress.mode ? [{ label: "Mode", value: cardFull.progress.mode }] : []),
    ]}
    <MetadataCard title="Progress" icon={Play} {rows} />
  {/if}
{/snippet}

{#snippet positionsSection()}
  {#if (cardFull.positions?.length ?? 0) > 0}
    <MetadataCard
      title="Positions"
      icon={ListOrdered}
      rows={(cardFull.positions ?? []).map((r) => ({ label: r.code, value: r.label }))}
    />
  {/if}
{/snippet}

{#snippet classificationSection()}
  {#if cardFull.classification}
    <MetadataCard
      title="Classification"
      icon={Badge}
      rows={[{ label: cardFull.classification.label, value: cardFull.classification.value }]}
    />
  {/if}
{/snippet}

{#snippet sourceSection()}
  {#if (cardFull.sources?.length ?? 0) > 0 || (cardFull.fingerprints?.length ?? 0) > 0}
    <MetadataCard title="Source" icon={Database}
      rows={[
        ...(cardFull.sources ?? []).map((s) => ({ label: s.code, value: s.value })),
        ...(cardFull.fingerprints ?? []).map((f) => ({ label: String(f.algorithm), value: f.value })),
      ]}
    />
  {/if}
{/snippet}

{#snippet sourcesSection()}
  {#if (cardFull.sources?.length ?? 0) > 0}
    <MetadataCard
      title="Sources"
      icon={Database}
      rows={(cardFull.sources ?? []).map((s) => ({ label: s.code, value: s.value }))}
    />
  {/if}
{/snippet}

{#snippet fingerprintsSection()}
  {#if (cardFull.fingerprints?.length ?? 0) > 0}
    <MetadataCard
      title="Fingerprints"
      icon={Fingerprint}
      rows={(cardFull.fingerprints ?? []).map((r) => ({ label: String(r.algorithm), value: r.value }))}
    />
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
  {#if isEditingActiveTab || hasStandaloneBodyContent || afterBody || standaloneMetadataSections.length > 0 || extraSections}
    <div class="detail-content-card detail-content-card--standalone">
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
        {#if editValidationErrors.length > 0 || editError || assetError}
          <div class="edit-errors" aria-live="polite">
            {#each editValidationErrors as error (error)}
              <p>{error}</p>
            {/each}
            {#if editError}
              <p>{editError}</p>
            {/if}
            {#if assetError}
              <p>{assetError}</p>
            {/if}
          </div>
        {/if}
      {/if}

      {#if isEditingActiveTab}
        <div class="detail-body">
          {@render descriptionEditSection()}
          {@render tagsEditSection()}
        </div>
      {:else if hasStandaloneBodyContent}
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

          <MetadataCardGrid>
            {#each standaloneMetadataSections as section (section.id)}
              {@render renderDetailSection(section)}
            {/each}
          </MetadataCardGrid>
        </div>
      {/if}
    </div>
  {/if}
{/snippet}

<input
  bind:this={posterInput}
  class="asset-file-input"
  type="file"
  accept="image/*"
  onchange={(event) => void handleAssetInput(ENTITY_FILE_ROLE.poster, event)}
/>
<input
  bind:this={headerInput}
  class="asset-file-input"
  type="file"
  accept="image/*"
  onchange={(event) => void handleAssetInput(ENTITY_FILE_ROLE.backdrop, event)}
/>

<article class="entity-detail" data-poster-size={effectivePosterSize} data-hero-mode={heroMode}>
  <!-- Hero -->
  <div
    class="hero"
    role="group"
    aria-label="Header artwork"
    data-hero-mode={heroMode}
    data-asset-dropzone={isEditingActiveTab && roleSupported(ENTITY_FILE_ROLE.backdrop) ? ENTITY_FILE_ROLE.backdrop : undefined}
    ondrop={(event) => void handleAssetDrop(ENTITY_FILE_ROLE.backdrop, event)}
    ondragover={preventAssetDrag}
  >

    {#snippet heroContent()}
      <div class="hero-content">
        {#if posterVisible}
          <div
            class="poster-frame"
            class:is-empty={!posterHasAsset}
            role="group"
            aria-label="Poster artwork"
            style:aspect-ratio={posterFrameAspectRatio}
            data-asset-dropzone={isEditingActiveTab && roleSupported(ENTITY_FILE_ROLE.poster) ? ENTITY_FILE_ROLE.poster : undefined}
            ondrop={(event) => void handleAssetDrop(ENTITY_FILE_ROLE.poster, event)}
            ondragover={preventAssetDrag}
          >
            {#if posterCard}
              <EntityThumbnail card={posterCard} linkable={false} mediaOnly={true} />
            {/if}
            {#if isEditingActiveTab && !posterHasAsset}
              <div class="asset-empty-label">
                <ImageIcon class="h-4 w-4" />
                <span>Poster empty</span>
              </div>
            {/if}
            {@render imageAssetActions(ENTITY_FILE_ROLE.poster, "poster", posterHasAsset)}
            {@render assetBusyOverlay(ENTITY_FILE_ROLE.poster)}
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

            {#if canEdit || visibleActionButtons.length > 0}
              <div class="action-group">
                {#if canEdit}
                  {#if isEditingActiveTab}
                    <button
                      type="button"
                      class="entity-action-button entity-action-button-active"
                      onclick={cancelEdit}
                      disabled={savingEdit}
                      aria-label={cancelEditActionLabel}
                    >
                      <PencilOff class="h-3.5 w-3.5" />
                      <span class="entity-action-button-label">Editing</span>
                    </button>
                  {:else}
                    <button
                      type="button"
                      class="entity-action-button"
                      onclick={() => startEdit(activeTab ?? undefined)}
                      aria-label={editActionLabel}
                    >
                      <Pencil class="h-3.5 w-3.5" />
                      <span class="entity-action-button-label">Edit</span>
                    </button>
                  {/if}
                {/if}
                {#each visibleActionButtons as action (action.id)}
                  {@const ActionIcon = action.icon}
                  {#if action.href && !action.disabled}
                    <a
                      class={[
                        "entity-action-button",
                        action.active && "entity-action-button-active",
                        action.variant === "primary" && "entity-action-button-primary",
                        action.variant === "danger" && "entity-action-button-danger",
                      ]}
                      href={action.href}
                      aria-label={action.ariaLabel ?? action.label}
                      title={action.title ?? action.ariaLabel ?? action.label}
                    >
                      <ActionIcon class={action.iconClass ?? "h-3.5 w-3.5"} fill={action.iconFill} />
                      <span class="entity-action-button-label">{action.label}</span>
                    </a>
                  {:else}
                    <button
                      type="button"
                      class={[
                        "entity-action-button",
                        action.active && "entity-action-button-active",
                        action.variant === "primary" && "entity-action-button-primary",
                        action.variant === "danger" && "entity-action-button-danger",
                      ]}
                      disabled={action.disabled}
                      aria-label={action.ariaLabel ?? action.label}
                      title={action.title ?? action.ariaLabel ?? action.label}
                      onclick={() => void action.onClick?.()}
                    >
                      <ActionIcon class={action.iconClass ?? "h-3.5 w-3.5"} fill={action.iconFill} />
                      <span class="entity-action-button-label">{action.label}</span>
                    </button>
                  {/if}
                {/each}

              </div>
            {/if}
          </div>
        </div>
      </div>
    {/snippet}

    {#if isEditingActiveTab && !headerHasAsset}
      <div
        class="header-asset-placeholder"
        style:background-image={placeholderGradient(card.entity.title)}
        aria-hidden="true"
      >
        <ImageIcon class="h-8 w-8" />
      </div>
    {/if}

    {#if isEditingActiveTab}
      <div class="header-asset-panel" class:is-empty={!headerHasAsset}>
        {@render imageAssetActions(ENTITY_FILE_ROLE.backdrop, "header", headerHasAsset)}
      </div>
    {/if}
    {@render assetBusyOverlay(ENTITY_FILE_ROLE.backdrop)}

    {#if heroMode === "image"}
      <!-- Sharp banner, mask fades bottom 10% -->
      <div class="hero-banner">
        <img src={displayHero!.src} alt="Banner" />
      </div>
      <!-- Lower zone: reflection bg + content on top -->
      <div class="hero-lower">
        <div class="hero-reflection">
          <img src={displayHero!.src} alt="" aria-hidden="true" />
        </div>
        <div class="hero-blur-overlay"></div>
        {@render heroContent()}
      </div>
    {:else if heroMode === "poster-blur"}
      <div class="hero-backdrop poster-mode">
        <div class="hero-backdrop-thumbnail">
          {#if posterCard}
            <EntityThumbnail card={posterCard} linkable={false} mediaOnly={true} interactive={false} />
          {/if}
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
        {#each visibleTabs as tab (tab.id)}
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
          class="detail-tab-panel detail-content-card detail-content-card--tabbed"
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
            {#if isEditingActiveTab && (editValidationErrors.length > 0 || editError || assetError)}
              <div class="edit-errors" aria-live="polite">
                {#each editValidationErrors as error (error)}
                  <p>{error}</p>
                {/each}
                {#if editError}
                  <p>{editError}</p>
                {/if}
                {#if assetError}
                  <p>{assetError}</p>
                {/if}
              </div>
            {/if}
          {/if}
          {#key activeTab.id}
            <div class="detail-tab-sections">
              <MetadataCardGrid>
                {#each activeTabSections as section (section.id)}
                  {@render renderDetailSection(section)}
                {/each}
              </MetadataCardGrid>
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
    --detail-accent-muted: var(--color-accent-overlay-medium);
    --detail-accent-glow: var(--color-accent-overlay-subtle);
    --detail-surface: var(--color-surface-2, #101420);
    --detail-surface-raised: var(--color-surface-3, #151a28);
    --detail-border: var(--color-border, #1c2235);
    --detail-text: var(--color-text-primary, #f2eed8);
    --detail-text-secondary: var(--color-text-secondary, #c4c9d4);
    --detail-text-muted: var(--color-text-muted, #8a93a6);
    --detail-text-disabled: var(--color-text-disabled, #4a5260);
    --detail-glass: rgba(12, 15, 21, 0.72);
    --detail-glass-blur: var(--glass-blur-sm);
    --hero-banner-max-height: clamp(13rem, 36vw, 20rem);
    --hero-lower-overlap: clamp(-3.75rem, -6vw, -2rem);
    --detail-slideout-inset: 5px;

    display: grid;
    gap: 0;
    min-width: 0;
    overflow: hidden;
  }

  .entity-detail > * {
    min-width: 0;
  }

  /* ── Hero ────────────────────────────────────────────────── */

  .asset-file-input {
    position: fixed;
    width: 1px;
    height: 1px;
    opacity: 0;
    pointer-events: none;
  }

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

  .hero-backdrop-thumbnail {
    position: absolute;
    inset: -40px;
    transform: scale(1.3);
    filter: blur(15px) saturate(1.3) brightness(0.5);
    will-change: transform;
  }

  .hero-backdrop-thumbnail :global(.entity-thumbnail) {
    width: 100%;
    height: 100%;
    border: 0;
    border-radius: 0;
    background: transparent;
    box-shadow: none;
    transform: none;
  }

  .hero-backdrop-thumbnail :global(.entity-thumbnail::after) {
    display: none;
  }

  .hero-backdrop-thumbnail :global(.media) {
    width: 100%;
    height: 100%;
    border-bottom: 0;
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
    align-items: center;
    gap: 1.25rem;
    padding: 1.5rem;
    padding-top: 3rem;
    z-index: 3;
  }

  .hero[data-hero-mode="image"] .hero-content {
    /* The lower hero overlaps the banner; add that overlap back so the poster
       keeps the same visible breathing room above and below. */
    padding-top: calc(1.5rem - var(--hero-lower-overlap));
  }

  /* ── Poster / cover ────────────────────────────────────── */

  .poster-frame {
    position: relative;
    flex-shrink: 0;
    width: var(--poster-width, 7rem);
    border-radius: var(--radius-sm, 6px);
    background: #050505;
    box-shadow:
      0 8px 32px rgba(0, 0, 0, 0.6),
      0 0 0 1px rgba(196, 154, 90, 0.2);
    overflow: hidden;
  }

  .poster-frame.is-empty {
    display: grid;
    place-items: center;
    background-image: linear-gradient(135deg, rgba(196, 154, 90, 0.12), rgba(255, 255, 255, 0.04));
  }

  [data-poster-size="small"] .poster-frame { --poster-width: 5rem; }
  [data-poster-size="medium"] .poster-frame { --poster-width: 7rem; }
  [data-poster-size="large"] .poster-frame { --poster-width: 10rem; }

  .poster-frame :global(.entity-thumbnail) {
    width: 100%;
    height: 100%;
    border: 0;
    background: #050505;
    box-shadow: none;
    transform: none;
  }

  .poster-frame :global(.entity-thumbnail::after) {
    display: none;
  }

  .poster-frame :global(.media) {
    height: 100%;
    border-bottom: 0;
  }

  .header-asset-placeholder {
    position: absolute;
    inset: 0;
    z-index: 1;
    display: grid;
    place-items: center;
    background-size: cover;
    color: color-mix(in srgb, var(--detail-accent) 42%, var(--detail-text-muted));
    opacity: 0.84;
  }

  .header-asset-placeholder::before {
    content: "";
    position: absolute;
    inset: 0;
    background:
      radial-gradient(circle at 50% 40%, rgba(242, 194, 106, 0.16), transparent 24%),
      linear-gradient(180deg, rgba(7, 8, 11, 0.18), rgba(7, 8, 11, 0.58));
  }

  .header-asset-placeholder :global(svg) {
    position: relative;
    z-index: 1;
    opacity: 0.48;
    filter: drop-shadow(0 0 20px var(--detail-accent-glow));
  }

  .header-asset-panel {
    position: absolute;
    inset: 0;
    z-index: 5;
    display: flex;
    align-items: flex-start;
    justify-content: flex-end;
    gap: 0.5rem;
    padding: 0.75rem;
    pointer-events: none;
  }

  .header-asset-panel.is-empty {
    align-items: flex-start;
    justify-content: center;
    padding-top: 1rem;
  }

  .asset-empty-label {
    display: inline-flex;
    align-items: center;
    gap: 0.4rem;
    padding: 0.4rem 0.55rem;
    border: 1px solid color-mix(in srgb, var(--detail-accent) 28%, var(--detail-border));
    border-radius: var(--radius-xs, 4px);
    background: rgba(8, 10, 15, 0.72);
    color: var(--detail-text-muted);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 700;
    letter-spacing: 0.04em;
    text-transform: uppercase;
    box-shadow: 0 0 16px rgba(0, 0, 0, 0.35);
    backdrop-filter: blur(10px);
    -webkit-backdrop-filter: blur(10px);
  }

  .poster-frame .asset-empty-label {
    position: absolute;
    inset: auto 0.45rem 3.2rem;
    justify-content: center;
  }

  .image-asset-actions {
    position: relative;
    z-index: 2;
    display: flex;
    gap: 0.35rem;
    pointer-events: auto;
  }

  /* Spinner shown over the artwork while an upload + thumbnail generation is in flight. */
  .asset-busy-overlay {
    position: absolute;
    inset: 0;
    z-index: 4;
    display: grid;
    place-items: center;
    background: rgba(7, 8, 11, 0.55);
    backdrop-filter: blur(2px);
    -webkit-backdrop-filter: blur(2px);
    pointer-events: none;
    border-radius: inherit;
  }

  .asset-busy-overlay :global(.asset-busy-spinner) {
    color: var(--detail-accent, #f2c26a);
    filter: drop-shadow(0 0 8px var(--detail-accent-glow, rgb(242 194 106 / 0.4)));
    animation: asset-busy-spin 0.8s linear infinite;
  }

  @keyframes asset-busy-spin {
    to {
      transform: rotate(360deg);
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .asset-busy-overlay :global(.asset-busy-spinner) {
      animation: none;
    }
  }

  .poster-frame .image-asset-actions {
    position: absolute;
    right: 0.4rem;
    bottom: 0.4rem;
    left: 0.4rem;
    justify-content: center;
  }

  .image-asset-btn {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    gap: 0.3rem;
    min-height: 1.8rem;
    padding: 0.3rem 0.5rem;
    border: 1px solid color-mix(in srgb, var(--detail-accent) 38%, var(--detail-border));
    border-radius: var(--radius-xs, 4px);
    background: rgba(8, 10, 15, 0.78);
    color: var(--detail-text-secondary);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.66rem;
    font-weight: 700;
    letter-spacing: 0.03em;
    text-transform: uppercase;
    cursor: pointer;
    box-shadow: 0 0 14px rgba(0, 0, 0, 0.35);
    backdrop-filter: blur(10px);
    -webkit-backdrop-filter: blur(10px);
  }

  .image-asset-btn:hover:not(:disabled) {
    color: var(--detail-accent);
    border-color: color-mix(in srgb, var(--detail-accent) 62%, var(--detail-border));
    box-shadow: 0 0 16px var(--detail-accent-glow);
  }

  .image-asset-btn:disabled {
    cursor: not-allowed;
    opacity: 0.55;
  }

  [data-asset-dropzone="poster"],
  [data-asset-dropzone="backdrop"] {
    outline: 1px dashed color-mix(in srgb, var(--detail-accent) 42%, transparent);
    outline-offset: -0.35rem;
  }

  .hero-text {
    display: grid;
    gap: 0.4rem;
    min-width: 0;
    flex: 1;
    align-self: flex-end;
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
    .hero-content {
      flex-direction: column;
      align-items: stretch;
      gap: 0.85rem;
      padding: 1.25rem;
      padding-top: 1.75rem;
    }

    .hero[data-hero-mode="image"] .hero-content {
      padding-top: calc(1.25rem - var(--hero-lower-overlap));
    }

    [data-poster-size="small"] .poster-frame { --poster-width: min(10rem, 68vw); }
    [data-poster-size="medium"] .poster-frame { --poster-width: min(11rem, 72vw); }
    [data-poster-size="large"] .poster-frame { --poster-width: min(12rem, 76vw); }

    .poster-frame {
      align-self: center;
    }

    .hero-text {
      align-self: stretch;
      grid-template-columns: minmax(0, 1fr) auto;
      grid-template-areas:
        "title title"
        "meta meta"
        "rating badges"
        "actions actions";
      align-items: center;
      column-gap: 0.75rem;
      row-gap: 0.35rem;
      width: 100%;
    }

    .hero-title {
      grid-area: title;
    }

    .meta-row {
      grid-area: meta;
    }

    .rating-row {
      grid-area: rating;
    }

    .position-badges {
      grid-area: badges;
      justify-self: end;
    }

    .action-row {
      grid-area: actions;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.35rem;
      width: 100%;
    }

    .action-badges {
      justify-self: start;
    }

    .action-group {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: 0.25rem;
      width: auto;
      margin-left: auto;
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

  /* ── Action buttons (edit, identify) use global .entity-action-button ── */

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
    min-width: 0;
    max-width: 100%;
    font-size: 0.82rem;
    color: var(--detail-text-muted);
    overflow-wrap: anywhere;
  }

  .meta-row :global(.meta-item) {
    min-width: 0;
    max-width: 100%;
    white-space: normal;
    font-size: 0.82rem;
    overflow-wrap: anywhere;
    word-break: normal;
  }

  .meta-row :global(.meta-item *) {
    min-width: 0;
    max-width: 100%;
    white-space: inherit;
    overflow-wrap: inherit;
  }

  .meta-row :global(.meta-item.is-studio) {
    color: var(--color-text-accent, #c49a5a);
    text-decoration: none;
    transition: opacity 0.15s;
  }

  .meta-row :global(.meta-item.is-studio:hover) {
    opacity: 0.8;
  }

  .meta-row :global(.meta-sep) {
    display: inline-block;
    flex: 0 0 auto;
    width: 3px;
    height: 3px;
    margin: 0 0.5rem;
    border-radius: 999px;
    background: var(--color-text-muted, #8a93a6);
    opacity: 0.5;
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
    align-items: center;
    gap: 0.45rem;
    flex-wrap: wrap;
  }

  :global(.hero-badge) {
    display: inline-flex;
    align-items: center;
    min-height: 1.45rem;
    padding: 0.2rem 0.62rem;
    border: 1px solid rgba(242, 194, 106, 0.38);
    border-radius: var(--radius-xs);
    background:
      linear-gradient(135deg, rgba(242, 194, 106, 0.11), rgba(255, 255, 255, 0.03)),
      color-mix(in srgb, var(--color-surface-2) 82%, var(--color-accent-900) 18%);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.06),
      0 0 8px rgba(242, 194, 106, 0.08);
    color: var(--color-accent-100);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 700;
    letter-spacing: 0.08em;
    line-height: 1;
    text-transform: uppercase;
    text-shadow: 0 0 6px rgba(242, 194, 106, 0.16);
  }

  /* ── Detail Body ────────────────────────────────────────── */

  .detail-tabs {
    min-width: 0;
    margin-inline: var(--detail-slideout-inset);
  }

  .detail-tab-list {
    position: relative;
    z-index: 2;
    display: flex;
    gap: 0.35rem;
    min-width: 0;
    margin-top: -1px;
    overflow-x: auto;
    padding: 0.65rem 1.5rem;
    border: 1px solid var(--detail-border);
    border-top: 0;
    border-radius: 0 0 var(--radius-md, 10px) var(--radius-md, 10px);
    background: var(--detail-glass);
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

  .detail-content-card {
    min-width: 0;
    border: 1px solid var(--detail-border);
    border-top: 0;
    border-radius: 0 0 var(--radius-md, 10px) var(--radius-md, 10px);
    background:
      linear-gradient(180deg, rgba(19, 23, 31, 0.88), rgba(12, 15, 21, 0.96)),
      var(--detail-surface);
    box-shadow:
      inset 0 1px 0 rgba(255, 255, 255, 0.035),
      0 10px 30px rgba(0, 0, 0, 0.24);
    overflow: hidden;
  }

  .detail-content-card--tabbed {
    position: relative;
    z-index: 1;
    margin-top: -0.5rem;
    padding-top: 0.5rem;
  }

  .detail-content-card--standalone {
    margin: -1px var(--detail-slideout-inset) 0;
  }

  .detail-edit-toolbar {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 0.5rem;
    padding: 0.5rem 1.5rem;
    border-bottom: 1px solid var(--detail-border);
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
    min-width: 0;
    padding: 1rem 1.5rem 1.5rem;
  }

  .detail-tab-sections .detail-body {
    padding: 0;
  }

  .detail-tab-sections .detail-section {
    padding: 0;
    border-bottom: none;
  }

  .custom-detail-section {
    min-width: 0;
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
    display: grid;
    gap: 0.75rem;
    padding: 1rem 1.5rem 1.5rem;
  }

  .metadata-sections .detail-section {
    padding: 0;
    border-bottom: none;
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

  .edit-flag-chips :global(button) {
    min-height: 2.55rem;
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
    backdrop-filter: blur(var(--glass-blur-lg));
    -webkit-backdrop-filter: blur(var(--glass-blur-lg));
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

  /* ── Links ──────────────────────────────────────────────── */

  .link-list {
    display: grid;
    gap: 0.4rem;
  }

  .link-group {
    display: grid;
    gap: 0.4rem;
  }

  .link-group + .link-group {
    margin-top: 0.9rem;
  }

  .link-group-label {
    color: var(--color-text-muted, #7d8596);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
    font-weight: 700;
    letter-spacing: 0;
    text-transform: uppercase;
  }

  .link-item {
    display: flex;
    align-items: center;
    gap: 0.4rem;
    min-width: 0;
    padding: 0.3rem 0.55rem;
    border: 1px solid var(--color-border-subtle, rgba(164, 172, 185, 0.07));
    border-radius: var(--radius-xs, 4px);
    background: linear-gradient(135deg, rgba(35, 42, 58, 0.9), rgba(18, 23, 34, 0.92));
    color: var(--color-text-secondary, #c4c9d4);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.74rem;
    text-decoration: none;
    overflow: hidden;
    transition: border-color 0.15s, color 0.15s, background 0.15s;
  }

  a.link-item:hover {
    color: var(--detail-accent);
    border-color: var(--detail-accent-muted);
    background: linear-gradient(135deg, rgba(48, 43, 33, 0.96), rgba(22, 24, 32, 0.94));
  }

  .url-link-item {
    gap: 0.55rem;
    min-height: 3.25rem;
    padding: 0.55rem 0.65rem;
  }

  .url-link-icon {
    display: grid;
    flex: 0 0 auto;
    place-items: center;
    width: 1.55rem;
    height: 1.55rem;
    border: 1px solid rgba(242, 194, 106, 0.18);
    border-radius: var(--radius-xs, 4px);
    background: rgba(242, 194, 106, 0.07);
    color: var(--detail-accent);
  }

  .url-link-copy {
    display: grid;
    gap: 0.08rem;
    min-width: 0;
  }

  .url-link-title,
  .url-link-subtitle {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .url-link-title {
    color: var(--detail-text, #f2eed8);
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.82rem;
    font-weight: 650;
    letter-spacing: 0.01em;
  }

  .url-link-subtitle {
    color: var(--color-text-muted, #8a93a6);
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.62rem;
  }

  .provider-id-item {
    gap: 0.5rem;
    white-space: nowrap;
  }

  .provider-id-item :global(svg) {
    flex: 0 0 auto;
  }

  .provider-id-provider {
    color: var(--detail-accent);
    font-weight: 800;
    text-transform: uppercase;
  }

  .provider-id-value {
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .link-item.no-url {
    color: var(--color-text-muted, #8a93a6);
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

    .hero[data-hero-mode="image"] .hero-content {
      padding-top: calc(2rem - var(--hero-lower-overlap));
    }

    [data-poster-size="small"] .poster-frame { --poster-width: 6rem; }
    [data-poster-size="medium"] .poster-frame { --poster-width: 9rem; }
    [data-poster-size="large"] .poster-frame { --poster-width: 13rem; }

    .detail-body {
      padding: 1.25rem 2rem 2rem;
    }

    .metadata-sections {
      padding: 1rem 2rem 2rem;
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
