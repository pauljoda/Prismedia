<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { CloudDownload, Info, Play, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchAudioTrack, type AudioTrackDetail } from "$lib/api/media";
  import { getCapability, isWanted } from "$lib/api/capabilities";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { CREDIT_ROLE, ENTITY_KIND } from "$lib/entities/entity-codes";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { hydrateStandardRelationshipCards } from "$lib/entities/entity-relationship-thumbnails";
  import { audioTrackDetailToListItem } from "$lib/entities/audio-track-items";
  import { useAudioPlayback } from "$lib/stores/audio-playback.svelte";

  const playback = useAudioPlayback()!;

  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);

  const detail = useEntityDetailPage<AudioTrackDetail>({
    loadKey: () => page.params.id ?? "",
    load: async ({ signal }) => {
      const nextTrack = await fetchAudioTrack(page.params.id ?? "", { signal });
      const relationships = await hydrateStandardRelationshipCards(nextTrack, { signal });
      signal.throwIfAborted();

      relationshipCredits = relationships.credits;
      relationshipStudio = relationships.studio;
      relationshipTags = relationships.relationshipTags;
      return nextTrack;
    },
    breadcrumbs: (nextTrack) => [
      { label: "Audio", href: "/audio" },
      { label: nextTrack.title },
    ],
  });

  const track = $derived(detail.entity);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!track) return null;
    return {
      ...entityCardToDetailCard(track),
      tags: relationshipTags,
      credits: relationshipCredits,
      studio: relationshipStudio,
    };
  });

  const studio = $derived(relationshipStudio);

  const dates = $derived(card?.dates ?? []);

  const trackItem = $derived(track ? audioTrackDetailToListItem(track) : null);
  const wanted = $derived(track ? isWanted(track.capabilities) : false);
  const coverUrl = $derived(card?.posterCard?.cover?.src ?? card?.poster?.src ?? null);

  const heroActions = $derived.by((): EntityDetailActionButton[] => {
    if (!trackItem || wanted) return [];
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
  const acq = useEntityAcquisition({
    entityId: () => track?.id,
    capabilities: () => track?.capabilities,
    onChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto("/audio"),
  });
  const fileManagement = {
    onDeleted: () => goto("/audio"),
    onReverted: () => refreshAfterManagedFileRevert(acq, () => detail.reload({ showLoading: false })),
  };
  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "acquisition" },
  ]);
  const detailTabs = $derived.by((): EntityDetailTab[] => [
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
      sections: ["stats", "dates", "classification", "technical", "source", "links"],
      layout: "grid",
    },
    ...(acq.visible
      ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
      : []),
  ]);

  function playTrack() {
    if (!trackItem || wanted) return;
    if (playback.isCurrent(trackItem.id)) {
      playback.toggle();
      return;
    }
    playback.play([trackItem], trackItem.id, {
      albumTitle: trackItem.embeddedAlbum,
      artistName: trackItem.embeddedArtist,
      coverUrl,
    });
  }

</script>

<svelte:head>
  <title>{track?.title ?? "Audio Track"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load audio track."
    onRetry={detail.retry}
  >
  {#if card && track && trackItem}
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
        {#if studio}
          <a href={resolveEntityHref(ENTITY_KIND.studio, studio.id)} class="meta-item is-studio">{studio.title}</a>
        {/if}
        <EntityDetailHeroDates {dates} leadingSeparator={Boolean(studio)} />
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "acquisition"}
          <EntityAcquisitionCard {acq} entity={track} {fileManagement} />
        {/if}
      {/snippet}
    </EntityDetail>
  {/if}
  </EntityDetailPageState>
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c7c9cc); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }
</style>
