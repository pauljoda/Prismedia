<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { CloudDownload, Disc3, Info, Play, Shuffle, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { assetUrl } from "$lib/api/orval-fetch";
  import { getCapability } from "$lib/api/capabilities";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { CAPABILITY_KIND, CREDIT_ROLE, ENTITY_KIND } from "$lib/entities/entity-codes";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import { collectLibraryTracks } from "$lib/entities/audio-track-collections";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useAudioPlayback, type PlaybackContext } from "$lib/stores/audio-playback.svelte";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  const playback = useAudioPlayback()!;

  let albumCards = $state<EntityThumbnailCard[]>([]);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);
  let queueBusy = $state(false);

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => page.params.id ?? "",
    load: async ({ signal }) => {
      const nextArtist = await fetchEntity(page.params.id ?? "", { signal });
      const albumGroup = nextArtist.childrenByKind.find(
        (group) => group.kind === ENTITY_KIND.audioLibrary,
      );
      const albumIds = albumGroup?.entities.map((entity) => entity.id) ?? [];
      const [albums, relationships] = await Promise.all([
        fetchOrderedEntityThumbnails(albumIds),
        hydrateStandardRelationshipCards(nextArtist),
      ]);
      signal.throwIfAborted();

      albumCards = thumbnailsToCards(albums, {
        hrefFor: (thumbnail) => resolveEntityHref(ENTITY_KIND.audioLibrary, thumbnail.id),
      });
      relationshipCredits = relationships.credits;
      relationshipTags = relationships.relationshipTags;
      return nextArtist;
    },
    breadcrumbs: (artist) => [
      { label: "Artists", href: "/artists" },
      { label: artist.title },
    ],
  });

  const artist = $derived(detail.entity);

  const artistCoverUrl = $derived.by(() => {
    if (!artist) return undefined;
    const images = getCapability(artist.capabilities, CAPABILITY_KIND.images);
    return assetUrl(images?.coverUrl ?? images?.thumbnailUrl) || undefined;
  });

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!artist) return null;
    return {
      ...entityCardToDetailCard(artist),
      tags: relationshipTags,
      credits: relationshipCredits,
    };
  });

  const identifyAction = useIdentifyDetailAction(() => artist);

  // Monitoring lives in the Acquisition detail tab ("Check for new works" runs the discovery sync
  // now; the page reloads to show any new phantoms). It works for scanned-in and requested artists
  // alike; it needs a provider identity a plugin can track. The same tab also owns the shared
  // per-child controls for albums, so parents do not need a medium-specific monitoring editor.
  const acq = useEntityAcquisition({
    entityId: () => artist?.id,
    capabilities: () => artist?.capabilities,
    childCards: () => requestableDirectChildCards(artist?.id, albumCards),
    onChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto("/artists"),
  });
  const fileManagement = {
    onDeleted: () => goto("/artists"),
    onReverted: () => refreshAfterManagedFileRevert(acq, () => detail.reload({ showLoading: false })),
  };

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
  // separate "Metadata" tab. Built-in sections come from EntityDetail's core catalog; only
  // the credits label override is declared here.
  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "credits", label: "Members", icon: Users },
    { id: "acquisition" },
  ]);
  const detailTabs = $derived.by((): EntityDetailTab[] => [
    { id: "details", label: "Details", icon: Info, sections: ["description", "tags", "credits"] },
    { id: "metadata", label: "Metadata", icon: SlidersHorizontal, sections: ["stats", "dates", "classification", "links"], layout: "grid" },
    ...(acq.visible
      ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
      : []),
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

  async function playArtist(shuffle: boolean) {
    if (queueBusy || albumCards.length === 0) return;
    queueBusy = true;
    try {
      const albumIds = albumCards.map((c) => c.entity.id);
      const tracks = (await Promise.all(albumIds.map((id) => collectLibraryTracks(id))))
        .flatMap((result) => result.tracks);
      if (tracks.length === 0) return;
      playback.play(tracks, shuffle ? undefined : tracks[0].id, artistContext(), { shuffle });
    } finally {
      queueBusy = false;
    }
  }

</script>

<svelte:head>
  <title>{artist?.title ?? "Artist"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load artist."
    onRetry={detail.retry}
    posterAspect="1 / 1"
  >
    {#if card && artist}
    <EntityDetail
      {card}
      onRatingChange={detail.changeRating}
      onFavoriteToggle={detail.toggleFavorite}
      onOrganizedToggle={detail.toggleOrganized}
      onMetadataSave={detail.saveMetadata}
      ratingBusy={detail.ratingBusy}
      peopleLabel="Members"
      defaultCreditRole={CREDIT_ROLE.artist}
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
        {#if section.id === "acquisition"}
          <EntityAcquisitionCard
            {acq}
            entity={artist}
            {fileManagement}
            onImported={() => detail.reload({ showLoading: false })}
          />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if albumCards.length > 0}
      <EntityGridSection
        title="Albums"
        count={albumCards.length}
        icon={Disc3}
        prefsKey={`artist-${artist.id}-albums-section`}
      >
        <EntityGrid
          cards={albumCards}
          prefsKey={`artist-${artist.id}-albums`}
          emptyTitle="No albums"
          emptyMessage="No albums for this artist."
        />
      </EntityGridSection>
    {:else}
      <div class="empty-children">
        <p>No albums grouped under this artist yet.</p>
      </div>
    {/if}
    {/if}
  </EntityDetailPageState>
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }

</style>
