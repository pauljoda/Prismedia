<script lang="ts">
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { CloudDownload, Info, MicVocal, Music, Play, Shuffle, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import {
    updateEntityRating,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { assetUrl } from "$lib/api/orval-fetch";
  import { getCapability } from "$lib/api/capabilities";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
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
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import AudioTrackList from "$lib/components/AudioTrackList.svelte";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import type { AppBreadcrumb } from "$lib/stores/app-chrome.svelte";
  import { useAudioPlayback, type PlaybackContext } from "$lib/stores/audio-playback.svelte";

  const playback = useAudioPlayback()!;

  let childCards = $state<EntityThumbnailCard[]>([]);
  let artistCards = $state<EntityThumbnailCard[]>([]);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let trackItems = $state<AudioTrackListItemDto[]>([]);
  let trackCards = $state<EntityThumbnailCard[]>([]);
  let artistLink = $state<{ id: string; title: string } | null>(null);

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => page.params.id ?? "",
    load: ({ signal }) => loadLibrary(signal),
    breadcrumbs: (entity) => {
      // Albums are scanned under their artist, so surface the artist as a breadcrumb crumb
      // ("Audio / Imagine Dragons / Evolve") when the music-artist parent resolved.
      const crumbs: AppBreadcrumb[] = [{ label: "Audio", href: "/audio" }];
      if (artistLink) {
        crumbs.push({
          label: artistLink.title,
          href: resolveEntityHref(ENTITY_KIND.musicArtist, artistLink.id),
        });
      }
      crumbs.push({ label: entity.title });
      return crumbs;
    },
  });
  const library = $derived(detail.entity);

  const playableTrackItems = $derived(
    trackItems.filter((track) => track.hasSourceMedia !== false && track.isWanted !== true),
  );

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

  // Albums expose their provider track graph through the same shared child-monitoring surface as
  // seasons/episodes. Wanted tracks can start or stop their own missing-content acquisition.
  const acq = useEntityAcquisition({
    entityId: () => library?.id,
    capabilities: () => library?.capabilities,
    childCards: () => requestableDirectChildCards(library?.id, [...subLibraryCards, ...trackCards]),
    onChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto("/audio"),
  });
  const fileManagement = {
    onDeleted: () => goto("/audio"),
    onReverted: () => refreshAfterManagedFileRevert(
      acq,
      () => detail.reload({ showLoading: false }),
    ),
  };

  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    const actions: EntityDetailActionButton[] = [];
    if (playableTrackItems.length > 0) {
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

  async function loadLibrary(signal: AbortSignal): Promise<EntityCardFull> {
      const nextLibrary = await fetchEntity(page.params.id ?? "", { signal });

      // Separate track children from non-track children using the entity groups
      const trackGroup = nextLibrary.childrenByKind.find((g) => g.kind === ENTITY_KIND.audioTrack);
      const nonTrackGroups = nextLibrary.childrenByKind.filter((g) => g.kind !== ENTITY_KIND.audioTrack);
      const nonTrackIds = nonTrackGroups.flatMap((g) => g.entities.map((e) => e.id));

      // The album's parent (when set) is its artist grouping; resolve its title for a back-link.
      const parentId = nextLibrary.parentEntityId;
      const [children, relationships, parentThumbs] = await Promise.all([
        fetchOrderedEntityThumbnails(nonTrackIds, { signal }),
        hydrateStandardRelationshipCards(nextLibrary, { signal }),
        parentId ? fetchOrderedEntityThumbnails([parentId], { signal }) : Promise.resolve([]),
      ]);

      signal.throwIfAborted();

      const parentThumb = parentThumbs.find((t) => t.kind === ENTITY_KIND.musicArtist);
      const resolvedArtist = parentThumb ? { id: parentThumb.id, title: parentThumb.title } : null;
      artistLink = resolvedArtist;

      childCards = thumbnailsToCards(children, {
        hrefFor: (thumbnail) => resolveEntityHref(ENTITY_KIND.audioLibrary, thumbnail.id),
      });
      relationshipStudio = relationships.studio;
      // An album is always scanned under its artist, so surface that music-artist as the lead
      // "Artist" card (its own thumbnail, linking to /artists/{id}). The credit list stays
      // unfiltered: it feeds the edit draft, and a hidden credit would be deleted on save.
      artistCards = parentThumb
        ? thumbnailsToCards([parentThumb], {
            hrefFor: (thumbnail) => resolveEntityHref(ENTITY_KIND.musicArtist, thumbnail.id),
          })
        : [];
      relationshipCredits = relationships.credits;
      relationshipTags = relationships.relationshipTags;

      const trackThumbs = trackGroup?.entities ?? [];
      trackCards = thumbnailsToCards(trackThumbs, {
        hrefFor: (thumbnail) => resolveEntityHref(ENTITY_KIND.audioTrack, thumbnail.id),
      });

      // Keep provider-backed missing tracks in the list. Their thumbnail projection already carries
      // wanted/source/acquisition state, so rows stay visibly non-playable without N+1 reads.
      trackItems = trackThumbs
        .map((thumb) => entityThumbnailToTrackItem(thumb, nextLibrary.id))
        .sort((a, b) => a.sortOrder - b.sortOrder);

      return nextLibrary;
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
      }, { kind: ENTITY_KIND.audioTrack });
    } catch (err) {
      trackItems = previousTrackItems;
      throw err;
    }
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
    const firstTrack = playableTrackItems[0];
    if (!firstTrack) return;
    playback.play(playableTrackItems, firstTrack.id, albumContext(), { shuffle: false });
  }

  function shuffleAll() {
    if (playableTrackItems.length === 0) return;
    playback.play(playableTrackItems, undefined, albumContext(), { shuffle: true });
  }

  function playTrack(trackId: string) {
    if (!playableTrackItems.some((track) => track.id === trackId)) return;
    // Re-clicking the current track toggles play/pause; otherwise (re)load the album from that track.
    if (playback.isCurrent(trackId)) playback.toggle();
    else playback.play(playableTrackItems, trackId, albumContext(), { shuffle: false });
  }
