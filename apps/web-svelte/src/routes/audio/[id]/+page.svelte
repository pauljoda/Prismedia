<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { CloudDownload, Info, MicVocal, Music, Play, Shuffle, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { fetchAudioLibrary, type AudioLibraryDetail } from "$lib/api/media";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { assetUrl } from "$lib/api/orval-fetch";
  import { getCapability } from "$lib/api/capabilities";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { CAPABILITY_KIND, CREDIT_ROLE } from "$lib/entities/entity-codes";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { AudioTrackListItemDto } from "$lib/entities/media-view-models";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import { entityThumbnailToTrackItem } from "$lib/entities/audio-track-items";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityCastAndCrewSection from "$lib/components/entities/EntityCastAndCrewSection.svelte";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import AudioTrackList from "$lib/components/AudioTrackList.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome, type AppBreadcrumb } from "$lib/stores/app-chrome.svelte";
  import { useAudioPlayback, type PlaybackContext } from "$lib/stores/audio-playback.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();
  const playback = useAudioPlayback()!;

  let loadState: LoadState = $state("loading");
  let library = $state<AudioLibraryDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let childCards = $state<EntityThumbnailCard[]>([]);
  let artistCards = $state<EntityThumbnailCard[]>([]);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let trackItems = $state<AudioTrackListItemDto[]>([]);
  let artistLink = $state<{ id: string; title: string } | null>(null);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!library) return null;
    return {
      ...entityCardToDetailCard(library),
      tags: relationshipTags,
      credits: relationshipCredits,
      studio: relationshipStudio,
    };
  });

  const studio = $derived(relationshipStudio);

  const dates = $derived(card?.dates ?? []);

  const subLibraryCards = $derived(requestableDirectChildCards(library?.id, childCards));
  const coverUrl = $derived.by(() => {
    if (!library) return undefined;
    const images = getCapability(library.capabilities, CAPABILITY_KIND.images);
    return assetUrl(images?.coverUrl ?? images?.thumbnailUrl) || undefined;
  });
  const identifyAction = useIdentifyDetailAction(() => library);

  // A phantom album's request state and its direct requestable sub-libraries share the same
  // Acquisition surface as every other Entity hierarchy. Tracks intentionally stay out until
  // AudioTrack becomes a RequestKindRegistry acquisition unit; exposing them now would offer an
  // action the server correctly rejects.
  const acq = useEntityAcquisition({
    entityId: () => library?.id,
    capabilities: () => library?.capabilities,
    childCards: () => subLibraryCards,
    onChanged: () => loadLibrary({ showLoading: false }),
    onPruned: () => goto("/audio"),
  });
  const fileManagement = {
    onDeleted: () => goto("/audio"),
    onReverted: () => refreshAfterManagedFileRevert(
      acq,
      () => loadLibrary({ showLoading: false }),
    ),
  };

  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    const actions: EntityDetailActionButton[] = [];
    if (trackItems.length > 0) {
      actions.push(
        {
          id: "play-all",
          label: "Play All",
          icon: Play,
          iconFill: "currentColor",
          variant: "primary",
          onClick: playAll,
        },
        {
          id: "shuffle",
          label: "Shuffle",
          icon: Shuffle,
          onClick: shuffleAll,
        },
      );
    }
    if (identifyAction.action) actions.push(identifyAction.action);
    return actions;
  });

  // Description + artist/studio/performers stay on the main "Details" tab; metadata cards move to a
  // separate "Metadata" tab. Built-in sections come from EntityDetail's core catalog; only the
  // artist rail and the credits label override are declared here.
  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "artists", hidden: artistCards.length === 0 },
    { id: "credits", label: "Performers", icon: Users },
    { id: "acquisition" },
  ]);
  const detailTabs = $derived.by((): EntityDetailTab[] => [
    { id: "details", label: "Details", icon: Info, sections: ["description", "tags", "artists", "studio", "credits"] },
    { id: "metadata", label: "Metadata", icon: SlidersHorizontal, sections: ["stats", "dates", "classification", "technical", "source", "links"], layout: "grid" },
    ...(acq.visible
      ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
      : []),
  ]);

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
    // Albums are scanned under their artist, so surface the artist as a breadcrumb crumb
    // ("Audio / Imagine Dragons / Evolve") when the music-artist parent resolved.
    const crumbs: AppBreadcrumb[] = [{ label: "Audio", href: "/audio" }];
    if (artistLink) {
      crumbs.push({ label: artistLink.title, href: resolveEntityHref("music-artist", artistLink.id) });
    }
    crumbs.push({ label: library.title });
    return appChrome.setBreadcrumbs(crumbs);
  });

  async function loadLibrary(options = { showLoading: true }) {
    // Silent for acquisition-driven refreshes: update in place instead of flashing the skeleton.
    if (options.showLoading || !library) loadState = "loading";
    errorMessage = null;
    try {
      const nextLibrary = await fetchAudioLibrary(page.params.id ?? "");

      // Separate track children from non-track children using the entity groups
      const trackGroup = nextLibrary.childrenByKind.find((g) => g.kind === "audio-track");
      const nonTrackGroups = nextLibrary.childrenByKind.filter((g) => g.kind !== "audio-track");
      const nonTrackIds = nonTrackGroups.flatMap((g) => g.entities.map((e) => e.id));

      // The album's parent (when set) is its artist grouping; resolve its title for a back-link.
      const parentId = nextLibrary.parentEntityId;
      const [children, relationships, parentThumbs] = await Promise.all([
        fetchOrderedEntityThumbnails(nonTrackIds),
        hydrateStandardRelationshipCards(nextLibrary),
        parentId ? fetchOrderedEntityThumbnails([parentId]) : Promise.resolve([]),
      ]);

      const parentThumb = parentThumbs.find((t) => t.kind === "music-artist");
      const resolvedArtist = parentThumb ? { id: parentThumb.id, title: parentThumb.title } : null;
      artistLink = resolvedArtist;

      library = nextLibrary;
      childCards = thumbnailsToCards(children, {
        hrefFor: (thumbnail) => resolveEntityHref("audio-library", thumbnail.id),
      });
      relationshipStudio = relationships.studio;
      // An album is always scanned under its artist, so surface that music-artist as the lead
      // "Artist" card (its own thumbnail, linking to /artists/{id}). The credit list stays
      // unfiltered: it feeds the edit draft, and a hidden credit would be deleted on save.
      artistCards = parentThumb
        ? thumbnailsToCards([parentThumb], {
            hrefFor: (thumbnail) => resolveEntityHref("music-artist", thumbnail.id),
          })
        : [];
      relationshipCredits = relationships.credits;
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

  async function handleTrackRatingChange(trackId: string, value: number | null) {
    const previousTrackItems = trackItems;
    trackItems = trackItems.map((track) =>
      track.id === trackId ? { ...track, rating: value } : track,
    );

    try {
      await updateEntityRating(trackId, value);
    } catch (err) {
      trackItems = previousTrackItems;
      console.warn("Unable to update audio track rating", err);
    }
  }

  async function handleTrackRename(track: AudioTrackListItemDto, title: string) {
    const previousTrackItems = trackItems;
    trackItems = trackItems.map((item) =>
      item.id === track.id ? { ...item, title } : item,
    );

    try {
      await updateEntityMetadata(track.id, {
        fields: ["title"],
        patch: {
          title,
          description: null,
          externalIds: {},
          urls: [],
          tags: [],
          studio: null,
          credits: [],
          dates: {},
          stats: {},
          positions: {},
          classification: null,
        },
      }, { kind: "audio-track" });
    } catch (err) {
      trackItems = previousTrackItems;
      throw err;
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

  function albumContext(): PlaybackContext {
    return {
      albumId: library?.id ?? null,
      albumTitle: library?.title ?? null,
      artistId: artistLink?.id ?? null,
      artistName: artistLink?.title ?? null,
      coverUrl: coverUrl ?? null,
    };
  }

  function playAll() {
    const firstTrack = trackItems[0];
    if (!firstTrack) return;
    playback.play(trackItems, firstTrack.id, albumContext(), { shuffle: false });
  }

  function shuffleAll() {
    if (trackItems.length === 0) return;
    playback.play(trackItems, undefined, albumContext(), { shuffle: true });
  }

  function playTrack(trackId: string) {
    // Re-clicking the current track toggles play/pause; otherwise (re)load the album from that track.
    if (playback.isCurrent(trackId)) playback.toggle();
    else playback.play(trackItems, trackId, albumContext(), { shuffle: false });
  }
</script>

<svelte:head>
  <title>{library?.title ?? "Audio"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton posterAspect="1 / 1" />
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
      defaultCreditRole={CREDIT_ROLE.artist}
      posterSize="large"
      actionButtons={heroActions}
      tabs={detailTabs}
      sections={detailSections}
    >
      {#snippet heroMeta()}
        {#if artistLink}
          <a href={resolveEntityHref("music-artist", artistLink.id)} class="meta-item is-studio">{artistLink.title}</a>
        {/if}
        {#if studio}
          {#if artistLink}<span class="meta-sep"></span>{/if}
          <a href={resolveEntityHref("studio", studio.id)} class="meta-item is-studio">{studio.title}</a>
        {/if}
        <EntityDetailHeroDates {dates} leadingSeparator={Boolean(artistLink || studio)} />
        {#if trackItems.length > 0}
          {#if artistLink || studio || dates.length > 0}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{trackItems.length} {trackItems.length === 1 ? "track" : "tracks"}</span>
        {/if}
      {/snippet}


      {#snippet sectionContent(section)}
        {#if section.id === "artists" && artistCards.length > 0}
          <EntityCastAndCrewSection
            relatedCards={artistCards}
            relatedLabel="Artist"
            relatedIcon={MicVocal}
            castLabel="Performers"
          />
        {:else if section.id === "acquisition"}
          <EntityAcquisitionCard
            {acq}
            entity={library}
            {fileManagement}
            onCancelled={() => void loadLibrary({ showLoading: false })}
            onImported={() => loadLibrary({ showLoading: false })}
          />
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
          emptyTitle="No sub-libraries"
          emptyMessage="No sub-libraries in this collection."
        />
      </section>
    {/if}

    {#if trackItems.length > 0}
      <AudioTrackList
        tracks={trackItems}
        activeTrackId={playback.currentTrack?.id ?? null}
        isPlaying={playback.playing}
        onPlay={playTrack}
        onRatingChange={handleTrackRatingChange}
        onRename={handleTrackRename}
      />
    {/if}

    {#if trackItems.length === 0 && subLibraryCards.length === 0}
      <div class="empty-children">
        <p>No tracks or sub-libraries in this audio library yet.</p>
      </div>
    {/if}
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c49a5a); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }


  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

</style>
