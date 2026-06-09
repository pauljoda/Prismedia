<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Disc3, Info, Play, Shuffle, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchMusicArtist, fetchAudioLibrary, type MusicArtistDetail } from "$lib/api/media";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { assetUrl } from "$lib/api/orval-fetch";
  import { getCapability } from "$lib/api/capabilities";
  import type { AudioTrackListItemDto } from "@prismedia/contracts";
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
  import { useAudioPlayback, type PlaybackContext } from "$lib/stores/audio-playback.svelte";
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();
  const playback = useAudioPlayback()!;

  let loadState: LoadState = $state("loading");
  let artist = $state<MusicArtistDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let albumCards = $state<EntityThumbnailCard[]>([]);
  let creditCards = $state<EntityThumbnailCard[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let queueBusy = $state(false);

  const artistCoverUrl = $derived.by(() => {
    if (!artist) return undefined;
    const images = getCapability(artist.capabilities, "images");
    return assetUrl(images?.coverUrl ?? images?.thumbnailUrl) || undefined;
  });

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!artist) return null;
    return {
      ...entityCardToDetailCard(artist),
      tags: relationshipTags,
    };
  });

  const identifyAction = useIdentifyDetailAction(() => artist?.id, () => artist?.kind);
  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    const actions: EntityDetailActionButton[] = [];
    if (albumCards.length > 0) {
      actions.push(
        {
          id: "play-all",
          label: queueBusy ? "Loading…" : "Play All",
          icon: Play,
          iconFill: "currentColor",
          variant: "primary",
          disabled: queueBusy,
          onClick: () => void playArtist(false),
        },
        {
          id: "shuffle",
          label: "Shuffle",
          icon: Shuffle,
          disabled: queueBusy,
          onClick: () => void playArtist(true),
        },
      );
    }
    if (identifyAction.action) actions.push(identifyAction.action);
    return actions;
  });

  // Keep description + band members on the main "Details" tab; tuck the metadata cards into a
  // separate "Metadata" tab. Empty sections and tabs auto-hide.
  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "members", label: "Members", icon: Users, hidden: creditCards.length === 0 },
    { id: "stats", label: "Stats" },
    { id: "dates", label: "Dates" },
    { id: "classification", label: "Classification" },
    { id: "links", label: "Links" },
  ]);
  const detailTabs = $derived.by((): EntityDetailTab[] => [
    { id: "details", label: "Details", icon: Info, sections: ["description", "tags", "members"] },
    { id: "metadata", label: "Metadata", icon: SlidersHorizontal, sections: ["stats", "dates", "classification", "links"], layout: "grid" },
  ]);

  function artistContext(): PlaybackContext {
    return {
      artistId: artist?.id ?? null,
      artistName: artist?.title ?? null,
      albumTitle: null,
      coverUrl: artistCoverUrl ?? null,
      albumCoverUrls: Object.fromEntries(
        albumCards.map((card) => [card.entity.id, card.cover?.src ?? null]),
      ),
    };
  }

  /** Walk a library and all of its nested sub-libraries, collecting ordered track items. */
  async function collectLibraryTracks(libraryId: string): Promise<AudioTrackListItemDto[]> {
    const detail = await fetchAudioLibrary(libraryId).catch(() => null);
    if (!detail) return [];
    const trackGroup = detail.childrenByKind.find((g) => g.kind === "audio-track");
    const tracks = (trackGroup?.entities ?? [])
      .map((thumb) => entityThumbnailToTrackItem(thumb, detail.id))
      .sort((a, b) => a.sortOrder - b.sortOrder);
    const subLibIds = detail.childrenByKind
      .filter((g) => g.kind === "audio-library")
      .flatMap((g) => g.entities.map((e) => e.id));
    const subTracks = (await Promise.all(subLibIds.map(collectLibraryTracks))).flat();
    return [...tracks, ...subTracks];
  }

  async function playArtist(shuffle: boolean) {
    if (queueBusy || albumCards.length === 0) return;
    queueBusy = true;
    try {
      const albumIds = albumCards.map((c) => c.entity.id);
      const tracks = (await Promise.all(albumIds.map(collectLibraryTracks))).flat();
      if (tracks.length === 0) return;
      playback.play(tracks, shuffle ? undefined : tracks[0].id, artistContext(), { shuffle });
    } finally {
      queueBusy = false;
    }
  }

  onMount(() => {
    void loadArtist();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadArtist();
  });

  $effect(() => {
    if (!artist) return;
    return appChrome.setBreadcrumbs([
      { label: "Artists", href: "/artists" },
      { label: artist.title },
    ]);
  });

  async function loadArtist() {
    loadState = "loading";
    errorMessage = null;
    try {
      const nextArtist = await fetchMusicArtist(page.params.id ?? "");

      const albumGroup = nextArtist.childrenByKind.find((g) => g.kind === "audio-library");
      const albumIds = albumGroup?.entities.map((e) => e.id) ?? [];

      const [albums, relationships] = await Promise.all([
        fetchOrderedEntityThumbnails(albumIds),
        hydrateStandardRelationshipCards(nextArtist),
      ]);

      artist = nextArtist;
      albumCards = thumbnailsToCards(albums, {
        hrefFor: (thumbnail) => resolveEntityHref("audio-library", thumbnail.id),
      });
      creditCards = relationships.creditCards;
      relationshipTags = relationships.relationshipTags;

      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!artist || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(artist, value, (next) => (artist = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!artist) return;
    await toggleOptimisticEntityFlag(artist, "isFavorite", (next) => (artist = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!artist) return;
    await toggleOptimisticEntityFlag(artist, "isOrganized", (next) => (artist = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!artist) return;
    await updateEntityMetadata(artist.id, request, { kind: artist.kind });
    await loadArtist();
  }
</script>

<svelte:head>
  <title>{artist?.title ?? "Artist"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton posterAspect="1 / 1" />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load artist."}</p>
      <button type="button" onclick={() => void loadArtist()}>Retry</button>
    </div>
  {:else if card && artist}
    <EntityDetail
      {card}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      peopleLabel="Members"
      posterSize="large"
      actionButtons={heroActions}
      tabs={detailTabs}
      sections={detailSections}
    >
      {#snippet heroMeta()}
        {#if albumCards.length > 0}
          <span class="meta-item">{albumCards.length} {albumCards.length === 1 ? "album" : "albums"}</span>
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "members" && creditCards.length > 0}
          <EntityCastAndCrewSection studioCards={[]} {creditCards} castLabel="Members" />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if albumCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <Disc3 class="h-4 w-4" />
          Albums
          <span class="content-count">{albumCards.length}</span>
        </h2>
        <EntityGrid
          cards={albumCards}
          prefsKey={`artist-${artist?.id}-albums`}
          selectable={false}
          emptyTitle="No albums"
          emptyMessage="No albums for this artist."
        />
      </section>
    {:else}
      <div class="empty-children">
        <p>No albums grouped under this artist yet.</p>
      </div>
    {/if}
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }

  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

</style>
