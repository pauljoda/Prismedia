<script lang="ts">
  import { page } from "$app/state";
  import { Film } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchEntities, fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { RELATIONSHIP_CODE } from "$lib/api/generated/codes";
  import { getTagPolicyCapability } from "$lib/api/capabilities";
  import { entityCardToDetailCard, REFERENCE_STANDALONE_METADATA_SECTION_IDS, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import EntityDetail from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  let relatedCards = $state<EntityThumbnailCard[]>([]);

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => page.params.id ?? "",
    load: async ({ signal }) => {
      const id = page.params.id ?? "";
      const nextTag = await fetchEntity(id, { signal });
      try {
        const response = await fetchEntities(
          { referencedBy: id, relationshipCode: RELATIONSHIP_CODE.tags, limit: 1000 },
          { signal },
        );
        relatedCards = response.items.map((item) =>
          entityCardToThumbnailCard(item, resolveEntityHref(item.kind, item.id)),
        );
      } catch (error) {
        if (signal.aborted) throw error;
        relatedCards = [];
      }
      return nextTag;
    },
    breadcrumbs: (tag) => [
      { label: "Tags", href: "/tags" },
      { label: tag.title },
    ],
  });

  const tag = $derived(detail.entity);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!tag) return null;
    return entityCardToDetailCard(tag);
  });
  const tagPolicy = $derived(tag ? getTagPolicyCapability(tag.capabilities) : undefined);

</script>

<svelte:head>
  <title>{tag?.title ?? "Tag"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load tag."
    onRetry={detail.retry}
  >
    {#if card && tag}
      <EntityDetail
        {card}
        standaloneMetadataSectionIds={REFERENCE_STANDALONE_METADATA_SECTION_IDS}
        sections={[{ id: "tags", label: "Tags", editable: false }]}
        onRatingChange={detail.changeRating}
        onFavoriteToggle={detail.toggleFavorite}
        onOrganizedToggle={detail.toggleOrganized}
        onMetadataSave={detail.saveMetadata}
        ratingBusy={detail.ratingBusy}
        posterSize="large"
      >
        {#snippet heroMeta()}
          {#if relatedCards.length > 0}
            <span class="meta-item">{relatedCards.length} {relatedCards.length === 1 ? "item" : "items"}</span>
          {/if}
          {#if tagPolicy?.ignoreAutoTag}
            {#if relatedCards.length > 0}<span class="meta-sep"></span>{/if}
            <span class="meta-item is-muted">Auto-tag ignored</span>
          {/if}
        {/snippet}
      </EntityDetail>

      {#if relatedCards.length > 0}
        <EntityGridSection
          title="Tagged Content"
          count={relatedCards.length}
          icon={Film}
          prefsKey={`tag-${tag.id}-content-section`}
        >
          <EntityGrid
            cards={relatedCards}
            prefsKey={`tag-${tag.id}-content`}
            emptyTitle="No content"
            emptyMessage="No content tagged with this tag."
          />
        </EntityGridSection>
      {/if}
    {/if}
  </EntityDetailPageState>
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }
  :global(.meta-item.is-muted) { color: var(--color-text-muted, #8a93a6); opacity: 0.7; font-style: italic; }
  :global(.meta-sep) { display: inline-block; width: 3px; height: 3px; margin: 0 0.5rem; background: var(--color-text-muted, #8a93a6); opacity: 0.5; }

</style>
