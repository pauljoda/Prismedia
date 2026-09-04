<script lang="ts">
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { goto } from "$app/navigation";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { Users, Building2, Calendar, CloudDownload, Info, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import MediaProgressPanel from "$lib/components/MediaProgressPanel.svelte";
  import { PROGRESS_UNIT } from "$lib/api/generated/codes";
  import { fetchEntity, fetchEntityChildReferences, type EntityCardFull } from "$lib/api/entities";
  import { updateEntityProgress } from "$lib/api/consumption";
  import { getCapability } from "$lib/api/capabilities";
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
  import { CAPABILITY_KIND, CREDIT_ROLE, ENTITY_KIND } from "$lib/entities/entity-codes";
  import {
    videoContainerProgressDisplay,
    videoProgressEpisodeFromCard,
  } from "$lib/entities/video-container-progress";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  let seasonEpisodeCounts = $state<Record<string, number>>({});
  let seasonCards = $state<EntityThumbnailCard[]>([]);
  let childSeriesCards = $state<EntityThumbnailCard[]>([]);
  let episodeCards = $state<EntityThumbnailCard[]>([]);
  let orderedSeriesEpisodeIds = $state<string[]>([]);
  let seriesProgressEpisodeCard = $state<EntityThumbnailCard | null>(null);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let progressBusy = $state(false);

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => page.params.id ?? "",
    load: ({ signal }) => loadSeries(signal),
    breadcrumbs: (entity) => [
      { label: "Series", href: resolve("/series") },
      { label: entity.title },
    ],
  });
  const series = $derived(detail.entity);

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
      [...seasonCards, ...childSeriesCards, ...episodeCards],
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
  const hasEpisodes = $derived(episodeCards.length > 0);
  const seasonCount = $derived(seasonCards.length);
  const totalEpisodeCount = $derived(
    episodeCards.length + Object.values(seasonEpisodeCounts).reduce((total, count) => total + count, 0),
  );
  const seriesProgress = $derived(
    series ? getCapability(series.capabilities, CAPABILITY_KIND.progress) : undefined,
  );
  const progressDisplay = $derived(videoContainerProgressDisplay(
    seriesProgress,
    videoProgressEpisodeFromCard(seriesProgressEpisodeCard),
  ));
  const firstEpisodeId = $derived(orderedSeriesEpisodeIds[0] ?? null);
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

  async function loadSeries(signal: AbortSignal): Promise<EntityCardFull> {
    const nextSeries = await fetchEntity(page.params.id ?? "", { signal });
    const hydration = await hydrateSeriesThumbnails(nextSeries, signal);
    const episodeState = await loadSeriesEpisodes(nextSeries, hydration.episodeCards, signal);
    signal.throwIfAborted();

    seasonCards = hydration.seasonCards;
    childSeriesCards = hydration.childSeriesCards;
    episodeCards = hydration.episodeCards;
    relationshipCredits = hydration.relationshipCredits;
    relationshipStudio = hydration.relationshipStudio;
    relationshipTags = hydration.relationshipTags;
    seasonEpisodeCounts = episodeState.counts;
    orderedSeriesEpisodeIds = episodeState.ids;
    seriesProgressEpisodeCard = episodeState.progressCard;
    return nextSeries;
  }

  function refreshSeries(): Promise<void> {
    return detail.reload({ showLoading: false });
  }

  async function loadSeriesEpisodes(
    nextSeries: EntityCardFull,
    directEpisodeCards: EntityThumbnailCard[],
    signal: AbortSignal,
  ): Promise<{
    counts: Record<string, number>;
    ids: string[];
    progressCard: EntityThumbnailCard | null;
  }> {
    const seasonIds = getChildIds(nextSeries, ENTITY_KIND.videoSeason);
    if (seasonIds.length === 0) {
      const progress = getCapability(nextSeries.capabilities, CAPABILITY_KIND.progress);
      return {
        counts: {},
        ids: directEpisodeCards.map((card) => card.entity.id),
        progressCard: directEpisodeCards.find((card) => card.entity.id === progress?.currentEntityId) ?? null,
      };
    }

    const seasonChildren = await fetchEntityChildReferences(seasonIds, { signal });
    const episodeIds = seasonChildren.flatMap((group) =>
      group.items
        .filter((child) => child.kind === ENTITY_KIND.videoEpisode)
        .map((child) => child.id),
    );
    const progress = getCapability(nextSeries.capabilities, CAPABILITY_KIND.progress);
    const progressCards = progress?.currentEntityId && episodeIds.includes(progress.currentEntityId)
      ? thumbnailsToCards(await fetchOrderedEntityThumbnails([progress.currentEntityId], { signal }))
      : [];

    return {
      counts: Object.fromEntries(seasonChildren.map((group) => [
        group.parentId,
        group.items.filter((child) => child.kind === ENTITY_KIND.videoEpisode).length,
      ])),
      ids: episodeIds,
      progressCard: progressCards[0] ?? null,
    };
  }

  function continueSeries() {
    if (!progressDisplay) return;
    void goto(`/videos/${progressDisplay.episodeId}`);
  }

  async function handleToggleSeriesWatched(watched: boolean) {
    if (!series || !progressDisplay || progressBusy) return;
    progressBusy = true;
    try {
      await updateEntityProgress(series.id, {
        currentEntityId: progressDisplay.episodeId,
        unit: PROGRESS_UNIT.item,
        index: progressDisplay.index,
        total: progressDisplay.total,
        completed: watched,
      });
      await detail.reload({ showLoading: false });
    } finally {
      progressBusy = false;
    }
  }

  async function startSeriesOver() {
    if (!series || !firstEpisodeId || !progressDisplay || progressBusy) return;
    progressBusy = true;
    try {
      await updateEntityProgress(series.id, {
        currentEntityId: firstEpisodeId,
        unit: PROGRESS_UNIT.item,
        index: 0,
        total: progressDisplay.total,
        reset: true,
      });
      await detail.reload({ showLoading: false });
    } finally {
      progressBusy = false;
    }
  }

  async function hydrateSeriesThumbnails(nextSeries: EntityCardFull, signal: AbortSignal) {
    const seasonIds = getChildIds(nextSeries, ENTITY_KIND.videoSeason);
    const childSeriesIds = getChildIds(nextSeries, ENTITY_KIND.videoSeries);
    const episodeIds = getChildIds(nextSeries, ENTITY_KIND.videoEpisode);

    const [
      seasons,
      childSeries,
      episodes,
      relationshipCards,
    ] = await Promise.all([
      fetchOrderedEntityThumbnails(seasonIds, { signal }),
      fetchOrderedEntityThumbnails(childSeriesIds, { signal }),
      fetchOrderedEntityThumbnails(episodeIds, { signal }),
      hydrateStandardRelationshipCards(nextSeries, { signal }),
    ]);

    return {
      seasonCards: thumbnailsToCards(seasons, {
        hrefFor: (thumbnail) => `/series/${nextSeries.id}/seasons/${thumbnail.id}`,
      }),
      childSeriesCards: thumbnailsToCards(childSeries),
      episodeCards: thumbnailsToCards(episodes, { groupSharedSourceEpisodes: true }),
      relationshipCredits: relationshipCards.credits,
      relationshipStudio: relationshipCards.studio,
      relationshipTags: relationshipCards.relationshipTags,
    };
  }