</script>

<svelte:head>
  <title>{library?.title ?? "Audio"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load audio library."
    onRetry={detail.retry}
    posterAspect="1 / 1"
  >
    {#if card && library}
    <EntityDetail
      {card}
      onRatingChange={detail.changeRating}
      onFavoriteToggle={detail.toggleFavorite}
      onOrganizedToggle={detail.toggleOrganized}
      onMetadataSave={detail.saveMetadata}
      ratingBusy={detail.ratingBusy}
      peopleLabel="Performers"
      defaultCreditRole={CREDIT_ROLE.artist}
      posterSize="large"
      actionButtons={heroActions}
      tabs={detailTabs}
      sections={detailSections}
    >
      {#snippet heroMeta()}
        {#if artistLink}
          <a href={resolveEntityHref(ENTITY_KIND.musicArtist, artistLink.id)} class="meta-item is-studio">{artistLink.title}</a>
        {/if}
        {#if studio}
          {#if artistLink}<span class="meta-sep"></span>{/if}
          <a href={resolveEntityHref(ENTITY_KIND.studio, studio.id)} class="meta-item is-studio">{studio.title}</a>
        {/if}
        <EntityDetailHeroDates {dates} leadingSeparator={Boolean(artistLink || studio)} />
        {#if trackItems.length > 0}
          {#if artistLink || studio || dates.length > 0}<span class="meta-sep"></span>{/if}
          <span class="meta-item">
            {playableTrackItems.length} of {trackItems.length} {trackItems.length === 1 ? "track" : "tracks"} present
          </span>
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
            onCancelled={() => void detail.reload({ showLoading: false })}
            onImported={() => detail.reload({ showLoading: false })}
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
  </EntityDetailPageState>
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c7c9cc); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }


  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

</style>
