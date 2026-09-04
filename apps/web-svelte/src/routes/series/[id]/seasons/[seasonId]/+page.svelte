<script lang="ts">
  import { Badge as UiBadge } from "@prismedia/ui-svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { CloudDownload, Film, Info, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import MediaProgressPanel from "$lib/components/MediaProgressPanel.svelte";
  import { PROGRESS_UNIT } from "$lib/api/generated/codes";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { updateEntityProgress } from "$lib/api/consumption";
  import { getCapability, isWanted } from "$lib/api/capabilities";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { getChildIds } from "$lib/entities/entity-children";
  import type { EntityDetailCredit, EntityDetailTag } from "$lib/entities/entity-detail";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { CAPABILITY_KIND, ENTITY_KIND } from "$lib/entities/entity-codes";
  import {
    videoContainerProgressDisplay,
    videoProgressEpisodeFromCard,
  } from "$lib/entities/video-container-progress";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";

  let parentSeries = $state<EntityCardFull | null>(null);
  let episodeCards = $state<EntityThumbnailCard[]>([]);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let progressBusy = $state(false);

  const seriesId = $derived(page.params.id ?? "");
  const seasonId = $derived(page.params.seasonId ?? "");

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => `${seriesId}:${seasonId}`,
    load: async ({ signal }) => {
      const [seriesDetail, seasonDetail] = await Promise.all([
        fetchEntity(seriesId, { signal }),
        fetchEntity(seasonId, { signal }),
      ]);
      const [episodes, relationships] = await Promise.all([
        loadEpisodeCards(seasonDetail, signal),
        loadSeasonRelationships(seasonDetail, seriesDetail, signal),
      ]);
      signal.throwIfAborted();

      parentSeries = seriesDetail;
      episodeCards = episodes;
      relationshipCredits = relationships.credits;
      relationshipStudio = relationships.studio;
      relationshipTags = relationships.relationshipTags;
      return seasonDetail;
    },
    breadcrumbs: (nextSeason) => [
      { label: "Series", href: "/series" },
      { label: parentSeries?.title ?? "Series", href: `/series/${seriesId}` },
      { label: nextSeason.title },
    ],
  });

  const season = $derived(detail.entity);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!season) return null;
    return {
      ...entityCardToDetailCard(season),
      tags: relationshipTags,
      credits: relationshipCredits,
      studio: relationshipStudio,
    };
  });

  const seasonNumber = $derived.by(() => {
    if (!season) return null;
    const pos = getCapability(season.capabilities, CAPABILITY_KIND.position);
    const item = pos?.items.find((p) => p.code === "season");
    return item ? Number(item.value) : null;
  });
  const seasonProgress = $derived(
    season ? getCapability(season.capabilities, CAPABILITY_KIND.progress) : undefined,
  );
  const progressEpisodeCard = $derived(
    episodeCards.find((item) => item.entity.id === seasonProgress?.currentEntityId) ?? null,
  );
  const progressDisplay = $derived(videoContainerProgressDisplay(
    seasonProgress,
    videoProgressEpisodeFromCard(progressEpisodeCard),
  ));
  const firstEpisodeId = $derived(episodeCards[0]?.entity.id ?? null);

  const dates = $derived(card?.dates ?? []);
  const identifyAction = useIdentifyDetailAction(() => season);
  const heroActions = $derived.by((): EntityDetailActionButton[] =>
    identifyAction.action ? [identifyAction.action] : []);

  // A phantom season's "Search for release" (a season-pack acquisition) and its acquisition
  // management live in the Acquisition detail tab, exactly like a wanted movie. Episodes ride
  // along so a half-imported season surfaces its missing episodes as a roll-up with a
  // "Search N missing" action, the same way the series page rolls up its seasons.
  const acq = useEntityAcquisition({
    entityId: () => season?.id,
    capabilities: () => season?.capabilities,
    childCards: () => requestableDirectChildCards(season?.id, episodeCards),
    onChanged: () => detail.reload({ showLoading: false }),
    onStatusChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto(`/series/${seriesId}`),
  });
  const wantedStateLabel = $derived(acquisitionStatusDisplay(acq.acquisition?.summary.status).label);
  const fileManagement = {
    onDeleted: () => goto(`/series/${seriesId}`),
    onReverted: () => refreshAfterManagedFileRevert(acq, () => detail.reload({ showLoading: false })),
  };

  // Seasons are not relationship owners: tags, studio, and cast belong to the series and
  // are shown here as inherited context only. Editing them on a season would write through
  // to the series via the backend's owner resolution, so the sections are read-only.
  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "tags", label: "Tags", editable: false },
    { id: "studio", label: "Studio", editable: false },
    { id: "credits", label: "Cast", icon: Users, editable: false },
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
        sections: ["stats", "dates", "links"],
        layout: "grid",
      },
      ...(acq.visible
        ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
        : []),
    ];
  });

  const seasonWanted = $derived(!!season && isWanted(season.capabilities));

  async function loadEpisodeCards(
    seasonDetail: EntityCardFull,
    signal: AbortSignal,
  ): Promise<EntityThumbnailCard[]> {
    const episodeIds = getChildIds(seasonDetail, ENTITY_KIND.videoEpisode);
    return thumbnailsToCards(
      await fetchOrderedEntityThumbnails(episodeIds, { signal }),
      { groupSharedSourceEpisodes: true },
    );
  }

  function continueSeason() {
    if (!progressDisplay) return;
    void goto(`/videos/${progressDisplay.episodeId}`);
  }

  async function handleToggleSeasonWatched(watched: boolean) {
    if (!season || !progressDisplay || progressBusy) return;
    progressBusy = true;
    try {
      await updateEntityProgress(season.id, {
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

  async function startSeasonOver() {
    if (!season || !firstEpisodeId || !progressDisplay || progressBusy) return;
    progressBusy = true;
    try {
      await updateEntityProgress(season.id, {
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

  async function loadSeasonRelationships(
    seasonDetail: EntityCardFull,
    seriesDetail: EntityCardFull,
    signal: AbortSignal,
  ): Promise<Awaited<ReturnType<typeof hydrateStandardRelationshipCards>>> {
    let relationshipCards = await hydrateStandardRelationshipCards(seasonDetail, { signal });
    if (
      !relationshipCards.studio &&
      relationshipCards.credits.length === 0 &&
      relationshipCards.relationshipTags.length === 0
    ) {
      relationshipCards = await hydrateStandardRelationshipCards(seriesDetail, { signal });
    }

    return relationshipCards;
  }
</script>

<svelte:head>
  <title>{season?.title ?? "Season"} · Prismedia</title>
</svelte:head>

<div class="season-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load season."
    onRetry={detail.retry}
  >
  {#if card && season}
    <EntityDetail
      {card}
      wantedStatus={acq.acquisition?.summary.status ?? null}
      onRatingChange={detail.changeRating}
      onFavoriteToggle={detail.toggleFavorite}
      onOrganizedToggle={detail.toggleOrganized}
      onMetadataSave={detail.saveMetadata}
      ratingBusy={detail.ratingBusy}
      posterSize="large"
      tabs={detailTabs}
      sections={detailSections}
      actionButtons={heroActions}
    >
      {#snippet heroMeta()}
        {#if parentSeries}
          <span class="meta-item is-studio">{parentSeries.title}</span>
        {/if}
        {#if seasonNumber != null}
          <span class="meta-sep"></span>
          <span class="meta-item">Season {seasonNumber}</span>
        {/if}
        <EntityDetailHeroDates {dates} leadingSeparator={Boolean(parentSeries || seasonNumber != null)} />
      {/snippet}

      {#snippet heroBadges()}
        {#if seasonWanted}
          <UiBadge variant="outline">{wantedStateLabel}</UiBadge>
        {/if}
        {#if seasonNumber != null}
          <UiBadge variant="outline">S{String(seasonNumber).padStart(2, "0")}</UiBadge>
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "acquisition"}
          <EntityAcquisitionCard
            {acq}
            entity={season}
            {fileManagement}
            onCancelled={() => void detail.reload({ showLoading: false })}
            onImported={() => detail.reload({ showLoading: false })}
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
          onToggleCompleted={handleToggleSeasonWatched}
          onResume={continueSeason}
          onStartOver={startSeasonOver}
        />
      </section>
    {/if}

    {#if episodeCards.length > 0}
      <EntityGridSection title="Episodes" icon={Film} count={episodeCards.length} prefsKey={`season-${seasonId}-episodes-section`}>
        <EntityGrid
          cards={episodeCards}
          prefsKey={`season-${seasonId}-episodes`}
          initialSortBy="position"
          dockControls={false}
          showPagination={false}
          emptyTitle="No episodes"
          emptyMessage="No episodes found in this season."
        />
      </EntityGridSection>
    {:else}
      <StatePlaceholder icon={Film} title="No episodes yet" description="Episodes linked to this season will appear here." />
    {/if}
  {/if}
  </EntityDetailPageState>
</div>

<style>
  .season-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
    max-width: none;
    margin: 0;
  }

  :global(.meta-item) {
    white-space: nowrap;
    font-size: 0.82rem;
  }

  :global(.meta-item.is-studio) {
    color: var(--color-text-accent, #c7c9cc);
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
