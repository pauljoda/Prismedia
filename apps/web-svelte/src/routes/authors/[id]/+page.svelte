<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, CloudDownload, Info, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { CREDIT_ROLE, ENTITY_KIND } from "$lib/entities/entity-codes";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import {
    fetchOrderedEntityThumbnails,
    hydrateStandardRelationshipCards,
    thumbnailsToCards,
  } from "$lib/entities/entity-relationship-thumbnails";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
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
  let bookCards = $state<EntityThumbnailCard[]>([]);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);

  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => page.params.id ?? "",
    load: async ({ signal }) => {
      const nextAuthor = await fetchEntity(page.params.id ?? "", { signal });
      const bookGroup = nextAuthor.childrenByKind.find((group) => group.kind === ENTITY_KIND.book);
      const bookIds = bookGroup?.entities.map((entity) => entity.id) ?? [];
      const [books, relationships] = await Promise.all([
        fetchOrderedEntityThumbnails(bookIds),
        hydrateStandardRelationshipCards(nextAuthor),
      ]);
      signal.throwIfAborted();

      bookCards = thumbnailsToCards(books, {
        hrefFor: (thumbnail) => resolveEntityHref(ENTITY_KIND.book, thumbnail.id),
      });
      relationshipCredits = relationships.credits;
      relationshipTags = relationships.relationshipTags;
      return nextAuthor;
    },
    breadcrumbs: (author) => [
      { label: "Authors", href: "/authors" },
      { label: author.title },
    ],
  });

  const author = $derived(detail.entity);

  const card = $derived.by((): EntityDetailCardFull | null => {
    if (!author) return null;
    return {
      ...entityCardToDetailCard(author),
      tags: relationshipTags,
      credits: relationshipCredits,
    };
  });

  const identifyAction = useIdentifyDetailAction(() => author);
  const heroActions = $derived.by((): EntityDetailActionButton[] =>
    identifyAction.action ? [identifyAction.action] : []);

  // Monitoring lives in the Acquisition detail tab ("Check for new works" runs the discovery sync
  // now; the page reloads to show any new phantoms). It works for scanned-in and requested authors
  // alike; it needs a provider identity a plugin can track, which Identify supplies for on-disk
  // authors and a request commit supplies for wanted ones. The same tab owns the shared per-child
  // controls for books, so parent monitoring stays independent of medium-specific route code.
  const acq = useEntityAcquisition({
    entityId: () => author?.id,
    capabilities: () => author?.capabilities,
    childCards: () => requestableDirectChildCards(author?.id, bookCards),
    onChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto("/authors"),
  });
  const fileManagement = {
    onDeleted: () => goto("/authors"),
    onReverted: () => refreshAfterManagedFileRevert(acq, () => detail.reload({ showLoading: false })),
  };

  const detailSections = $derived.by((): EntityDetailSection[] => [
    { id: "credits", label: "People", icon: Users },
    { id: "acquisition" },
  ]);
  const detailTabs = $derived.by((): EntityDetailTab[] => [
    { id: "details", label: "Details", icon: Info, sections: ["description", "tags", "credits"] },
    { id: "metadata", label: "Metadata", icon: SlidersHorizontal, sections: ["stats", "dates", "classification", "links"], layout: "grid" },
    ...(acq.visible
      ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
      : []),
  ]);

</script>

<svelte:head>
  <title>{author?.title ?? "Author"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load author."
    onRetry={detail.retry}
    posterAspect="2 / 3"
  >
    {#if card && author}
    <EntityDetail
      {card}
      onRatingChange={detail.changeRating}
      onFavoriteToggle={detail.toggleFavorite}
      onOrganizedToggle={detail.toggleOrganized}
      onMetadataSave={detail.saveMetadata}
      ratingBusy={detail.ratingBusy}
      peopleLabel="People"
      defaultCreditRole={CREDIT_ROLE.writer}
      posterSize="large"
      actionButtons={heroActions}
      tabs={detailTabs}
      sections={detailSections}
    >
      {#snippet heroMeta()}
        {#if bookCards.length > 0}
          <span class="meta-item">{bookCards.length} {bookCards.length === 1 ? "book" : "books"}</span>
        {/if}
      {/snippet}

      {#snippet sectionContent(section)}
        {#if section.id === "acquisition"}
          <EntityAcquisitionCard
            {acq}
            entity={author}
            {fileManagement}
            onImported={() => detail.reload({ showLoading: false })}
          />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if bookCards.length > 0}
      <EntityGridSection
        title="Books"
        count={bookCards.length}
        icon={BookOpen}
        prefsKey={`author-${author.id}-books-section`}
      >
        <EntityGrid
          cards={bookCards}
          entityKind={ENTITY_KIND.book}
          prefsKey={`author-${author.id}-books`}
          emptyTitle="No books"
          emptyMessage="No books for this author."
        />
      </EntityGridSection>
    {:else}
      <div class="empty-children">
        <p>No books grouped under this author yet.</p>
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
