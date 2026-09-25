<script lang="ts">
  import { untrack } from "svelte";
  import { Skeleton, cn } from "@prismedia/ui-svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { loadLibraryComposition, type LibraryFamily } from "$lib/components/concepts/library-composition";
  import ConceptBackLink from "$lib/components/concepts/dashboard/ConceptBackLink.svelte";
  import ContinueBillboard from "$lib/components/concepts/dashboard/ContinueBillboard.svelte";
  import FamilyTabShelf from "$lib/components/concepts/dashboard/FamilyTabShelf.svelte";
  import LatestMosaic from "$lib/components/concepts/dashboard/LatestMosaic.svelte";
  import PulseColumn from "$lib/components/concepts/dashboard/PulseColumn.svelte";
  import {
    continueShelfParams,
    loadLatestAcrossLibrary,
    loadLibraryPulse,
    loadWeekActivity,
    readShelfCards,
    type LatestItem,
    type LibraryPulse,
    type WeekActivity,
  } from "$lib/components/concepts/dashboard/dashboard-data";

  const nsfw = useNsfw();
  const hideNsfw = $derived(nsfw.mode === "off");

  let families = $state<LibraryFamily[]>([]);
  let continueCards = $state<EntityThumbnailCard[]>([]);
  let continueReady = $state(false);
  let week = $state<WeekActivity | null | undefined>(null);
  let pulse = $state<LibraryPulse | null>(null);
  let latest = $state<LatestItem[] | null>(null);

  const feature = $derived(continueCards[0] ?? null);
  const latestFamilyKind = $derived(latest?.[0]?.card.entity.kind ?? null);

  $effect(() => {
    const hide = hideNsfw;
    const controller = new AbortController();
    const live = () => !controller.signal.aborted;

    // Cached shelves apply synchronously, so the loads run untracked: only the NSFW mode reruns this.
    untrack(() => {
      void readShelfCards(continueShelfParams(hide), (cards) => {
        if (!live()) return;
        continueCards = cards;
        continueReady = true;
      });
      void loadLibraryComposition(hide, controller.signal).then((next) => {
        if (!live()) return;
        families = next;
        const kinds = next.filter((family) => family.count > 0).map((family) => family.kind);
        void loadLatestAcrossLibrary(kinds, hide, 8).then((items) => {
          if (live()) latest = items;
        });
      });
      void loadWeekActivity(hide, controller.signal)
        .then((next) => {
          if (live()) week = next;
        })
        .catch(() => {
          if (live()) week = undefined;
        });
      void loadLibraryPulse(hide, controller.signal).then((next) => {
        if (live()) pulse = next;
      });
    });

    return () => controller.abort();
  });
</script>

<svelte:head>
  <title>Beam &amp; pulse · Concepts · Prismedia</title>
</svelte:head>

<div class="-mx-3 flex flex-col gap-10 pb-16">
  <div class="flex flex-col gap-5 px-3">
    <ConceptBackLink title="Beam & pulse" />

    <div class={cn("grid gap-4 lg:items-stretch", (!continueReady || feature) && "lg:grid-cols-[minmax(0,1fr)_21rem]")}>
      {#if !continueReady}
        <Skeleton class="h-[clamp(20rem,42vh,27rem)] rounded-[var(--radius-xl)]" />
      {:else if feature}
        <ContinueBillboard card={feature} next={continueCards.slice(1, 9)} />
      {:else}
        <h1 class="sr-only">Dashboard</h1>
      {/if}

      <PulseColumn {families} {week} {pulse} />
    </div>
  </div>

  <!-- Waits for the merged timeline so the shelf opens on the family that received the newest item. -->
  {#if latest !== null}
    <FamilyTabShelf {families} {hideNsfw} initialKind={latestFamilyKind} />
  {:else}
    <div class="flex flex-col gap-4 px-3" aria-hidden="true">
      <Skeleton class="h-6 w-48" />
      <div class="flex gap-2">
        {#each Array(5) as _, index (index)}
          <Skeleton class="h-8 w-24 rounded-[var(--radius-xs)]" />
        {/each}
      </div>
    </div>
  {/if}

  {#if latest === null || latest.length > 0}
  <section aria-labelledby="latest-title" class="flex flex-col gap-4 px-3">
    <h2 id="latest-title" class="font-heading text-lg font-semibold text-text-primary">Latest</h2>
    {#if latest === null}
      <div class="grid grid-cols-3 gap-2 sm:grid-cols-6" aria-hidden="true">
        {#each Array(12) as _, index (index)}
          <Skeleton class="aspect-square rounded-[var(--radius-sm)]" />
        {/each}
      </div>
    {:else}
      <LatestMosaic items={latest} />
    {/if}
  </section>
  {/if}
</div>
