<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Music, Play } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchAudioTrack, type AudioTrackDetail } from "$lib/api/media";
  import { fetchEntityThumbnails } from "$lib/api/entities";
  import {
    updateEntityFlags,
    updateEntityMetadata,
    updateEntityRating,
  } from "$lib/api/entity-mutations";
  import { assetUrl } from "$lib/api/orval-fetch";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { hydrateStandardRelationshipCards } from "$lib/entities/entity-relationship-thumbnails";
  import { audioTrackDetailToListItem } from "$lib/entities/audio-track-items";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";
  import { useAudioPlayback } from "$lib/stores/audio-playback.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();
  const playback = useAudioPlayback()!;

  let loadState: LoadState = $state("loading");
  let track = $state<AudioTrackDetail | null>(null);
  let errorMessage = $state<string | null>(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let studioCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let parentCoverUrl = $state<string | undefined>(undefined);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!track) return null;
    return {
      ...entityCardToDetailCard(track),
      tags: relationshipTags,
    };
  });

  const studio = $derived(studioCards[0]?.entity ?? null);

  const dates = $derived.by(() => {
    if (!track) return [];
    return getCapability(track.capabilities, "dates")?.items ?? [];
  });

  const trackItem = $derived(track ? audioTrackDetailToListItem(track) : null);

  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    if (!trackItem) return [];
    const isCurrent = playback.isCurrent(trackItem.id);
    return [{
      id: "play",
      label: isCurrent && playback.playing ? "Pause" : "Play",
      icon: Play,
      iconFill: "currentColor",
      variant: "primary",
      onClick: playTrack,
    }];
  });

  function playTrack() {
    if (!trackItem) return;
    if (playback.isCurrent(trackItem.id)) {
      playback.toggle();
      return;
    }
    playback.play([trackItem], trackItem.id, {
      albumTitle: trackItem.embeddedAlbum,
      artistName: trackItem.embeddedArtist,
      coverUrl: parentCoverUrl ?? null,
    });
  }

  onMount(() => {
    void loadTrack();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadTrack();
  });

  $effect(() => {
    if (!track) return;
    return appChrome.setBreadcrumbs([
      { label: "Audio", href: "/audio" },
      { label: track.title },
    ]);
  });

  async function loadTrack() {
    loadState = "loading";
    errorMessage = null;
    try {
      const nextTrack = await fetchAudioTrack(page.params.id ?? "");
      const relationships = await hydrateStandardRelationshipCards(nextTrack);
      track = nextTrack;
      studioCards = relationships.studioCards;
      creditCards = relationships.creditCards;
      relationshipTags = relationships.relationshipTags;

      // Fetch parent library cover for player thumbnail
      if (nextTrack.parentEntityId) {
        fetchEntityThumbnails([nextTrack.parentEntityId])
          .then((thumbs) => {
            const parentThumb = thumbs[0];
            parentCoverUrl = assetUrl(parentThumb?.coverUrl) || undefined;
          })
          .catch(() => {});
      }

      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!track || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(track, value, (next) => (track = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!track) return;
    await toggleOptimisticEntityFlag(track, "isFavorite", (next) => (track = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!track) return;
    await toggleOptimisticEntityFlag(track, "isOrganized", (next) => (track = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!track) return;
    await updateEntityMetadata(track.id, request, { kind: track.kind });
    await loadTrack();
  }
</script>

<svelte:head>
  <title>{track?.title ?? "Audio Track"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load audio track."}</p>
      <button type="button" onclick={() => void loadTrack()}>Retry</button>
    </div>
  {:else if card && track && trackItem}
    <EntityDetail
      {card}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      peopleLabel="Performers"
      posterSize="large"
      actionButtons={heroActions}
    >
      {#snippet heroMeta()}
        {#if studio}
          <a href={resolveEntityHref("studio", studio.id)} class="meta-item is-studio">{studio.title}</a>
        {/if}
        {#each dates as date, i (date.code)}
          {#if studio || i > 0}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{date.value}</span>
        {/each}
      {/snippet}

      {#snippet afterBody()}
        {#if studioCards.length > 0 || creditCards.length > 0}
          <div class="credits-section">
            <EntityCastAndCrewSection {studioCards} {creditCards} castLabel="Performers" />
          </div>
        {:else}
          <div class="empty-inline">
            <Music class="h-4 w-4" />
            <span>No credited performers yet.</span>
          </div>
        {/if}
      {/snippet}
    </EntityDetail>
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }
  .credits-section { display: grid; gap: 0.7rem; margin-top: 1rem; }
  .empty-inline { display: inline-flex; align-items: center; gap: 0.45rem; color: var(--color-text-muted, #8a93a6); font-size: 0.8rem; }
  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c49a5a); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }
</style>
