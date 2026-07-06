<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Film, Info, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { fetchSeason, fetchSeries, type VideoSeasonDetail, type VideoSeriesDetail } from "$lib/api/media";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { getCapability, isWanted } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { getChildIds } from "$lib/entities/entity-children";
  import type { EntityDetailCredit, EntityDetailTag } from "$lib/entities/entity-detail";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
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
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);

  const seriesId = $derived(page.params.id ?? "");
  const seasonId = $derived(page.params.seasonId ?? "");

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
    const pos = getCapability(season.capabilities, "position");
    const item = pos?.items.find((p) => p.code === "season");
    return item ? Number(item.value) : null;
  });

  const dates = $derived(card?.dates ?? []);

  // Seasons are not relationship owners: tags, studio, and cast belong to the series and
  // are shown here as inherited context only. Editing them on a season would write through
  // to the series via the backend's owner resolution, so the sections are read-only.
  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "tags", label: "Tags", editable: false },
    { id: "studio", label: "Studio", editable: false },
    { id: "credits", label: "Cast", icon: Users, editable: false },
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
    ];
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

  // A phantom season's "Search for release" (a season-pack acquisition) and its acquisition
  // management live in the EntityAcquisitionCard below the detail, exactly like a wanted movie.
  const seasonWanted = $derived(!!season && isWanted(season.capabilities));

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
      !relationshipCards.studio &&
      relationshipCards.credits.length === 0 &&
      relationshipCards.relationshipTags.length === 0
    ) {
      relationshipCards = await hydrateStandardRelationshipCards(seriesDetail);
    }

    relationshipCredits = relationshipCards.credits;
    relationshipStudio = relationshipCards.studio;
    relationshipTags = relationshipCards.relationshipTags;
  }
</script>

<svelte:head>
  <title>{season?.title ?? "Season"} · Prismedia</title>
</svelte:head>

<div class="season-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
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
      actionButtons={[]}
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
          <span class="hero-badge wanted">Wanted</span>
        {/if}
        {#if seasonNumber != null}
          <span class="hero-badge">S{String(seasonNumber).padStart(2, "0")}</span>
        {/if}
      {/snippet}

    </EntityDetail>

    <EntityAcquisitionCard
      entityId={season?.id}
      capabilities={season?.capabilities}
      onChanged={loadSeason}
      onCancelled={() => void loadSeason()}
    />

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


</style>
