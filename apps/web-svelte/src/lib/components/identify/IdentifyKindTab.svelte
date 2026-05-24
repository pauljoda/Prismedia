<script lang="ts">
  import { onMount } from "svelte";
  import { goto } from "$app/navigation";
  import {
    ChevronLeft,
    ChevronDown,
    Loader2,
    Sparkles,
    Zap,
  } from "@lucide/svelte";
  import { cn, StatusLed } from "@prismedia/ui-svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { fetchIdentifyEntities } from "$lib/api/identify";
  import type { EntityCard } from "$lib/api/prismedia";
  import type { EntityThumbnailCard } from "$lib/entities/entity-thumbnail";
  import { useIdentifyStore } from "./identify-store.svelte";
  import { entityKindIcon } from "./identify-icons";

  interface Props {
    entityKind: string;
  }

  let { entityKind }: Props = $props();

  const store = useIdentifyStore();
  const kindProviders = $derived(store.providersForKind(entityKind));
  const defaultProvider = $derived(kindProviders[0] ?? null);

  let entities = $state<EntityCard[]>([]);
  let cards = $state<EntityThumbnailCard[]>([]);
  let loading = $state(true);
  let selectedIds = $state<string[]>([]);

  const KindIcon = $derived(entityKindIcon(entityKind));
  const kindLabel = $derived(store.supportedKinds.find((k) => k.kind === entityKind)?.label ?? entityKind);

  onMount(() => {
    void loadEntities();
  });

  async function loadEntities() {
    loading = true;
    try {
      const response = await fetchIdentifyEntities(entityKind);
      entities = response.items;
      cards = entities.map((e) => entityCardToThumbnailCard(e));
    } catch (err) {
      store.error = err instanceof Error ? err.message : "Failed to load entities";
    } finally {
      loading = false;
    }
  }

  function handleCardActivate(card: EntityThumbnailCard) {
    const entity = entities.find((e) => e.id === card.entity.id);
    if (!entity) return;
    void store.queueEntity(entity).then(() => goto(`/identify/${entity.id}`));
  }

  async function handleBulkQueue() {
    if (!defaultProvider) return;
    const toQueue = selectedIds.length > 0
      ? entities.filter((e) => selectedIds.includes(e.id))
      : entities;
    if (toQueue.length === 0) return;
    await store.startBulk(defaultProvider.id, toQueue);
  }
</script>

<div class="flex flex-col gap-4">
  <!-- Back + Kind hero -->
  <div class="flex items-center gap-3">
    <button
      type="button"
      class="inline-flex h-8 items-center gap-1.5 rounded-xs border border-border-default bg-surface-2 px-2.5 text-[0.78rem] text-text-muted transition-colors hover:bg-surface-3 hover:text-text-primary"
      onclick={() => store.navigateToDashboard()}
    >
      <ChevronLeft class="h-3.5 w-3.5" />
      Dashboard
    </button>
  </div>

  <!-- Kind hero panel -->
  <div
    class="grid grid-cols-[auto_1fr_auto] items-center gap-4 rounded-md border border-border-accent bg-gradient-to-br from-accent-950/40 to-transparent p-4"
    style="box-shadow: var(--shadow-glow-accent);"
  >
    <div class="grid h-12 w-12 place-items-center rounded-xs border border-border-accent bg-accent-950/50 text-text-accent-bright">
      <KindIcon class="h-6 w-6" />
    </div>
    <div>
      <span class="text-kicker text-text-accent">Identify scope</span>
      <h2 class="mt-1">{kindLabel}</h2>
      <div class="mt-1.5 flex flex-wrap gap-x-4 gap-y-1 text-[0.78rem] text-text-muted">
        <span><span class="font-mono text-text-secondary">{entities.length}</span> in library</span>
        <span><span class="font-mono text-text-accent">{entities.length}</span> unidentified</span>
      </div>
    </div>
    {#if defaultProvider}
      <div class="flex flex-col items-end gap-1.5">
        <span class="text-kicker">Default provider</span>
        <div class="flex items-center gap-2">
          <StatusLed status="accent" pulse />
          <span class="font-heading text-[0.82rem] font-semibold text-text-accent-bright">
            {defaultProvider.name}
          </span>
          <ChevronDown class="h-3.5 w-3.5 text-text-muted" />
        </div>
      </div>
    {/if}
  </div>

  <!-- Bulk queue panel -->
  {#if defaultProvider && entities.length > 0}
    <div class="surface-panel overflow-hidden">
      <header class="flex items-center gap-2.5 border-b border-border-subtle bg-surface-2 px-3.5 py-2.5">
        <Zap class="h-3.5 w-3.5 text-text-accent" />
        <span class="text-kicker text-text-accent">Queue all</span>
        <span class="font-mono text-[0.7rem] text-text-muted">
          {selectedIds.length > 0 ? `${selectedIds.length} selected` : `${entities.length} unidentified`} will be queued
        </span>
        <div class="flex-1"></div>
        <button
          type="button"
          class="inline-flex h-7 items-center gap-1.5 rounded-xs border border-border-accent-strong bg-accent-950/40 px-2.5 text-[0.72rem] font-medium text-text-accent transition-colors hover:bg-accent-950/60 disabled:cursor-not-allowed disabled:opacity-40"
          disabled={store.bulkStarting || (!selectedIds.length && !entities.length)}
          onclick={handleBulkQueue}
        >
          {#if store.bulkStarting}
            <Loader2 class="h-3 w-3 animate-spin" />
          {:else}
            <Sparkles class="h-3 w-3" />
          {/if}
          Queue {selectedIds.length || entities.length} with {defaultProvider.name}
        </button>
      </header>
      <div class="flex items-center gap-3 px-3.5 py-2.5 text-[0.78rem]">
        <span class="font-mono text-[0.7rem] text-text-muted">with provider</span>
        <span class="rounded-xs border border-border-accent bg-accent-950/30 px-2 py-0.5 font-mono text-[0.66rem] text-text-accent">
          {defaultProvider.name}
        </span>
      </div>
    </div>
  {/if}

  <!-- Entity grid -->
  {#if loading}
    <div class="flex items-center justify-center py-12">
      <Loader2 class="h-5 w-5 animate-spin text-text-accent" />
    </div>
  {:else}
    <EntityGrid
      {cards}
      selectable
      prefsKey="identify-{entityKind}"
      emptyTitle="No unidentified {kindLabel.toLowerCase()}"
      emptyMessage="All {kindLabel.toLowerCase()} in your library have been identified."
      onCardActivate={handleCardActivate}
      onSelectionChange={(ids) => (selectedIds = ids)}
      bulkActions={defaultProvider
        ? [
            {
              id: "identify-bulk",
              label: `Identify with ${defaultProvider.name}`,
              onRun: (ids) => store.startBulk(defaultProvider.id, entities.filter((e) => ids.includes(e.id))),
            },
          ]
        : []}
    />
  {/if}
</div>
