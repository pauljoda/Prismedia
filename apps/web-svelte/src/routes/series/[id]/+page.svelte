<script lang="ts">
  import { onMount } from "svelte";
  import { resolve } from "$app/paths";
  import { page } from "$app/state";
  import { Users, Building2, Calendar, Info, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchSeason, fetchSeries, type VideoSeasonDetail, type VideoSeriesDetail } from "$lib/api/media";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import type { EntityDetailTag } from "$lib/entities/entity-detail";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { getChildIds } from "$lib/entities/entity-children";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityMetadataUpdateRequest,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
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
  let studioCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!series) return null;
    return {
      ...entityCardToDetailCard(series),
      tags: relationshipTags,
    };
  });

  const identifyAction = useIdentifyDetailAction(() => card?.entity.id, () => card?.entity.kind);
  const heroActions = $derived.by((): EntityDetailActionButton[] => identifyAction.action ? [identifyAction.action] : []);

  const dates = $derived.by(() => {
    if (!series) return [];
    const cap = getCapability(series.capabilities, "dates");
    return cap?.items ?? [];
  });

  const dateAired = $derived.by(() => {
    const date = dates.find((item) => item.code === "first-air") ?? dates[0];
    return date ? formatDateForHero(date.value) : null;
  });

  const hasSeasons = $derived(seasonCards.length > 0);
  const hasChildSeries = $derived(childSeriesCards.length > 0);
  const hasVideos = $derived(videoCards.length > 0);
  const hasCastAndCrew = $derived(studioCards.length > 0 || creditCards.length > 0);
  const seasonCount = $derived(seasonCards.length);
  const totalEpisodeCount = $derived(
    videoCards.length + Object.values(seasonEpisodeCounts).reduce((total, count) => total + count, 0),
  );
  const detailSections = $derived.by((): EntityDetailSection[] => [
    {
      id: "cast-and-crew",
      label: "Cast and Crew",
      icon: Users,
      hidden: !hasCastAndCrew,
    },
  ]);
  const detailTabs = $derived.by((): EntityDetailTab[] => {
    if (!card) return [];
    const tabs: EntityDetailTab[] = [
      {
        id: "details",
        label: "Details",
        icon: Info,
        sections: ["description", "tags", "cast-and-crew"],
      },
    ];

    if (card.links.length > 0) {
      tabs.push({
        id: "metadata",
        label: "Metadata",
        icon: SlidersHorizontal,
        count: card.links.length,
        sections: ["links"],
      });
    }

    return tabs;
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

  async function loadSeries() {
    loadState = "loading";
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
    studioCards = relationshipCards.studioCards;
    creditCards = relationshipCards.creditCards;
    relationshipTags = relationshipCards.relationshipTags;
  }

  function formatDateForHero(value: string): string {
    const match = /^(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})$/.exec(value);
    if (!match?.groups) return value;
    return `${match.groups.day}-${match.groups.month}-${match.groups.year}`;
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
    >
      {#snippet heroMeta()}
        {#if dateAired}
          <span class="meta-item">Date Aired: {dateAired}</span>
        {/if}
        {#if dateAired && (seasonCount > 0 || totalEpisodeCount > 0)}
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
        {#if section.id === "cast-and-crew"}
          <EntityCastAndCrewSection {studioCards} {creditCards} />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if hasSeasons}
      <section class="content-section">
        <h2 class="content-heading">
          <Calendar class="h-4 w-4" />
          Seasons
          <span class="content-count">{seasonCards.length}</span>
        </h2>
        <EntityGrid
          cards={seasonCards}
          prefsKey={`series-${series?.id}-seasons`}
          initialSortBy="position"
          selectable={false}
          emptyTitle="No seasons"
          emptyMessage="This series has no seasons."
        />
      </section>
    {/if}

    {#if hasChildSeries}
      <section class="content-section">
        <h2 class="content-heading">
          <Building2 class="h-4 w-4" />
          Sub-Series
          <span class="content-count">{childSeriesCards.length}</span>
        </h2>
        <EntityGrid
          cards={childSeriesCards}
          prefsKey={`series-${series?.id}-children`}
          selectable={false}
          emptyTitle="No sub-series"
          emptyMessage="This series has no sub-series."
        />
      </section>
    {/if}

    {#if hasVideos}
      <section class="content-section">
        <h2 class="content-heading">
          {hasSeasons ? "Specials" : "Episodes"}
          <span class="content-count">{videoCards.length}</span>
        </h2>
        <EntityGrid
          cards={videoCards}
          prefsKey={`series-${series?.id}-videos`}
          initialSortBy="position"
          selectable={false}
          emptyTitle={hasSeasons ? "No specials" : "No episodes"}
          emptyMessage="No loose videos in this series."
        />
      </section>
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

  /* ── Content sections (seasons, episodes, sub-series) ── */

  .content-section {
    display: grid;
    gap: 0.75rem;
  }

  .content-heading {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin: 0;
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 1.1rem;
    font-weight: 600;
    color: var(--color-text-primary, #f2eed8);
  }

  .content-count {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    font-weight: 600;
    color: var(--color-text-muted, #8a93a6);
    padding: 0.1rem 0.4rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-3, #151a28);
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
