<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, CloudDownload, Info, SlidersHorizontal } from "@lucide/svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import EntityDetail, {
    type EntityDetailSection,
    type EntityDetailTab,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityGridSection from "$lib/components/entities/EntityGridSection.svelte";
  import { orderedBookChildren, persistedPageCount } from "$lib/entities/book-entity-reader";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { thumbnailsToCards } from "$lib/entities/entity-relationship-thumbnails";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";

  let book = $state<EntityCardFull | null>(null);
  let chapterCards = $state<EntityThumbnailCard[]>([]);

  const bookId = $derived(page.params.id ?? "");
  const volumeId = $derived(page.params.volumeId ?? "");
  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => `${bookId}:${volumeId}`,
    load: async ({ signal }) => {
      const [nextBook, nextVolume] = await Promise.all([
        fetchEntity(bookId, { signal }),
        fetchEntity(volumeId, { signal }),
      ]);
      signal.throwIfAborted();
      book = nextBook;
      chapterCards = thumbnailsToCards(
        orderedBookChildren(nextVolume, ENTITY_KIND.bookChapter),
        { hrefFor: (chapter) => `/books/${nextBook.id}/chapters/${chapter.id}` },
      );
      return nextVolume;
    },
    breadcrumbs: (volume) => [
      { label: "Books", href: "/books" },
      { label: book?.title ?? "Book", href: `/books/${bookId}` },
      { label: volume.title },
    ],
  });

  const volume = $derived(detail.entity);
  const card = $derived(
    volume ? entityCardToDetailCard(volume) as EntityDetailCardFull : null,
  );
  const pageCount = $derived(persistedPageCount(volume));
  const acq = useEntityAcquisition({
    entityId: () => volume?.id,
    capabilities: () => volume?.capabilities,
    onChanged: () => detail.reload({ showLoading: false }),
    onPruned: () => goto(`/books/${bookId}`),
  });
  const fileManagement = {
    onDeleted: () => goto(`/books/${bookId}`),
    onReverted: () => refreshAfterManagedFileRevert(
      acq,
      () => detail.reload({ showLoading: false }),
    ),
  };
  const detailSections = $derived.by((): EntityDetailSection[] => [{ id: "acquisition" }]);
  const detailTabs = $derived.by((): EntityDetailTab[] => [
    {
      id: "details",
      label: "Details",
      icon: Info,
      sections: ["description", "stats", "positions", "source"],
    },
    {
      id: "metadata",
      label: "Metadata",
      icon: SlidersHorizontal,
      sections: ["dates", "links"],
      layout: "grid",
    },
    ...(acq.visible
      ? [{ id: "acquisition", label: "Acquisition", icon: CloudDownload, sections: ["acquisition"] }]
      : []),
  ]);
</script>

<svelte:head>
  <title>{volume?.title ?? "Volume"} · Prismedia</title>
</svelte:head>

<div class="volume-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load volume."
    onRetry={detail.retry}
    tabCount={3}
  >
    {#if card && volume && book}
      <EntityDetail
        {card}
        onRatingChange={detail.changeRating}
        onFavoriteToggle={detail.toggleFavorite}
        onOrganizedToggle={detail.toggleOrganized}
        onMetadataSave={detail.saveMetadata}
        ratingBusy={detail.ratingBusy}
        posterSize="large"
        tabs={detailTabs}
        sections={detailSections}
      >
        {#snippet heroMeta()}
          <span class="meta-item">{book?.title}</span>
          <span class="meta-sep"></span>
          <span class="meta-item">{chapterCards.length} chapter{chapterCards.length === 1 ? "" : "s"}</span>
          {#if pageCount > 0}
            <span class="meta-sep"></span>
            <span class="meta-item">{pageCount} pages</span>
          {/if}
        {/snippet}

        {#snippet sectionContent(section)}
          {#if section.id === "acquisition"}
            <EntityAcquisitionCard {acq} entity={volume} {fileManagement} />
          {/if}
        {/snippet}
      </EntityDetail>

      <EntityGridSection
        title="Chapters"
        count={chapterCards.length}
        icon={BookOpen}
        prefsKey={`book-${book.id}-volume-${volume.id}-chapters-section`}
      >
        <EntityGrid
          cards={chapterCards}
          prefsKey={`book-${book.id}-volume-${volume.id}-chapters`}
          initialSortBy="position"
          emptyTitle="No chapters"
          emptyMessage="No chapters found in this volume."
        />
      </EntityGridSection>
    {/if}
  </EntityDetailPageState>
</div>

<style>
  .volume-page {
    display: grid;
    gap: 1.25rem;
    padding: 0;
  }

  :global(.meta-item) {
    white-space: nowrap;
    font-size: 0.82rem;
  }

  :global(.meta-sep) {
    display: inline-block;
    width: 3px;
    height: 3px;
    margin: 0 0.5rem;
    border-radius: 50%;
    background: var(--color-text-muted, #8a93a6);
    opacity: 0.5;
  }
</style>