</script>

<svelte:head>
  <title>{series?.title ?? "Series"} · Prismedia</title>
</svelte:head>

<div class="series-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load series."
    onRetry={detail.retry}
  >
    {#if card && series}
      <EntityDetail
      {card}
      onRatingChange={detail.changeRating}
      onFavoriteToggle={detail.toggleFavorite}
      onOrganizedToggle={detail.toggleOrganized}
      onMetadataSave={detail.saveMetadata}
      ratingBusy={detail.ratingBusy}
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

    {#if progressDisplay}
      <section class="progress-section">
        <MediaProgressPanel
          kind="watch"
          completed={progressDisplay.completed}
          percent={progressDisplay.percent}
          positionLabel={progressDisplay.positionLabel}
          countLabel={progressDisplay.episodeLabel}
          canResume={progressDisplay.canContinue}
          canStartOver
          busy={progressBusy}
          resumeLabel="Continue"
          onToggleCompleted={handleToggleSeriesWatched}
          onResume={continueSeries}
          onStartOver={startSeriesOver}
        />
      </section>
    {/if}

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

    {#if hasEpisodes}
      <EntityGridSection
        title={hasSeasons ? "Specials" : "Episodes"}
        count={episodeCards.length}
        prefsKey={`series-${series?.id}-videos-section`}
      >
        <EntityGrid
          cards={episodeCards}
          prefsKey={`series-${series?.id}-videos`}
          initialSortBy="position"
          emptyTitle={hasSeasons ? "No specials" : "No episodes"}
          emptyMessage="No loose episodes in this series."
        />
      </EntityGridSection>
    {/if}

    {#if !hasSeasons && !hasChildSeries && !hasEpisodes}
      <StatePlaceholder icon={Calendar} title="No episodes yet" description="Seasons, episodes, and linked series will appear here." />
    {/if}
    {/if}
  </EntityDetailPageState>
</div>

<style>
  .series-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
    max-width: none;
    margin: 0;
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



</style>
