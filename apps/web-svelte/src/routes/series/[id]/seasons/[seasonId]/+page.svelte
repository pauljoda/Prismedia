<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Film, Info, SlidersHorizontal, Users } from "@lucide/svelte";
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
  import { getChildIds } from "$lib/entities/entity-children";
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import type { EntityDetailTag } from "$lib/entities/entity-detail";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import EntityDetail, {
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
  let parentSeries = $state<VideoSeriesDetail | null>(null);
  let season = $state<VideoSeasonDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let episodeCards = $state<EntityThumbnailCard[]>([]);
  let studioCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);

  const seriesId = $derived(page.params.id ?? "");
  const seasonId = $derived(page.params.seasonId ?? "");

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!season) return null;
    return {
      ...entityCardToDetailCard(season),
      tags: relationshipTags,
    };
  });

  const seasonNumber = $derived.by(() => {
    if (!season) return null;
    const pos = getCapability(season.capabilities, "position");
    const item = pos?.items.find((p) => p.code === "season");
    return item ? Number(item.value) : null;
  });

  const dates = $derived.by(() => {
    if (!season) return [];
    const cap = getCapability(season.capabilities, "dates");
    return cap?.items ?? [];
  });

  const hasCastAndCrew = $derived(studioCards.length > 0 || creditCards.length > 0);
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
    void loadSeason();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadSeason();
  });

  $effect(() => {
    if (!season) return;
    return appChrome.setBreadcrumbs([
      { label: "Series", href: "/series" },
      { label: parentSeries?.title ?? "Series", href: `/series/${seriesId}` },
      { label: season.title },
    ]);
  });

  async function loadSeason() {
    loadState = "loading";
    errorMessage = null;
    try {
      const [seriesDetail, seasonDetail] = await Promise.all([
        fetchSeries(seriesId),
        fetchSeason(seriesId, seasonId),
      ]);
      parentSeries = seriesDetail;
      season = seasonDetail;
      await Promise.all([
        hydrateEpisodeThumbnails(seasonDetail),
        hydrateSeasonRelationships(seasonDetail, seriesDetail),
      ]);
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!season || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(season, value, (next) => (season = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!season) return;
    await toggleOptimisticEntityFlag(season, "isFavorite", (next) => (season = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!season) return;
    await toggleOptimisticEntityFlag(season, "isOrganized", (next) => (season = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!season) return;
    await updateEntityMetadata(season.id, request, { kind: season.kind });
    await loadSeason();
  }

  async function hydrateEpisodeThumbnails(seasonDetail: VideoSeasonDetail) {
    const episodeIds = getChildIds(seasonDetail, ENTITY_KIND.video);
    episodeCards = thumbnailsToCards(await fetchOrderedEntityThumbnails(episodeIds));
  }

  async function hydrateSeasonRelationships(
    seasonDetail: VideoSeasonDetail,
    seriesDetail: VideoSeriesDetail,
  ) {
    let relationshipCards = await hydrateStandardRelationshipCards(seasonDetail);
    if (
      relationshipCards.studioCards.length === 0 &&
      relationshipCards.creditCards.length === 0 &&
      relationshipCards.relationshipTags.length === 0
    ) {
      relationshipCards = await hydrateStandardRelationshipCards(seriesDetail);
    }

    studioCards = relationshipCards.studioCards;
    creditCards = relationshipCards.creditCards;
    relationshipTags = relationshipCards.relationshipTags;
  }
</script>

<svelte:head>
  <title>{season?.title ?? "Season"} · Prismedia</title>
</svelte:head>

<div class="season-page">
  {#if loadState === "loading"}
    <div class="loading-shell" aria-busy="true"></div>
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load season."}</p>
      <button type="button" onclick={() => void loadSeason()}>Retry</button>
    </div>
  {:else if card && season}
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
    >
      {#snippet heroMeta()}
        {#if parentSeries}
          <span class="meta-item is-studio">{parentSeries.title}</span>
        {/if}
        {#if seasonNumber != null}
          <span class="meta-sep"></span>
          <span class="meta-item">Season {seasonNumber}</span>
        {/if}
        {#each dates as date, i (date.code)}
          <span class="meta-sep"></span>
          <span class="meta-item">{date.value}</span>
        {/each}
      {/snippet}

      {#snippet heroBadges()}
        {#if seasonNumber != null}
          <span class="hero-badge">S{String(seasonNumber).padStart(2, "0")}</span>
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "cast-and-crew"}
          <EntityCastAndCrewSection {studioCards} {creditCards} />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if episodeCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <Film class="h-4 w-4" />
          Episodes
          <span class="content-count">{episodeCards.length}</span>
        </h2>
        <EntityGrid
          cards={episodeCards}
          prefsKey={`season-${seasonId}-episodes`}
          initialSortBy="position"
          dockControls={false}
          selectable={false}
          showPagination={false}
          emptyTitle="No episodes"
          emptyMessage="No episodes found in this season."
        />
      </section>
    {:else}
      <div class="empty-children">
        <p>No episodes found in this season yet.</p>
      </div>
    {/if}
  {/if}
</div>

<style>
  .season-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
    max-width: none;
    margin: 0;
  }

  .loading-shell {
    min-height: 28rem;
    border: 1px solid var(--color-border, #1c2235);
    background: var(--color-surface-2, #101420);
    animation: pulse 1.2s ease-in-out infinite;
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

  :global(.meta-item) {
    white-space: nowrap;
    font-size: 0.82rem;
  }

  :global(.meta-item.is-studio) {
    color: var(--color-text-accent, #c49a5a);
  }

  :global(.meta-sep) {
    display: inline-block;
    width: 3px;
    height: 3px;
    margin: 0 0.5rem;
    background: var(--color-text-muted, #8a93a6);
    opacity: 0.5;
  }

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

  @keyframes pulse {
    0%, 100% { opacity: 0.45; }
    50% { opacity: 0.85; }
  }
</style>
