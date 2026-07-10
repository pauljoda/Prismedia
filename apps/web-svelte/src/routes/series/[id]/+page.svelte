<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { Users, Building2, Calendar, CloudDownload, Info, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchSeason, fetchSeries, type VideoSeasonDetail, type VideoSeriesDetail } from "$lib/api/media";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import type { EntityDetailCredit, EntityDetailTag } from "$lib/entities/entity-detail";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { getChildIds } from "$lib/entities/entity-children";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { CREDIT_ROLE, ENTITY_KIND } from "$lib/entities/entity-codes";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityMetadataUpdateRequest,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let series = $state<VideoSeriesDetail | null>(null);
  let seasonEpisodeCounts = $state<Record<string, number>>({});
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let seasonCards = $state<EntityThumbnailCard[]>([]);
  let childSeriesCards = $state<EntityThumbnailCard[]>([]);
  let videoCards = $state<EntityThumbnailCard[]>([]);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!series) return null;
    return {
      ...entityCardToDetailCard(series),
      tags: relationshipTags,
      credits: relationshipCredits,
      studio: relationshipStudio,
    };
  });

  const identifyAction = useIdentifyDetailAction(() => series);
  const heroActions = $derived.by((): EntityDetailActionButton[] =>
    identifyAction.action ? [identifyAction.action] : []);

  // Following the series and managing its child Entities live in the shared Acquisition detail tab;
  // no season-specific pass or route-local monitoring state is needed.
  const acq = useEntityAcquisition({
    entityId: () => series?.id,
    capabilities: () => series?.capabilities,
    childCards: () => requestableDirectChildCards(
      series?.id,
      [...seasonCards, ...childSeriesCards, ...videoCards],
    ),
    onChanged: refreshSeries,
    onPruned: () => goto("/series"),
  });
  const fileManagement = {
    onDeleted: () => goto("/series"),
    onReverted: () => refreshAfterManagedFileRevert(acq, refreshSeries),
  };

  const dates = $derived(card?.dates ?? []);
  const airedDate = $derived(
    dates.find((item) => item.code.toLowerCase().replaceAll("-", "") === "firstair") ?? dates[0] ?? null,
  );

  const hasSeasons = $derived(seasonCards.length > 0);
  const hasChildSeries = $derived(childSeriesCards.length > 0);
  const hasVideos = $derived(videoCards.length > 0);
  const seasonCount = $derived(seasonCards.length);
  const totalEpisodeCount = $derived(
    videoCards.length + Object.values(seasonEpisodeCounts).reduce((total, count) => total + count, 0),
  );
  // Built-in sections come from EntityDetail's core catalog; only label overrides
  // are declared here.
  const detailSections = $derived.by((): EntityDetailSection[] => [
    {
      id: "credits",
      label: "Cast",
      icon: Users,
    },
    { id: "acquisition" },
  ]);
  const detailTabs = $derived.by((): EntityDetailTab[] => {
    if (!card) return [];
    return [
      {
        id: "details",
        label: "Details",
        icon: Info,
        sections: ["description", "tags", "studio", "credits"],
      },
      {
        id: "metadata",
        label: "Metadata",
        icon: SlidersHorizontal,
        sections: ["stats", "dates", "classification", "source", "links"],
        layout: "grid",
      },
      ...(acq.visible
        ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
        : []),
    ];
  });

  onMount(() => {
    void loadSeries();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadSeries();
  });

  $effect(() => {
    if (!series) return;
    return appChrome.setBreadcrumbs([
      { label: "Series", href: resolve("/series") },
      { label: series.title },
    ]);
  });

  async function loadSeries(options: { showLoading?: boolean } = {}) {
    const showLoading = options.showLoading ?? true;
    if (showLoading || !series) loadState = "loading";
    errorMessage = null;
    try {
      const nextSeries = await fetchSeries(page.params.id ?? "");
      await hydrateSeriesThumbnails(nextSeries);
      seasonEpisodeCounts = await loadSeasonEpisodeCounts(nextSeries);
      series = nextSeries;
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function refreshSeries() {
    await loadSeries({ showLoading: false });
  }

  async function handleRatingChange(value: number | null) {
    if (!series || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(series, value, (next) => (series = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!series) return;
    await toggleOptimisticEntityFlag(series, "isFavorite", (next) => (series = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!series) return;
    await toggleOptimisticEntityFlag(series, "isOrganized", (next) => (series = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!series) return;
    await updateEntityMetadata(series.id, request, { kind: series.kind });
    await loadSeries();
  }

  async function loadSeasonEpisodeCounts(nextSeries: VideoSeriesDetail): Promise<Record<string, number>> {
    const seasonIds = getChildIds(nextSeries, ENTITY_KIND.videoSeason);
    if (seasonIds.length === 0) return {};

    const details = await Promise.all(
      seasonIds.map((id) => fetchSeason(nextSeries.id, id)),
    );

    return Object.fromEntries(details.map((detail: VideoSeasonDetail) => [
      detail.id,
      getChildIds(detail, ENTITY_KIND.video).length,
    ]));
  }

  async function hydrateSeriesThumbnails(nextSeries: VideoSeriesDetail) {
    const seasonIds = getChildIds(nextSeries, ENTITY_KIND.videoSeason);
    const childSeriesIds = getChildIds(nextSeries, ENTITY_KIND.videoSeries);
    const videoIds = getChildIds(nextSeries, ENTITY_KIND.video);

    const [
      seasons,
      childSeries,
      videos,
      relationshipCards,
    ] = await Promise.all([
      fetchOrderedEntityThumbnails(seasonIds),
      fetchOrderedEntityThumbnails(childSeriesIds),
      fetchOrderedEntityThumbnails(videoIds),
      hydrateStandardRelationshipCards(nextSeries),
    ]);

    seasonCards = thumbnailsToCards(seasons, {
      hrefFor: (thumbnail) => `/series/${nextSeries.id}/seasons/${thumbnail.id}`,
    });
    childSeriesCards = thumbnailsToCards(childSeries);
    videoCards = thumbnailsToCards(videos);
    relationshipCredits = relationshipCards.credits;
    relationshipStudio = relationshipCards.studio;
    relationshipTags = relationshipCards.relationshipTags;
  }

</script>

<svelte:head>
  <title>{series?.title ?? "Series"} · Prismedia</title>
</svelte:head>

<div class="series-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load series."}</p>
      <button type="button" onclick={() => void loadSeries()}>Retry</button>
    </div>
  {:else if card && series}
    <EntityDetail
      {card}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      posterSize="large"
      tabs={detailTabs}
      sections={detailSections}
      actionButtons={heroActions}
      defaultCreditRole={CREDIT_ROLE.actor}
    >
      {#snippet heroMeta()}
        {#if airedDate}
          <span class="meta-item">
            <span class="meta-item-label">{airedDate.label}</span>
            {airedDate.display}
          </span>
        {/if}
        {#if airedDate && (seasonCount > 0 || totalEpisodeCount > 0)}
          <span class="meta-sep"></span>
        {/if}
        {#if seasonCount > 0}
          <span class="meta-item">Seasons: {seasonCount}</span>
        {/if}
        {#if seasonCount > 0 && totalEpisodeCount > 0}
          <span class="meta-sep"></span>
        {/if}
        {#if totalEpisodeCount > 0}
          <span class="meta-item">Episodes: {totalEpisodeCount}</span>
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "acquisition"}
          <EntityAcquisitionCard
            {acq}
            entity={series}
            {fileManagement}
            onImported={refreshSeries}
          />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if hasSeasons}
      <EntityGridSection
        title="Seasons"
        count={seasonCards.length}
        icon={Calendar}
        prefsKey={`series-${series?.id}-seasons-section`}
      >
        <EntityGrid
          cards={seasonCards}
          prefsKey={`series-${series?.id}-seasons`}
          initialSortBy="position"
          emptyTitle="No seasons"
          emptyMessage="This series has no seasons."
        />
      </EntityGridSection>
    {/if}

    {#if hasChildSeries}
      <EntityGridSection
        title="Sub Series"
        count={childSeriesCards.length}
        icon={Building2}
        prefsKey={`series-${series?.id}-children-section`}
      >
        <EntityGrid
          cards={childSeriesCards}
          prefsKey={`series-${series?.id}-children`}
          emptyTitle="No sub-series"
          emptyMessage="This series has no sub-series."
        />
      </EntityGridSection>
    {/if}

    {#if hasVideos}
      <EntityGridSection
        title={hasSeasons ? "Specials" : "Episodes"}
        count={videoCards.length}
        prefsKey={`series-${series?.id}-videos-section`}
      >
        <EntityGrid
          cards={videoCards}
          prefsKey={`series-${series?.id}-videos`}
          initialSortBy="position"
          emptyTitle={hasSeasons ? "No specials" : "No episodes"}
          emptyMessage="No loose videos in this series."
        />
      </EntityGridSection>
    {/if}

    {#if !hasSeasons && !hasChildSeries && !hasVideos}
      <div class="empty-children">
        <p>No seasons, episodes, or sub-series linked to this series yet.</p>
      </div>
    {/if}
  {/if}
</div>

<style>
  .series-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
    max-width: none;
    margin: 0;
  }


  .error-notice {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 1rem;
    padding: 1rem;
    border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235));
    background: var(--color-surface-2, #101420);
    color: var(--color-text-muted, #8a93a6);
    font-size: 0.85rem;
  }

  .error-notice button {
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
    color: var(--color-text-muted, #8a93a6);
    padding: 0.4rem 0.8rem;
    font-size: 0.78rem;
    cursor: pointer;
  }

  /* ── Hero meta items (used inside EntityDetail snippets) ── */

  :global(.meta-item) {
    white-space: nowrap;
    font-size: 0.82rem;
  }

  :global(.meta-sep) {
    display: inline-block;
    width: 3px;
    height: 3px;
    margin: 0 0.5rem;
    background: var(--color-text-muted, #8a93a6);
    opacity: 0.5;
  }

  .empty-children {
    padding: 2rem;
    border: 1px solid var(--color-border-subtle, #1c2235);
    background: var(--color-surface-1, #0c0f15);
    color: var(--color-text-muted, #8a93a6);
    text-align: center;
    font-size: 0.85rem;
  }


</style>
