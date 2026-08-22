<script lang="ts">
  import { page } from "$app/state";
  import { Info, SlidersHorizontal } from "@lucide/svelte";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import EntityDetail, { type EntityDetailTab } from "$lib/components/entities/EntityDetail.svelte";
  import EntityDetailPageState from "$lib/components/entities/EntityDetailPageState.svelte";
  import { useEntityDetailPage } from "$lib/components/entities/entity-detail-page-controller.svelte";
  import { persistedPageCount } from "$lib/entities/book-entity-reader";
  import { entityCardToDetailCard, type EntityDetailCardFull } from "$lib/entities/entity-detail";

  let book = $state<EntityCardFull | null>(null);

  const bookId = $derived(page.params.id ?? "");
  const chapterId = $derived(page.params.chapterId ?? "");
  const detail = useEntityDetailPage<EntityCardFull>({
    loadKey: () => `${bookId}:${chapterId}`,
    load: async ({ signal }) => {
      const [nextBook, nextChapter] = await Promise.all([
        fetchEntity(bookId, { signal }),
        fetchEntity(chapterId, { signal }),
      ]);
      signal.throwIfAborted();
      book = nextBook;
      return nextChapter;
    },
    breadcrumbs: (chapter) => [
      { label: "Books", href: "/books" },
      { label: book?.title ?? "Book", href: `/books/${bookId}` },
      { label: chapter.title },
    ],
  });

  const chapter = $derived(detail.entity);
  const card = $derived(
    chapter ? entityCardToDetailCard(chapter) as EntityDetailCardFull : null,
  );
  const pageCount = $derived(persistedPageCount(chapter));
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
  ]);
</script>

<svelte:head>
  <title>{chapter?.title ?? "Chapter"} · Prismedia</title>
</svelte:head>

<div class="chapter-page">
  <EntityDetailPageState
    loadState={detail.loadState}
    errorMessage={detail.errorMessage}
    fallbackError="Failed to load chapter."
    onRetry={detail.retry}
    tabCount={2}
  >
    {#if card && chapter && book}
      <EntityDetail
        {card}
        onRatingChange={detail.changeRating}
        onFavoriteToggle={detail.toggleFavorite}
        onOrganizedToggle={detail.toggleOrganized}
        onMetadataSave={detail.saveMetadata}
        ratingBusy={detail.ratingBusy}
        posterSize="large"
        tabs={detailTabs}
      >
        {#snippet heroMeta()}
          <span class="meta-item">{book?.title}</span>
          {#if pageCount > 0}
            <span class="meta-sep"></span>
            <span class="meta-item">{pageCount} page{pageCount === 1 ? "" : "s"}</span>
          {/if}
        {/snippet}
      </EntityDetail>
    {/if}
  </EntityDetailPageState>
</div>

<style>
  .chapter-page {
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
