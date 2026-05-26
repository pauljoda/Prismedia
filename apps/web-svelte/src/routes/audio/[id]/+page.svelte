<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Music } from "@lucide/svelte";
  import {
    fetchAudioLibrary,
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
    type AudioLibraryDetail,
  } from "$lib/api/prismedia";
  import { apiAssetUrl } from "$lib/api/orval-fetch";
  import type { AudioTrackListItemDto } from "@prismedia/contracts";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import { entityThumbnailToTrackItem } from "$lib/entities/audio-track-items";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import EntityDetail, {
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import AudioVidStackPlayer from "$lib/components/AudioVidStackPlayer.svelte";
  import AudioTrackList from "$lib/components/AudioTrackList.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let library = $state<AudioLibraryDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let childCards = $state<EntityThumbnailCard[]>([]);
  let studioCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let trackItems = $state<AudioTrackListItemDto[]>([]);

  let activeTrackId = $state<string | null>(null);
  let isPlaying = $state(false);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!library) return null;
    return {
      ...entityCardToDetailCard(library),
      tags: relationshipTags,
    };
  });

  const studio = $derived(studioCards[0]?.entity ?? null);

  const dates = $derived.by(() => {
    if (!library) return [];
    const cap = getCapability(library.capabilities, "dates");
    return cap?.items ?? [];
  });

  const subLibraryCards = $derived(childCards.filter((c) => c.entity.kind === "audio-library"));
  const coverUrl = $derived.by(() => {
    if (!library) return undefined;
    const images = getCapability(library.capabilities, "images");
    return apiAssetUrl(images?.coverUrl ?? images?.thumbnailUrl);
  });

  onMount(() => {
    void loadLibrary();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadLibrary();
  });

  $effect(() => {
    if (!library) return;
    return appChrome.setBreadcrumbs([
      { label: "Audio", href: "/audio" },
      { label: library.title },
    ]);
  });

  async function loadLibrary() {
    loadState = "loading";
    errorMessage = null;
    try {
      const nextLibrary = await fetchAudioLibrary(page.params.id ?? "");

      // Separate track children from non-track children using the entity groups
      const trackGroup = nextLibrary.childrenByKind.find((g) => g.kind === "audio-track");
      const nonTrackGroups = nextLibrary.childrenByKind.filter((g) => g.kind !== "audio-track");
      const nonTrackIds = nonTrackGroups.flatMap((g) => g.entities.map((e) => e.id));

      const [children, relationships] = await Promise.all([
        fetchOrderedEntityThumbnails(nonTrackIds),
        hydrateStandardRelationshipCards(nextLibrary),
      ]);

      library = nextLibrary;
      childCards = thumbnailsToCards(children, {
        hrefFor: (thumbnail) => resolveEntityHref("audio-library", thumbnail.id),
      });
      studioCards = relationships.studioCards;
      creditCards = relationships.creditCards;
      relationshipTags = relationships.relationshipTags;

      // Build track items from entity thumbnails already in the response — no N+1 fetches
      trackItems = (trackGroup?.entities ?? [])
        .map((thumb) => entityThumbnailToTrackItem(thumb, nextLibrary.id))
        .sort((a, b) => a.sortOrder - b.sortOrder);

      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!library || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(library, value, (next) => (library = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!library) return;
    await toggleOptimisticEntityFlag(library, "isFavorite", (next) => (library = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!library) return;
    await toggleOptimisticEntityFlag(library, "isOrganized", (next) => (library = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!library) return;
    await updateEntityMetadata(library.id, request, { kind: library.kind });
    await loadLibrary();
  }
</script>

<svelte:head>
  <title>{library?.title ?? "Audio"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <div class="loading-shell" aria-busy="true"></div>
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load audio library."}</p>
      <button type="button" onclick={() => void loadLibrary()}>Retry</button>
    </div>
  {:else if card && library}
    <EntityDetail
      {card}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      peopleLabel="Performers"
      posterSize="large"
    >
      {#snippet heroMeta()}
        {#if studio}
          <a href={resolveEntityHref("studio", studio.id)} class="meta-item is-studio">{studio.title}</a>
        {/if}
        {#each dates as date, i (date.code)}
          {#if studio || i > 0}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{date.value}</span>
        {/each}
        {#if trackItems.length > 0}
          {#if studio || dates.length > 0}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{trackItems.length} {trackItems.length === 1 ? "track" : "tracks"}</span>
        {/if}
      {/snippet}

      {#snippet afterBody()}
        {#if studioCards.length > 0 || creditCards.length > 0}
          <div class="credits-section">
            <EntityCastAndCrewSection {studioCards} {creditCards} castLabel="Performers" />
          </div>
        {/if}
      {/snippet}
    </EntityDetail>

    {#if trackItems.length > 0}
      <AudioVidStackPlayer
        tracks={trackItems}
        {activeTrackId}
        onTrackChange={(id) => (activeTrackId = id)}
        libraryCoverUrl={coverUrl}
        onPlayingChange={(p) => (isPlaying = p)}
      />

      <AudioTrackList
        tracks={trackItems}
        {activeTrackId}
        {isPlaying}
        onPlay={(id) => (activeTrackId = id)}
      />
    {/if}

    {#if subLibraryCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <Music class="h-4 w-4" />
          Sub-Libraries
          <span class="content-count">{subLibraryCards.length}</span>
        </h2>
        <EntityGrid
          cards={subLibraryCards}
          prefsKey={`audio-${library?.id}-children`}
          selectable={false}
          emptyTitle="No sub-libraries"
          emptyMessage="No sub-libraries in this collection."
        />
      </section>
    {/if}

    {#if trackItems.length === 0 && subLibraryCards.length === 0}
      <div class="empty-children">
        <p>No tracks or sub-libraries in this audio library yet.</p>
      </div>
    {/if}
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: clamp(1rem, 3vw, 2rem); padding-bottom: 10rem; max-width: 72rem; margin: 0 auto; }
  .loading-shell { min-height: 28rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-2, #101420); animation: pulse 1.2s ease-in-out infinite; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c49a5a); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

  .credits-section { padding: 1rem 1.5rem; border-top: 1px solid var(--color-border, #1c2235); }

  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

  @media (min-width: 640px) { .credits-section { padding: 1rem 2rem; } }
  @keyframes pulse { 0%, 100% { opacity: 0.45; } 50% { opacity: 0.85; } }
</style>
