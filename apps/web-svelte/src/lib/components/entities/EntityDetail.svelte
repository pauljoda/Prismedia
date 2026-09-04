<script module lang="ts">
  export type {
    EntityDetailActionButton,
    EntityDetailActionVariant,
    EntityDetailPosterSize,
    EntityDetailProps,
    EntityDetailSection,
    EntityDetailTab,
    EntityMetadataPatch,
    EntityMetadataUpdateRequest,
  } from "./entity-detail-types";
</script>

<script lang="ts">
  import { THUMBNAIL_HOVER_KIND } from "$lib/api/generated/codes";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
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
    Link,
    ListOrdered,
    MonitorCog,
    Pencil,
    Play,
    Users,
  } from "@lucide/svelte";
  import { Button, Tabs } from "@prismedia/ui-svelte";
  import EntityDetailEditLayout from "./EntityDetailEditLayout.svelte";
  import EntityDetailArtworkEditor from "./EntityDetailArtworkEditor.svelte";
  import type { EntityDetailCard, EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { renderEntityDescriptionMarkdown } from "$lib/entities/entity-detail-markdown";
  import {
    DEFAULT_STANDALONE_METADATA_SECTION_IDS,
  } from "$lib/entities/entity-detail";
  import {
    entityReferenceToThumbnailCard,
    toAspectRatioValue,
    type EntityThumbnailCard,
  } from "$lib/entities/entity-thumbnail";
  import EntityThumbnail from "$lib/components/thumbnails/EntityThumbnail.svelte";
  import MetadataCardGrid from "$lib/components/MetadataCardGrid.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import EntityDateEditRequest from "./EntityDateEditRequest.svelte";
  import EntityTagChips from "./EntityTagChips.svelte";
  import EntityDetailHeroControls from "./EntityDetailHeroControls.svelte";
  import MarkdownEditor from "$lib/components/forms/MarkdownEditor.svelte";
  import EntityPicker from "$lib/components/forms/EntityPicker.svelte";
  import FormField from "$lib/components/forms/FormField.svelte";
  import ToggleChip from "$lib/components/forms/ToggleChip.svelte";
  import TextField from "$lib/components/forms/TextField.svelte";
  import { isNsfw as hasNsfwCapability } from "$lib/api/capabilities";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { CREDIT_ROLE, ENTITY_FILE_ROLE, type EntityFileRoleCode } from "$lib/entities/entity-codes";
  import type { EntityDetailEditDraft } from "$lib/entities/entity-detail-edit";
  import { searchTags } from "$lib/entities/entity-detail-search";
  import type {
    EntityDetailProps,
    EntityDetailSection,
    EntityDetailTab,
  } from "./entity-detail-types";
  import { entityAccentForKind } from "$lib/entities/entity-accent";
  import { paletteFromImage, type ArtworkPalette } from "$lib/entities/artwork-palette";
  import { EntityDetailArtworkController } from "./entity-detail-artwork-controller.svelte";
  import EntityDetailDirtyTabDialog from "./EntityDetailDirtyTabDialog.svelte";
  import EntityDetailEditControls from "./EntityDetailEditControls.svelte";
  import EntityDetailMetadataSection from "./EntityDetailMetadataSection.svelte";
  import { EntityDetailEditController } from "./entity-detail-edit-controller.svelte";

  type Props = EntityDetailProps;

  let {
    card,
    wantedStatus,
    onRatingChange,
    onFavoriteToggle,
    onOrganizedToggle,
    peopleLabel = "Cast & Crew",
    defaultCreditRole = CREDIT_ROLE.person,
    posterSize = "medium",
    ratingBusy = false,
    showHero = true,
    showFlagActions = true,
    tabs = [],
    standaloneMetadataSectionIds = DEFAULT_STANDALONE_METADATA_SECTION_IDS,
    onMetadataSave,
    onImageAssetUpload,
    onImageAssetClear,
    onArtworkPaletteChange,
    sections = [],
    heroMeta,
    heroBadges,
    actionButtons = [],
    afterBody,
    extraSections,
    sectionContent,
  }: Props = $props();

  let activeTabId = $state("");
  let paletteState = $state<{ entityId: string; palette: ArtworkPalette } | null>(null);

  const isFavorite = $derived(card.flags.find((f) => f.code === "favorite")?.active ?? false);
  const isNsfw = $derived(
    card.flags.find((f) => f.code === "nsfw")?.active ??
      hasNsfwCapability(card.entity.capabilities),
  );
  const isOrganized = $derived(card.flags.find((f) => f.code === "organized")?.active ?? false);
  const nsfw = useNsfw();
  const entityAccent = $derived(entityAccentForKind(card.entity.kind));
  const artworkPalette = $derived(
    paletteState?.entityId === card.entity.id ? paletteState.palette : null,
  );
  const activePalette = $derived(
    artworkPalette ?? {
      primary: entityAccent.primary,
      secondary: entityAccent.secondary,
      background: "#000000",
    },
  );

  function captureArtworkPalette(image: HTMLImageElement) {
    const palette = paletteFromImage(image);
    if (!palette) return;
    paletteState = { entityId: card.entity.id, palette };
    onArtworkPaletteChange?.(palette);
  }

  $effect(() => {
    if (nsfw.mode === "off" && isNsfw) {
      void goto(resolve("/"), { replaceState: true });
    }
  });

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
  // Route-provided sections take precedence so a route can override a core section's
  // label or flags (e.g. credits labeled "Members" on artists) by re-declaring its id.
  const availableSections = $derived([...sections, ...coreSections]);
  const cardFull = $derived(card as EntityDetailCard & Partial<EntityDetailCardFull>);
  const visibleActionButtons = $derived.by(() => actionButtons.filter((action) => !action.hidden));
  const visibleTabs = $derived.by(() => tabs.filter(tabHasContent));
  const hasTabs = $derived(visibleTabs.length > 0);
  const activeTab = $derived(visibleTabs.find((tab) => tab.id === activeTabId) ?? visibleTabs[0] ?? null);
  const activeTabSections = $derived(activeTab ? sectionsForTab(activeTab) : []);
  const standaloneMetadataSections = $derived.by(() =>
    standaloneMetadataSectionIds
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .filter(sectionHasContent),
  );
  const artwork = new EntityDetailArtworkController({
    card: () => card,
    metadataSave: () => onMetadataSave,
    upload: () => onImageAssetUpload,
    clear: () => onImageAssetClear,
  });
  const edit = new EntityDetailEditController({
    card: () => card,
    flags: () => ({ isFavorite, isNsfw, isOrganized }),
    hasTabs: () => hasTabs,
    activeTab: () => activeTab,
    activeTabSections: () => activeTabSections.filter(sectionEditable),
    standaloneSections: () => standaloneEditSections,
    ratingMax: () => card.rating?.max ?? 5,
    save: () => onMetadataSave,
    activateTab: (tabId) => (activeTabId = tabId),
    onStart: artwork.clearError,
  });
  const editDraft = edit.draft;
  const isEditingActiveTab = $derived(edit.isEditingActiveTab);
  const pendingTabId = $derived(edit.pendingTabId);
  const savingEdit = $derived(edit.saving);
  const effectiveShowHero = $derived(showHero || isEditingActiveTab);
  const displayHero = $derived(artwork.displayHeader);
  const displayPoster = $derived(artwork.displayPoster);
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
  const canManageImages = $derived(artwork.canManage);
  const editableArtwork = $derived([
    { role: ENTITY_FILE_ROLE.poster, label: "Poster", hasAsset: posterHasAsset },
    { role: ENTITY_FILE_ROLE.backdrop, label: "Header", hasAsset: headerHasAsset },
  ].filter(asset => artwork.supports(asset.role)));
  const canEdit = $derived(Boolean(onMetadataSave));
  const editActionLabel = $derived(activeTab ? `Edit ${activeTab.label}` : "Edit details");
  const cancelEditActionLabel = $derived(activeTab ? `Cancel ${activeTab.label}` : "Cancel editing");
  const standaloneSections = $derived.by(() => {
    const ids = ["description", "tags", ...standaloneMetadataSectionIds];
    return [...new Set(ids)]
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .filter((section) => !section.hidden);
  });
  const standaloneEditSections = $derived(standaloneSections.filter(sectionEditable));
  const editValidationErrors = $derived(edit.validationErrors);
  const saveDisabled = $derived(edit.saveDisabled);
  const editErrors = $derived.by(() =>
    [...editValidationErrors, edit.error, artwork.error].filter((error): error is string => Boolean(error)),
  );

  function updateEditDraft<Key extends keyof EntityDetailEditDraft>(
    key: Key,
    value: EntityDetailEditDraft[Key],
  ): void {
    editDraft[key] = value;
  }

  function findSection(sectionId: string): EntityDetailSection | null {
    return availableSections.find((section) => section.id === sectionId) ?? null;
  }

  function sectionEditable(section: EntityDetailSection): boolean {
    if (section.hidden) return false;
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

  function tabHasContent(tab: EntityDetailTab): boolean {
    // A tab stays reachable when any of its sections can be edited, even with no display
    // content yet — otherwise empty metadata (external IDs, dates, …) could never be added.
    return tab.sections
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .some((section) => sectionHasDisplayContent(section) || (canEdit && sectionEditable(section) && !section.hidden));
  }

  function sectionsForTab(tab: EntityDetailTab): EntityDetailSection[] {
    return tab.sections
      .map(findSection)
      .filter((section): section is EntityDetailSection => Boolean(section))
      .filter(sectionHasContent);
  }

  const startEdit = edit.start;
  const cancelEdit = edit.cancel;
  const requestTab = edit.requestTab;
  const stayOnDirtyTab = edit.stayOnDirtyTab;
  const discardDirtyTab = edit.discardDirtyTab;
  const saveEdit = edit.save;

  function posterCardForDisplay(): EntityThumbnailCard | null {
    const withWantedStatus = (posterCard: EntityThumbnailCard): EntityThumbnailCard =>
      wantedStatus === undefined ? posterCard : { ...posterCard, wantedStatus };
    const poster = displayPoster;
    if (poster) {
      return withWantedStatus({
        ...(card.posterCard ?? entityReferenceToThumbnailCard(card.entity)),
        cover: { src: poster.src, alt: poster.alt, role: ENTITY_FILE_ROLE.poster },
        hover: { kind: THUMBNAIL_HOVER_KIND.none },
      });
    }

    if (!isEditingActiveTab) {
      return card.posterCard ? withWantedStatus(card.posterCard) : null;
    }
    return withWantedStatus({
      ...entityReferenceToThumbnailCard(card.entity, { cover: null }),
      hover: { kind: THUMBNAIL_HOVER_KIND.none },
    });
  }

  function roleSupported(role: EntityFileRoleCode): boolean {
    return artwork.supports(role);
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
    await artwork.uploadAsset(role, file);
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

{#snippet renderDetailSection(section: EntityDetailSection)}
  {@const editingSection = isEditingActiveTab && sectionEditable(section)}
  {#if editingSection && section.id === "description"}
    {@render descriptionEditSection()}
  {:else if editingSection && section.id === "tags"}
    {@render tagsEditSection()}
  {:else if section.id === "description"}
    {@render descriptionSection()}
  {:else if section.id === "tags"}
    {@render tagsSection()}
  {:else}
    <EntityDetailMetadataSection
      {card}
      {defaultCreditRole}
      draft={editDraft}
      editing={editingSection}
      onDraftChange={updateEditDraft}
      {peopleLabel}
      {section}
      {sectionContent}
    />
  {/if}
{/snippet}

{#snippet defaultDetailContent()}
  {#if isEditingActiveTab || hasStandaloneBodyContent || afterBody || standaloneMetadataSections.length > 0 || extraSections}
    <div class="detail-content-card detail-content-card--standalone">
      {#if isEditingActiveTab}
        <EntityDetailEditControls
          cancelLabel="Cancel editing"
          errors={editErrors}
          onCancel={cancelEdit}
          onSave={() => void saveEdit()}
          {saveDisabled}
          saveLabel="Save changes"
          saving={savingEdit}
        />
      {/if}

      {#if isEditingActiveTab}
        <div class="detail-tab-sections">
          <EntityDetailEditLayout sections={standaloneSections.filter(sectionHasContent)} item={renderDetailSection} />
        </div>
      {:else if hasStandaloneBodyContent}
        <div class="detail-body">
          {@render descriptionContent()}
          {@render tagsContent()}
        </div>
      {/if}

      <!-- Kind-specific content between body and metadata (studio, credits, etc.).
           Padded at this base level so every page's afterBody is inset consistently and never
           hugs the card edges — pages should not re-add their own horizontal padding. -->
      {#if afterBody}
        <div class="detail-after-body">
          {@render afterBody()}
        </div>
      {/if}

      <!-- Lower metadata sections -->
      {#if (!isEditingActiveTab && standaloneMetadataSections.length > 0) || extraSections}
        <div class="metadata-sections">
          {#if extraSections}
            {@render extraSections()}
          {/if}

          {#if !isEditingActiveTab}
            <MetadataCardGrid>
              {#each standaloneMetadataSections as section (section.id)}
                {@render renderDetailSection(section)}
              {/each}
            </MetadataCardGrid>
          {/if}
        </div>
      {/if}
    </div>
  {/if}
{/snippet}

<EntityDateEditRequest
  {canEdit}
  {hasTabs}
  {visibleTabs}
  {standaloneEditSections}
  onEditTab={(tab) => {
    activeTabId = tab.id;
    startEdit(tab);
  }}
  onEditStandalone={() => startEdit()}
/>

<article
  class="entity-detail"
  data-poster-size={effectivePosterSize}
  data-hero-mode={heroMode}
  style:--detail-accent={activePalette.primary}
  style:--detail-secondary={activePalette.secondary}
  style:--detail-background={activePalette.background}
>
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
      <div class="hero-content" class:has-poster={posterVisible}>
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
          </div>
        {/if}

        <div class="hero-text">
          <h1 class="hero-title">{card.entity.title}</h1>

          {#if heroMeta}
            <div class="meta-row">
              {@render heroMeta()}
            </div>
          {/if}

          <EntityDetailHeroControls
            actionButtons={visibleActionButtons}
            {canEdit}
            {cancelEditActionLabel}
            {card}
            {editActionLabel}
            editing={isEditingActiveTab}
            {heroBadges}
            {isFavorite}
            {isNsfw}
            {isOrganized}
            onCancelEdit={cancelEdit}
            {onFavoriteToggle}
            {onOrganizedToggle}
            {onRatingChange}
            onStartEdit={() => startEdit(activeTab ?? undefined)}
            {ratingBusy}
            {savingEdit}
            {showFlagActions}
          />
        </div>
      </div>
    {/snippet}


    {#if heroMode === "image"}
      <!-- Sharp banner, mask fades bottom 10%; the page's LCP image, loaded at high priority. -->
      <div class="hero-banner">
        <img
          src={displayHero!.src}
          alt="Banner"
          decoding="async"
          fetchpriority="high"
          referrerpolicy="no-referrer"
          onload={(event) => captureArtworkPalette(event.currentTarget as HTMLImageElement)}
        />
      </div>
      <!-- Lower zone: reflection bg + content on top (same URL as the banner, so it reuses the fetch) -->
      <div class="hero-lower">
        <div class="hero-reflection">
          <img src={displayHero!.src} alt="" aria-hidden="true" decoding="async" referrerpolicy="no-referrer" />
        </div>
        <div class="hero-blur-overlay"></div>
        {@render heroContent()}
      </div>
    {:else if heroMode === "poster-blur"}
      <div class="hero-backdrop poster-mode">
        <div class="hero-backdrop-thumbnail">
          {#if posterCard}
            <EntityThumbnail
              card={posterCard}
              linkable={false}
              mediaOnly={true}
              interactive={false}
              onArtworkLoad={captureArtworkPalette}
            />
          {/if}
        </div>
        <div class="hero-backdrop-blur"></div>
      </div>
      {@render heroContent()}
    {:else}
      <div class="hero-gradient-bg"></div>
      {@render heroContent()}
    {/if}
  </div>

  {#if isEditingActiveTab && canManageImages && editableArtwork.length}
    <EntityDetailArtworkEditor assets={editableArtwork} busyRole={artwork.busyRole} onUpload={artwork.uploadAsset} onClear={artwork.clearAsset} />
  {/if}

  {#if hasTabs}
    <div class="detail-tabs">
      <Tabs.Root class="gap-0" activationMode="manual" bind:value={() => activeTab?.id ?? "", requestTab}>
      <Tabs.List variant="line" class="max-w-full justify-start overflow-x-auto rounded-none" aria-label="Detail sections">
        {#each visibleTabs as tab (tab.id)}
          {@const TabIcon = tab.icon}
          <Tabs.Trigger
            value={tab.id}
            id={`entity-detail-tab-${tab.id}`}
            class="max-sm:group-data-[variant=line]/tabs-list:px-2"
          >
            {#if TabIcon}
              <TabIcon class="detail-tab-icon h-3.5 w-3.5" />
            {/if}
            <span>{tab.label}</span>
            {#if tab.count != null && tab.count > 0}
              <strong>{tab.count}</strong>
            {/if}
          </Tabs.Trigger>
        {/each}
      </Tabs.List>

      {#if activeTab}
        <Tabs.Content
          value={activeTab.id}
          class="detail-tab-panel detail-content-card detail-content-card--tabbed"
          id={`entity-detail-panel-${activeTab.id}`}
          aria-labelledby={`entity-detail-tab-${activeTab.id}`}
        >
          {#if isEditingActiveTab}
            <EntityDetailEditControls
              cancelLabel={`Cancel ${activeTab.label}`}
              errors={editErrors}
              onCancel={cancelEdit}
              onSave={() => void saveEdit()}
              {saveDisabled}
              saveLabel={`Save ${activeTab.label}`}
              saving={savingEdit}
            />
          {/if}
          {#key activeTab.id}
            <div class="detail-tab-sections">
              {#if activeTabSections.length === 0 && !isEditingActiveTab}
                {@const EmptyTabIcon = activeTab.icon ?? Pencil}
                <StatePlaceholder
                  icon={EmptyTabIcon}
                  title={`No ${activeTab.label.toLowerCase()} yet`}
                >
                  {#if canEdit}
                    <Button
                      type="button"
                      variant="secondary"
                      size="sm"
                      onclick={() => startEdit(activeTab ?? undefined)}
                    >
                      <Pencil class="h-3.5 w-3.5" />
                      Edit {activeTab.label}
                    </Button>
                  {/if}
                </StatePlaceholder>
              {:else if isEditingActiveTab}
                <EntityDetailEditLayout sections={activeTabSections} item={renderDetailSection} />
              {:else}
                <MetadataCardGrid>
                  {#each activeTabSections as section (section.id)}
                    {@render renderDetailSection(section)}
                  {/each}
                </MetadataCardGrid>
              {/if}
            </div>
          {/key}
        </Tabs.Content>
      {/if}
      </Tabs.Root>
    </div>
  {:else}
    {@render defaultDetailContent()}
  {/if}
</article>

<EntityDetailDirtyTabDialog open={Boolean(pendingTabId)} onStay={stayOnDirtyTab} onDiscard={discardDirtyTab} />

<style>
  /* ── Layout ─────────────────────────────────────────────── */

  .entity-detail {
    --detail-accent-muted: color-mix(in srgb, var(--detail-accent) 24%, transparent);
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

    display: grid;
    gap: 0;
    min-width: 0;
    overflow: hidden;
    border-radius: var(--radius-lg);
    background:
      radial-gradient(circle at 8% 2%, color-mix(in srgb, var(--detail-accent) 10%, transparent), transparent 38rem),
      radial-gradient(circle at 96% 32%, color-mix(in srgb, var(--detail-secondary) 7%, transparent), transparent 34rem),
      linear-gradient(180deg, color-mix(in srgb, var(--detail-background) 88%, #000 12%), #000 45rem);
    transition: background 180ms var(--ease-default);
  }

  .entity-detail > * {
    min-width: 0;
  }

  /* ── Hero ────────────────────────────────────────────────── */


  .hero {
    position: relative;
    z-index: 1;
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
    filter: blur(15px) saturate(0.82) brightness(0.42);
    will-change: transform;
  }

  .hero-blur-overlay {
    position: absolute;
    inset: 0;
    z-index: 1;
    background:
      radial-gradient(circle at 16% 10%, color-mix(in srgb, var(--detail-accent) 18%, transparent), transparent 52%),
      radial-gradient(circle at 88% 40%, color-mix(in srgb, var(--detail-secondary) 12%, transparent), transparent 48%),
      linear-gradient(180deg, rgb(0 0 0 / 0.2), rgb(0 0 0 / 0.72));
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
    transform: scale(1.42);
    filter: blur(36px) saturate(0.78) brightness(0.4);
    opacity: 0.48;
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
    background:
      radial-gradient(circle at top left, color-mix(in srgb, var(--detail-accent) 28%, transparent), transparent 58%),
      radial-gradient(circle at right, color-mix(in srgb, var(--detail-secondary) 18%, transparent), transparent 54%),
      linear-gradient(180deg, rgb(0 0 0 / 0.3), rgb(0 0 0 / 0.82));
  }


  /* Gradient background when no images exist */
  .hero-gradient-bg {
    position: absolute;
    inset: 0;
    z-index: 0;
    background:
      radial-gradient(circle at 14% 18%, color-mix(in srgb, var(--detail-accent) 26%, transparent), transparent 56%),
      radial-gradient(circle at 90% 42%, color-mix(in srgb, var(--detail-secondary) 16%, transparent), transparent 54%),
      #000;
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
      0 0 0 1px rgba(199, 201, 204, 0.2);
    overflow: hidden;
  }

  .poster-frame.is-empty {
    display: grid;
    place-items: center;
    background-image: linear-gradient(135deg, rgba(199, 201, 204, 0.12), rgba(255, 255, 255, 0.04));
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

  @media (max-width: 767px) {
    .hero-content {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 2fr);
      align-items: start;
      column-gap: var(--spacing-control-pad-lg);
      row-gap: var(--spacing-control-gap);
    }

    .hero[data-hero-mode="image"] .hero-content {
      padding-top: calc(1.25rem - var(--hero-lower-overlap));
    }

    .poster-frame {
      grid-column: 1;
      grid-row: 1 / span 4;
      align-self: center;
      width: 100%;
    }

    .hero-text {
      display: contents;
    }

    .hero-content:not(.has-poster) {
      grid-template-columns: minmax(0, 1fr);
      --detail-text-column: 1;
    }

    .hero-title {
      grid-column: var(--detail-text-column, 2);
      grid-row: 1;
    }

    .meta-row {
      grid-column: var(--detail-text-column, 2);
      grid-row: 2;
    }

  }

  @media (max-width: 400px) {
    .poster-frame { grid-row: 1 / span 2; }
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

  .meta-row :global(.meta-item .meta-item-label) {
    margin-right: 0.3rem;
    font-size: 0.72rem;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--detail-text-muted);
  }

  .meta-row :global(.meta-item.is-studio) {
    color: var(--color-text-accent, #c7c9cc);
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

  /* ── Detail Body ────────────────────────────────────────── */

  .detail-tabs {
    --tabs-indicator: var(--detail-accent);
    position: relative;
    z-index: 0;
    min-width: 0;
  }

  /* Attached sections continue behind the hero's lower corners. */
  .hero + .detail-tabs,
  .hero + .detail-content-card--standalone {
    margin-top: calc(-1 * var(--radius-md));
    padding-top: var(--radius-md);
    background: var(--color-surface-1);
  }

  .detail-tabs :global(.detail-tab-panel) {
    min-width: 0;
  }

  .entity-detail :global(.detail-content-card) {
    min-width: 0;
    border: 1px solid var(--detail-border);
    border-top: 0;
    border-radius: 0 0 var(--radius-md, 10px) var(--radius-md, 10px);
    background: var(--detail-surface);
    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.22);
    overflow: hidden;
  }

  .entity-detail :global(.detail-content-card--tabbed) {
    position: relative;
    z-index: 1;
    margin-top: 0;
  }

  .detail-content-card--standalone {
    margin-top: -1px;
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

  .detail-body {
    display: grid;
    gap: 0;
    padding: 1rem 1.5rem 1.5rem;
  }

  /* Page-supplied content (afterBody) inset to match the standard body padding. */
  .detail-after-body {
    padding: 1rem 1.5rem 1.5rem;
  }

  /* When the description/tags body renders above, its bottom padding already
     provides the separation — don't double it up. */
  .detail-body + .detail-after-body {
    padding-top: 0;
  }

  /* ── Description (markdown) ─────────────────────────────── */

  .description-content {
    max-width: 80ch;
    color: var(--detail-text-secondary);
    font-size: 0.9375rem;
    line-height: 1.7;
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
    grid-template-columns: minmax(0, 1fr);
    min-width: 0;
    gap: calc(var(--spacing) * 4);
  }

  .edit-inline-grid {
    display: grid;
    grid-template-columns: minmax(0, 1fr);
    min-width: 0;
    gap: calc(var(--spacing) * 4);
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
    gap: var(--spacing-control-gap);
    min-height: var(--spacing-control);
    align-items: center;
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

    .detail-after-body {
      padding: 1.25rem 2rem 2rem;
    }

    .metadata-sections {
      padding: 1rem 2rem 2rem;
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
