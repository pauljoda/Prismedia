<script lang="ts">
  import { untrack } from "svelte";
  import { PlayCircle } from "@lucide/svelte";
  import { Skeleton } from "@prismedia/ui-svelte";
  import EntityShelf from "$lib/components/entities/EntityShelf.svelte";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useNsfw } from "$lib/nsfw/store.svelte";
  import { loadLibraryComposition, type LibraryFamily } from "$lib/components/concepts/library-composition";
  import ConceptBackLink from "$lib/components/concepts/dashboard/ConceptBackLink.svelte";
  import FamilyRailShelf from "$lib/components/concepts/dashboard/FamilyRailShelf.svelte";
  import LibraryPrismHero from "$lib/components/concepts/dashboard/LibraryPrismHero.svelte";
  import NeedsYouStrip from "$lib/components/concepts/dashboard/NeedsYouStrip.svelte";
  import {
    continueShelfParams,
    loadLibraryPulse,
    newestShelfParams,
    readShelfCards,
    type LibraryPulse,
  } from "$lib/components/concepts/dashboard/dashboard-data";

  const nsfw = useNsfw();

  let families = $state<LibraryFamily[]>([]);
  let continueCards = $state<EntityThumbnailCard[]>([]);
  let continueReady = $state(false);
  let pulse = $state<LibraryPulse | null>(null);
  let focusKind = $state<string | null>(null);
  let focusCards = $state<EntityThumbnailCard[]>([]);
  let focusReady = $state(false);

  const focusFamily = $derived(families.find((family) => family.kind === focusKind) ?? null);

  // One load per NSFW mode: composition, the Continue rail, and live work all refetch together.
  $effect(() => {
    const hideNsfw = nsfw.mode === "off";
    const controller = new AbortController();
    const live = () => !controller.signal.aborted;

    // Cached shelves apply synchronously, so the loads run untracked: only the NSFW mode reruns this.
    untrack(() => {
      void loadLibraryComposition(hideNsfw, controller.signal).then((next) => {
        if (live()) families = next;
      });
      void readShelfCards(continueShelfParams(hideNsfw), (cards) => {
        if (!live()) return;
        continueCards = cards;
        continueReady = true;
      });
      void loadLibraryPulse(hideNsfw, controller.signal).then((next) => {
        if (live()) pulse = next;
      });
      if (focusKind) loadFocus(focusKind, hideNsfw, live);
    });

    return () => controller.abort();
  });

  function loadFocus(kind: string, hideNsfw: boolean, live: () => boolean = () => true) {
    focusReady = false;
    void readShelfCards(newestShelfParams(kind, hideNsfw), (cards) => {
      if (!live() || focusKind !== kind) return;
      focusCards = cards;
      focusReady = true;
    });
  }

  function selectFamily(kind: string | null) {
    focusKind = kind;
    focusCards = [];
    if (kind) loadFocus(kind, nsfw.mode === "off");
  }
</script>

<svelte:head>
  <title>Spectrum home · Concepts · Prismedia</title>
</svelte:head>

<div class="-mx-3 flex flex-col gap-8 pb-16">
  <div class="flex flex-col gap-5 px-3">
    <ConceptBackLink title="Spectrum home" />
    <h1 class="sr-only">Dashboard</h1>

    <LibraryPrismHero {families} activeKind={focusKind} onSelect={selectFamily}>
      {#snippet focus()}
        {#if focusFamily}
          <div class="-mx-3 border-t border-[var(--color-border-subtle)] pt-4">
            {#if focusReady && focusCards.length > 0}
              <EntityShelf
                label="Newest {focusFamily.label.toLowerCase()}"
                icon={focusFamily.icon}
                cards={focusCards}
                href={focusFamily.href}
              />
            {:else if focusReady}
              <p class="px-3 text-sm text-text-muted">No {focusFamily.label.toLowerCase()}</p>
            {:else}
              <div class="flex gap-3 overflow-hidden px-3 pb-3" aria-hidden="true">
                {#each Array(6) as _, index (index)}
                  <Skeleton class="h-44 w-36 flex-none rounded-[var(--radius-sm)]" />
                {/each}
              </div>
            {/if}
          </div>
        {/if}
      {/snippet}
    </LibraryPrismHero>

    {#if pulse}
      <NeedsYouStrip {pulse} />
    {/if}
  </div>

  {#if !continueReady}
    <div class="flex gap-3 overflow-hidden px-3" aria-hidden="true">
      {#each Array(6) as _, index (index)}
        <Skeleton class="h-[clamp(150px,16vw,200px)] w-40 flex-none rounded-[var(--radius-sm)]" />
      {/each}
    </div>
  {:else if continueCards.length > 0}
    <FamilyRailShelf label="Continue" icon={PlayCircle} cards={continueCards} />
  {/if}
</div>
