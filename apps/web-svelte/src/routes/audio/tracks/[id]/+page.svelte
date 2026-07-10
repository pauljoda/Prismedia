<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { CloudDownload, Info, Play, SlidersHorizontal } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { fetchAudioTrack, type AudioTrackDetail } from "$lib/api/media";
  import {
    updateEntityFlags,
    updateEntityMetadata,
    updateEntityRating,
  } from "$lib/api/entity-mutations";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import EntityDetail, {
    type EntityDetailActionButton,
    type EntityDetailSection,
    type EntityDetailTab,
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { CREDIT_ROLE } from "$lib/entities/entity-codes";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { hydrateStandardRelationshipCards } from "$lib/entities/entity-relationship-thumbnails";
  import { audioTrackDetailToListItem } from "$lib/entities/audio-track-items";
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
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipStudio = $state<EntityDetailCredit | null>(null);
  let relationshipTags = $state<EntityDetailTag[]>([]);

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
  const coverUrl = $derived(card?.posterCard?.cover?.src ?? card?.poster?.src ?? null);

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
  const acq = useEntityAcquisition({
    entityId: () => track?.id,
    capabilities: () => track?.capabilities,
    onChanged: loadTrack,
    onPruned: () => goto("/audio"),
  });
  const fileManagement = {
    onDeleted: () => goto("/audio"),
    onReverted: () => refreshAfterManagedFileRevert(acq, loadTrack),
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
    if (!trackItem) return;
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
      relationshipCredits = relationships.credits;
      relationshipStudio = relationships.studio;
      relationshipTags = relationships.relationshipTags;

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
      defaultCreditRole={CREDIT_ROLE.artist}
      posterSize="large"
      actionButtons={heroActions}
      tabs={detailTabs}
      sections={detailSections}
    >
      {#snippet heroMeta()}
        {#if studio}
          <a href={resolveEntityHref("studio", studio.id)} class="meta-item is-studio">{studio.title}</a>
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
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }
  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-studio) { color: var(--color-text-accent, #c49a5a); text-decoration: none; transition: opacity 0.15s; }
  :global(.meta-item.is-studio:hover) { opacity: 0.8; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }
</style>
