<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";
  import { Button } from "@prismedia/ui-svelte";
  import ComicReader from "$lib/components/ComicReader.svelte";
  import {
    CAPABILITY_KIND,
    CONSUMPTION_EVENT_KIND,
  } from "$lib/entities/entity-codes";
  import { PROGRESS_UNIT, READER_MODE } from "$lib/api/generated/codes";
  import { getCapability } from "$lib/api/capabilities";
  import { recordEntityConsumptionEvent, updateEntityProgress } from "$lib/api/consumption";
  import { fetchEntityReaderManifest, entityReaderPageUrl } from "$lib/api/entity-reader";
  import { fetchEntity, type EntityCardFull } from "$lib/api/entities";
  import type { EntityReaderManifestResponse } from "$lib/api/generated/model";
  import type { ImageListItemDto } from "$lib/entities/media-view-models";
  import { resolveOrderedEntitySequence } from "$lib/entities/ordered-entity-sequence";

  type LoadState = "loading" | "ready" | "error";
  type ComicReaderMode = typeof READER_MODE.paged | typeof READER_MODE.webtoon;

  let loadState: LoadState = $state("loading");
  let entity = $state.raw<EntityCardFull | null>(null);
  let manifest = $state.raw<EntityReaderManifestResponse | null>(null);
  let images = $state.raw<ImageListItemDto[]>([]);
  let initialIndex = $state(0);
  let readerMode = $state<ComicReaderMode>(READER_MODE.paged);
  let nextItem = $state.raw<{ id: string; title: string } | null>(null);
  let errorMessage = $state("This Entity does not expose readable pages.");
  let currentIndex = 0;
  let pendingReset = false;
  let progressSaveQueue: Promise<void> = Promise.resolve();

  const entityId = $derived(page.params.id ?? "");
  const returnHref = $derived(safeReturnHref(page.url.searchParams.get("returnTo")));
  const doublePageOrdinals = $derived(
    manifest?.pages.filter((readerPage) => readerPage.isDoublePage).map((readerPage) => Number(readerPage.ordinal)) ?? [],
  );

  $effect(() => {
    const id = entityId;
    const reset = page.url.searchParams.get("reset") === "1";
    const controller = new AbortController();
    void loadReader(id, reset, controller.signal);
    return () => controller.abort();
  });

  async function loadReader(id: string, reset: boolean, signal: AbortSignal) {
    loadState = "loading";
    errorMessage = "This Entity does not expose readable pages.";
    try {
      const [nextEntity, nextManifest] = await Promise.all([
        fetchEntity(id, { signal }),
        fetchEntityReaderManifest(id, { signal }),
      ]);
      signal.throwIfAborted();

      const pageSequence = getCapability(nextEntity.capabilities, CAPABILITY_KIND.pageSequence);
      if (!pageSequence || nextManifest.pages.length === 0) {
        throw new Error("This Entity does not expose readable pages.");
      }

      const progress = getCapability(nextEntity.capabilities, CAPABILITY_KIND.progress);
      const savedIndex = progress?.currentEntityId === nextEntity.id && !progress.completedAt
        ? Number(progress.index)
        : 0;
      const nextSequence = await resolveOrderedEntitySequence(nextEntity, { signal });
      signal.throwIfAborted();
      const itemIndex = nextSequence?.items.findIndex((item) => item.id === nextEntity.id) ?? -1;
      const following = itemIndex >= 0 ? nextSequence?.items[itemIndex + 1] : undefined;

      entity = nextEntity;
      manifest = nextManifest;
      images = nextManifest.pages.map((readerPage) => manifestPageImage(nextEntity, readerPage));
      currentIndex = reset ? 0 : clampPageIndex(savedIndex, nextManifest.pages.length);
      initialIndex = currentIndex;
      readerMode = comicReaderMode(progress?.mode ?? nextManifest.defaultMode);
      pendingReset = reset;
      nextItem = following ? { id: following.id, title: following.title } : null;
      loadState = "ready";

      void recordEntityConsumptionEvent(nextEntity.id, {
        kind: CONSUMPTION_EVENT_KIND.accessed,
        sessionId: crypto.randomUUID(),
      }).catch(() => undefined);
      if (reset) queueProgressSave(0, readerMode);
    } catch (error) {
      if (signal.aborted) return;
      errorMessage = error instanceof Error ? error.message : "Failed to open the reader.";
      loadState = "error";
    }
  }

  function manifestPageImage(
    owner: EntityCardFull,
    readerPage: EntityReaderManifestResponse["pages"][number],
  ): ImageListItemDto {
    const ordinal = Number(readerPage.ordinal);
    return {
      id: `${owner.id}:${ordinal}`,
      title: `Page ${ordinal + 1}`,
      date: null,
      rating: null,
      organized: false,
      isNsfw: false,
      width: readerPage.width == null ? null : Number(readerPage.width),
      height: readerPage.height == null ? null : Number(readerPage.height),
      format: readerPage.mimeType,
      isVideo: false,
      fileSize: null,
      thumbnailPath: null,
      previewPath: null,
      fullPath: entityReaderPageUrl(owner.id, ordinal),
      galleryId: null,
      sortOrder: ordinal,
      studioId: null,
      performers: [],
      tags: [],
      createdAt: "",
    };
  }

  function handleIndexChange(index: number) {
    currentIndex = index;
    queueProgressSave(index, readerMode);
  }

  function handleModeChange(mode: ComicReaderMode) {
    readerMode = mode;
    queueProgressSave(currentIndex, mode);
  }

  function queueProgressSave(index: number, mode: ComicReaderMode) {
    const activeEntity = entity;
    const pageCount = images.length;
    if (!activeEntity || pageCount === 0) return;
    const reset = pendingReset;
    pendingReset = false;
    progressSaveQueue = progressSaveQueue
      .catch(() => undefined)
      .then(() => updateEntityProgress(activeEntity.id, {
        currentEntityId: activeEntity.id,
        unit: PROGRESS_UNIT.page,
        index: clampPageIndex(index, pageCount),
        total: pageCount,
        mode,
        completed: index >= pageCount - 1 ? true : null,
        reset,
      }));
  }

  function openNextItem() {
    if (!nextItem) return;
    const target = `/entities/${nextItem.id}/reader?returnTo=${encodeURIComponent(returnHref)}`;
    return goto(target);
  }

  function closeReader() {
    return goto(returnHref);
  }

  function safeReturnHref(value: string | null): string {
    return value?.startsWith("/") && !value.startsWith("//") ? value : "/comics";
  }

  function clampPageIndex(index: number, pageCount: number): number {
    return Math.max(0, Math.min(Number.isFinite(index) ? index : 0, Math.max(0, pageCount - 1)));
  }

  function comicReaderMode(mode: string | null | undefined): ComicReaderMode {
    return mode === READER_MODE.webtoon ? READER_MODE.webtoon : READER_MODE.paged;
  }
