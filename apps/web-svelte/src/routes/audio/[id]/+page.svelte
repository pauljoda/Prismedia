<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { ArrowLeft, Users, Music } from "@lucide/svelte";
  import {
    fetchAudioLibrary,
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
    type AudioLibraryDetail,
  } from "$lib/api/prismedia";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { getAllChildIds } from "$lib/entities/entity-children";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import {
    fetchOrderedEntityThumbnails,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();

  let loadState: LoadState = $state("loading");
  let library = $state<AudioLibraryDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let childCards = $state<EntityThumbnailCard[]>([]);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!library) return null;
    return entityCardToDetailCard(library);
  });

  const studio = $derived.by((): { id: string; title: string } | null => null);

  const credits = $derived.by((): Array<{ id: string; title: string }> => []);

  const dates = $derived.by(() => {
    if (!library) return [];
    const cap = getCapability(library.capabilities, "dates");
    return cap?.items ?? [];
  });

  const trackCards = $derived(childCards.filter((c) => c.entity.kind === "audio-track"));
  const subLibraryCards = $derived(childCards.filter((c) => c.entity.kind === "audio-library"));

  onMount(() => {
    void loadLibrary();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadLibrary();
  });

  async function loadLibrary() {
    loadState = "loading";
    errorMessage = null;
    try {
      const nextLibrary = await fetchAudioLibrary(page.params.id ?? "");
      library = nextLibrary;
      childCards = thumbnailsToCards(await fetchOrderedEntityThumbnails(getAllChildIds(nextLibrary)), {
        hrefFor: (thumbnail) => thumbnail.kind === "audio-library"
          ? resolveEntityHref("audio-library", thumbnail.id)
          : resolveEntityHref(thumbnail.kind, thumbnail.id, { kind: "audio-library", id: nextLibrary.id }),
      });
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
  <a href="/audio" class="back-link">
    <ArrowLeft class="h-4 w-4" />
    Audio
  </a>

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
        {#if childCards.length > 0}
          {#if studio || dates.length > 0}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{childCards.length} {childCards.length === 1 ? "item" : "items"}</span>
        {/if}
      {/snippet}

      {#snippet afterBody()}
        {#if credits.length > 0}
          <div class="credits-section">
            <h2 class="section-label">
              <Users class="h-4 w-4" />
              Artists
            </h2>
            <div class="credits-grid">
              {#each credits as person (person.id)}
                <a href={resolveEntityHref("person", person.id)} class="credit-chip">
                  {person.title}
                </a>
              {/each}
            </div>
          </div>
        {/if}
      {/snippet}
    </EntityDetail>

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

    {#if trackCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          Tracks
          <span class="content-count">{trackCards.length}</span>
        </h2>
        <EntityGrid
          cards={trackCards}
          prefsKey={`audio-${library?.id}-tracks`}
          selectable={false}
          emptyTitle="No tracks"
          emptyMessage="No tracks in this library."
        />
      </section>
    {/if}

    {#if childCards.length === 0}
      <div class="empty-children">
        <p>No tracks or sub-libraries in this audio library yet.</p>
      </div>
    {/if}
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: clamp(1rem, 3vw, 2rem); max-width: 72rem; margin: 0 auto; }
  .back-link { display: inline-flex; align-items: center; gap: 0.4rem; color: var(--color-text-muted, #8a93a6); font-size: 0.78rem; text-decoration: none; font-family: var(--font-mono, "JetBrains Mono", monospace); text-transform: uppercase; letter-spacing: 0.04em; transition: color 0.15s; }
  .back-link:hover { color: var(--color-text-primary, #f2eed8); }
  .loading-shell { min-height: 28rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-2, #101420); animation: pulse 1.2s ease-in-out infinite; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c49a5a); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

  .credits-section { padding: 1rem 1.5rem; border-top: 1px solid var(--color-border, #1c2235); }
  .section-label { display: flex; align-items: center; gap: 0.45rem; margin: 0 0 0.75rem; font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; letter-spacing: 0.06em; text-transform: uppercase; color: var(--color-text-muted, #8a93a6); }
  .credits-grid { display: flex; flex-wrap: wrap; gap: 0.35rem; }
  .credit-chip { padding: 0.22rem 0.55rem; font-size: 0.75rem; color: var(--color-text-secondary, #c4c9d4); border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); text-decoration: none; transition: border-color 0.15s, color 0.15s; }
  .credit-chip:hover { color: var(--color-text-accent, #c49a5a); border-color: rgba(196, 154, 90, 0.35); }

  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

  @media (min-width: 640px) { .credits-section { padding: 1rem 2rem; } }
  @keyframes pulse { 0%, 100% { opacity: 0.45; } 50% { opacity: 0.85; } }
</style>
