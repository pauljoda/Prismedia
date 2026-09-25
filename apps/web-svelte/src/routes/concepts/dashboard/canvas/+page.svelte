<script lang="ts">
  import { untrack } from "svelte";
  import { Skeleton } from "@prismedia/ui-svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { loadLibraryComposition, type LibraryFamily } from "$lib/components/concepts/library-composition";
  import ConceptBackLink from "$lib/components/concepts/dashboard/ConceptBackLink.svelte";
  import LibraryFigures from "$lib/components/concepts/dashboard/LibraryFigures.svelte";
  import LightRailContinue from "$lib/components/concepts/dashboard/LightRailContinue.svelte";
  import RecentMasonry from "$lib/components/concepts/dashboard/RecentMasonry.svelte";
  import {
    continueShelfParams,
    loadLatestAcrossLibrary,
    readShelfCards,
    type LatestItem,
  } from "$lib/components/concepts/dashboard/dashboard-data";

  /** Newest items read per family before merging into one timeline. */
  const RECENT_PER_FAMILY = 16;

  const nsfw = useNsfw();

  let families = $state<LibraryFamily[]>([]);
  let continueCards = $state<EntityThumbnailCard[]>([]);
  let continueReady = $state(false);
  let recent = $state<LatestItem[] | null>(null);

  $effect(() => {
    const hideNsfw = nsfw.mode === "off";
    const controller = new AbortController();
    const live = () => !controller.signal.aborted;

    // Cached shelves apply synchronously, so the loads run untracked: only the NSFW mode reruns this.
    untrack(() => {
      void loadLibraryComposition(hideNsfw, controller.signal).then((next) => {
        if (!live()) return;
        families = next;
        const kinds = next.filter((family) => family.count > 0).map((family) => family.kind);
        void loadLatestAcrossLibrary(kinds, hideNsfw, RECENT_PER_FAMILY).then((items) => {
          if (live()) recent = items;
        });
      });
      void readShelfCards(continueShelfParams(hideNsfw), (cards) => {
        if (!live()) return;
        continueCards = cards;
        continueReady = true;
      });
    });

    return () => controller.abort();
  });
</script>

<svelte:head>
  <title>Quiet canvas · Concepts · Prismedia</title>
</svelte:head>

<div class="-mx-3 flex flex-col gap-14 pb-20 sm:gap-16">
  <div class="flex flex-col gap-4 px-3">
    <ConceptBackLink title="Quiet canvas" />
    <h1 class="sr-only">Dashboard</h1>
    <LibraryFigures {families} />
  </div>

  {#if !continueReady}
    <div class="flex gap-5 overflow-hidden px-3" aria-hidden="true">
      {#each Array(5) as _, index (index)}
        <Skeleton class="h-[clamp(13rem,26vw,20rem)] w-[clamp(9rem,18vw,14rem)] flex-none rounded-[var(--radius-sm)]" />
      {/each}
    </div>
  {:else if continueCards.length > 0}
    <LightRailContinue cards={continueCards} />
  {/if}

  {#if recent === null || recent.length > 0}
    <section aria-labelledby="canvas-recent-title" class="flex flex-col gap-5 px-3">
      <h2 id="canvas-recent-title" class="font-mono text-[0.68rem] uppercase tracking-[0.2em] text-text-muted">Recent</h2>
      {#if recent === null}
        <div class="grid grid-cols-2 gap-4 sm:grid-cols-4 lg:grid-cols-6" aria-hidden="true">
          {#each Array(12) as _, index (index)}
            <Skeleton class="rounded-[var(--radius-sm)] {index % 3 === 0 ? 'aspect-[2/3]' : 'aspect-square'}" />
          {/each}
        </div>
      {:else}
        <RecentMasonry items={recent} />
      {/if}
    </section>
  {/if}
</div>