</script>

<svelte:head>
  <title>{entity?.title ?? "Reader"} · Prismedia</title>
</svelte:head>

{#if loadState === "ready" && entity && manifest}
  <ComicReader
    {images}
    {initialIndex}
    initialMode={readerMode}
    readingDirection={manifest.direction}
    coverOrdinal={manifest.coverOrdinal == null ? null : Number(manifest.coverOrdinal)}
    {doublePageOrdinals}
    title={entity.title}
    nextChapterLabel={nextItem?.title ?? null}
    presentation="page"
    closeIcon="back"
    onClose={closeReader}
    onIndexChange={handleIndexChange}
    onModeChange={handleModeChange}
    onNextChapter={nextItem ? openNextItem : undefined}
  />
{:else if loadState === "error"}
  <main class="reader-state">
    <h1>Couldn’t open reader</h1>
    <p>{errorMessage}</p>
    <Button onclick={closeReader}>Back to comics</Button>
  </main>
{:else}
  <main class="reader-state" aria-busy="true">
    <p>Opening reader…</p>
  </main>
{/if}

<style>
  .reader-state {
    display: grid;
    min-height: 60vh;
    place-content: center;
    justify-items: center;
    gap: 0.75rem;
    padding: 2rem;
    text-align: center;
    color: var(--color-text-secondary);
  }

  .reader-state h1 {
    margin: 0;
    font-family: var(--font-heading, Geist, sans-serif);
    color: var(--color-text-primary);
  }

  .reader-state p {
    margin: 0;
  }

</style>
