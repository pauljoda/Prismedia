<script lang="ts">
  import { onMount } from "svelte";
  import { page } from "$app/state";
  import { Film } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import EntityDetailHeroDates from "$lib/components/entities/EntityDetailHeroDates.svelte";
  import { fetchEntities } from "$lib/api/entities";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import { getStudio } from "$lib/api/generated/prismedia";
  import type { StudioDetail } from "$lib/api/generated/model";
  import { unwrapGenerated } from "$lib/api/generated-response";
  import { getCapability } from "$lib/api/capabilities";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { entityCardToDetailCard, REFERENCE_STANDALONE_METADATA_SECTION_IDS, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail, {
    type EntityDetailActionButton,
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

  let loadState: LoadState = $state("loading");
  let studio = $state<StudioDetail | null>(null);
  let relatedCards = $state<EntityThumbnailCard[]>([]);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!studio) return null;
    return entityCardToDetailCard(studio);
  });

  const identifyAction = useIdentifyDetailAction(() => studio);
  const heroActions = $derived.by((): EntityDetailActionButton[] => identifyAction.action ? [identifyAction.action] : []);

  const dates = $derived(card?.dates ?? []);

  onMount(() => {
    void loadStudio();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadStudio();
  });

  $effect(() => {
    if (!studio) return;
    return appChrome.setBreadcrumbs([
      { label: "Studios", href: "/studios" },
      { label: studio.title },
    ]);
  });

  async function loadStudio() {
    loadState = "loading";
    errorMessage = null;
    try {
      const id = page.params.id ?? "";
      studio = unwrapGenerated<StudioDetail>(await getStudio(id), `Failed to fetch studio ${id}`);
      await loadRelated(id);
      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      loadState = "error";
    }
  }

  async function loadRelated(studioId: string) {
    try {
      const response = await fetchEntities({ referencedBy: studioId, relationshipCode: "studio", limit: 1000 });
      relatedCards = response.items.map((item) => entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id)));
    } catch {
      relatedCards = [];
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!studio || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(studio, value, (next) => (studio = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!studio) return;
    await toggleOptimisticEntityFlag(studio, "isFavorite", (next) => (studio = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!studio) return;
    await toggleOptimisticEntityFlag(studio, "isOrganized", (next) => (studio = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!studio) return;
    await updateEntityMetadata(studio.id, request, { kind: studio.kind });
    await loadStudio();
  }
</script>

<svelte:head>
  <title>{studio?.title ?? "Studio"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load studio."}</p>
      <button type="button" onclick={() => void loadStudio()}>Retry</button>
    </div>
  {:else if card && studio}
    <EntityDetail
      {card}
      standaloneMetadataSectionIds={REFERENCE_STANDALONE_METADATA_SECTION_IDS}
      sections={[{ id: "tags", label: "Tags", editable: false }]}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
      posterSize="large"
      actionButtons={heroActions}
    >
      {#snippet heroMeta()}
        <EntityDetailHeroDates {dates} />
        {#if relatedCards.length > 0}
          {#if dates.length > 0}<span class="meta-sep"></span>{/if}
          <span class="meta-item">{relatedCards.length} {relatedCards.length === 1 ? "title" : "titles"}</span>
        {/if}
      {/snippet}

    </EntityDetail>

    {#if relatedCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <Film class="h-4 w-4" />
          Content
          <span class="content-count">{relatedCards.length}</span>
        </h2>
        <EntityGrid
          cards={relatedCards}
          prefsKey={`studio-${studio?.id}-content`}
          emptyTitle="No content"
          emptyMessage="No content linked to this studio."
        />
      </section>
    {/if}
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

</style>
