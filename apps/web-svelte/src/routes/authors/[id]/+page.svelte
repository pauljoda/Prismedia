<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { BookOpen, CloudDownload, Info, SlidersHorizontal, Users } from "@lucide/svelte";
  import EntityDetailSkeleton from "$lib/components/entities/EntityDetailSkeleton.svelte";
  import { fetchBookAuthor, type BookAuthorDetail } from "$lib/api/media";
  import {
    updateEntityRating,
    updateEntityFlags,
    updateEntityMetadata,
  } from "$lib/api/entity-mutations";
  import {
    toggleOptimisticEntityFlag,
    updateOptimisticEntityRating,
  } from "$lib/entities/entity-detail-state";
  import { refreshAfterManagedFileRevert } from "$lib/entities/entity-file-management";
  import { entityCardToDetailCard, type EntityDetailCardFull, type EntityDetailCredit, type EntityDetailTag } from "$lib/entities/entity-detail";
  import { CREDIT_ROLE } from "$lib/entities/entity-codes";
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
    type EntityMetadataUpdateRequest,
  } from "$lib/components/entities/EntityDetail.svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import EntityAcquisitionCard from "$lib/components/acquisitions/EntityAcquisitionCard.svelte";
  import { useEntityAcquisition } from "$lib/components/acquisitions/use-entity-acquisition.svelte";
  import { requestableDirectChildCards } from "$lib/requests/requestable-entity-children";
  import { useIdentifyDetailAction } from "$lib/components/identify/use-identify-detail-action.svelte";
  import { redirectHiddenEntityNotFound } from "$lib/nsfw/hidden-entity";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { useAppChrome } from "$lib/stores/app-chrome.svelte";

  type LoadState = "loading" | "ready" | "error";

  const nsfw = useNsfw();
  const appChrome = useAppChrome();

  let loadState: LoadState = $state("loading");
  let author = $state<BookAuthorDetail | null>(null);
  let errorMessage: string | null = $state(null);
  let lastNsfwMode = $state(nsfw.mode);
  let ratingBusy = $state(false);
  let bookCards = $state<EntityThumbnailCard[]>([]);
  let relationshipCredits = $state<EntityDetailCredit[]>([]);
  let relationshipTags = $state<EntityDetailTag[]>([]);

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
    onChanged: () => loadAuthor({ showLoading: false }),
    onPruned: () => goto("/authors"),
  });
  const fileManagement = {
    onDeleted: () => goto("/authors"),
    onReverted: () => refreshAfterManagedFileRevert(acq, () => loadAuthor({ showLoading: false })),
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

  onMount(() => {
    void loadAuthor();
  });

  $effect(() => {
    if (nsfw.mode === lastNsfwMode) return;
    lastNsfwMode = nsfw.mode;
    void loadAuthor();
  });

  $effect(() => {
    if (!author) return;
    return appChrome.setBreadcrumbs([
      { label: "Authors", href: "/authors" },
      { label: author.title },
    ]);
  });

  async function loadAuthor(options = { showLoading: true }) {
    if (options.showLoading) {
      loadState = "loading";
      errorMessage = null;
    }
    try {
      const nextAuthor = await fetchBookAuthor(page.params.id ?? "");

      const bookGroup = nextAuthor.childrenByKind.find((group) => group.kind === "book");
      const bookIds = bookGroup?.entities.map((entity) => entity.id) ?? [];

      const [books, relationships] = await Promise.all([
        fetchOrderedEntityThumbnails(bookIds),
        hydrateStandardRelationshipCards(nextAuthor),
      ]);

      author = nextAuthor;
      bookCards = thumbnailsToCards(books, {
        hrefFor: (thumbnail) => resolveEntityHref("book", thumbnail.id),
      });
      relationshipCredits = relationships.credits;
      relationshipTags = relationships.relationshipTags;

      loadState = "ready";
    } catch (err) {
      if (redirectHiddenEntityNotFound(err, nsfw.mode)) return;
      errorMessage = err instanceof Error ? err.message : String(err);
      if (options.showLoading) loadState = "error";
    }
  }

  async function handleRatingChange(value: number | null) {
    if (!author || ratingBusy) return;
    ratingBusy = true;
    try {
      await updateOptimisticEntityRating(author, value, (next) => (author = next), updateEntityRating);
    } finally {
      ratingBusy = false;
    }
  }

  async function handleFavoriteToggle() {
    if (!author) return;
    await toggleOptimisticEntityFlag(author, "isFavorite", (next) => (author = next), updateEntityFlags);
  }

  async function handleOrganizedToggle() {
    if (!author) return;
    await toggleOptimisticEntityFlag(author, "isOrganized", (next) => (author = next), updateEntityFlags);
  }

  async function handleMetadataSave(request: EntityMetadataUpdateRequest) {
    if (!author) return;
    await updateEntityMetadata(author.id, request, { kind: author.kind });
    await loadAuthor();
  }
</script>

<svelte:head>
  <title>{author?.title ?? "Author"} · Prismedia</title>
</svelte:head>

<div class="detail-page">
  {#if loadState === "loading"}
    <EntityDetailSkeleton posterAspect="2 / 3" />
  {:else if loadState === "error"}
    <div class="error-notice">
      <p>{errorMessage ?? "Failed to load author."}</p>
      <button type="button" onclick={() => void loadAuthor()}>Retry</button>
    </div>
  {:else if card && author}
    <EntityDetail
      {card}
      onRatingChange={handleRatingChange}
      onFavoriteToggle={handleFavoriteToggle}
      onOrganizedToggle={handleOrganizedToggle}
      onMetadataSave={handleMetadataSave}
      {ratingBusy}
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
            onImported={() => loadAuthor({ showLoading: false })}
          />
        {/if}
      {/snippet}
    </EntityDetail>

    {#if bookCards.length > 0}
      <section class="content-section">
        <h2 class="content-heading">
          <BookOpen class="h-4 w-4" />
          Books
          <span class="content-count">{bookCards.length}</span>
        </h2>
        <EntityGrid
          cards={bookCards}
          entityKind="book"
          prefsKey={`author-${author?.id}-books`}
          emptyTitle="No books"
          emptyMessage="No books for this author."
        />
      </section>
    {:else}
      <div class="empty-children">
        <p>No books grouped under this author yet.</p>
      </div>
    {/if}
  {/if}
</div>

<style>
  .detail-page { display: grid; gap: 1.25rem; padding: 0; max-width: none; margin: 0; }
  .error-notice { display: flex; align-items: center; justify-content: space-between; gap: 1rem; padding: 1rem; border: 1px solid color-mix(in srgb, #ef4444 50%, var(--color-border, #1c2235)); background: var(--color-surface-2, #101420); color: var(--color-text-muted, #8a93a6); font-size: 0.85rem; }
  .error-notice button { border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); color: var(--color-text-muted, #8a93a6); padding: 0.4rem 0.8rem; font-size: 0.78rem; cursor: pointer; }

  :global(.meta-item) { white-space: nowrap; font-size: 0.82rem; }

  .content-section { display: grid; gap: 0.75rem; }
  .content-heading { display: flex; align-items: center; gap: 0.5rem; margin: 0; font-family: var(--font-heading, Geist, sans-serif); font-size: 1.1rem; font-weight: 600; color: var(--color-text-primary, #f2eed8); }
  .content-count { font-family: var(--font-mono, "JetBrains Mono", monospace); font-size: 0.68rem; font-weight: 600; color: var(--color-text-muted, #8a93a6); padding: 0.1rem 0.4rem; border: 1px solid var(--color-border, #1c2235); background: var(--color-surface-3, #151a28); }

  .empty-children { padding: 2rem; border: 1px solid var(--color-border-subtle, #1c2235); background: var(--color-surface-1, #0c0f15); color: var(--color-text-muted, #8a93a6); text-align: center; font-size: 0.85rem; }
</style>
