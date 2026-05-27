<script lang="ts">
  import { goto } from "$app/navigation";
  import {
    Loader2,
    Sparkles,
  } from "@lucide/svelte";
  import { cn } from "@prismedia/ui-svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { fetchIdentifyEntities } from "$lib/api/identify-client";
  import type { EntityCard } from "$lib/api/entities";
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

  let allEntities = $state<EntityCard[]>([]);
  let loading = $state(true);
  let selectedIds = $state<string[]>([]);
  let showAll = $state(false);
  let selectedProviderId = $state("");
  let loadedEntityKind: string | null = null;
  let loadRequestId = 0;

  const activeProviderId = $derived(selectedProviderId || kindProviders[0]?.id || "");
  const activeProvider = $derived(kindProviders.find((p) => p.id === activeProviderId) ?? null);

  const KindIcon = $derived(entityKindIcon(entityKind));
  const kindLabel = $derived(store.supportedKinds.find((k) => k.kind === entityKind)?.label ?? entityKind);

  const filteredEntities = $derived(
    showAll ? allEntities : allEntities.filter((e) => !e.isOrganized),
  );
  const organizedCount = $derived(allEntities.filter((e) => e.isOrganized).length);
  const unorganizedCount = $derived(allEntities.length - organizedCount);
  const cards = $derived(filteredEntities.map((e) => entityCardToThumbnailCard(e)));

  $effect(() => {
    const kind = entityKind;
    if (loadedEntityKind === kind) return;

    loadedEntityKind = kind;
    selectedIds = [];
    selectedProviderId = "";
    void loadEntities(kind);
  });

  async function loadEntities(kind: string) {
    const requestId = ++loadRequestId;
    loading = true;
    try {
      const response = await fetchIdentifyEntities(kind);
      if (requestId !== loadRequestId) return;
      allEntities = response.items;
    } catch (err) {
      if (requestId !== loadRequestId) return;
      store.error = err instanceof Error ? err.message : "Failed to load entities";
    } finally {
      if (requestId === loadRequestId) loading = false;
    }
  }

  function handleCardActivate(card: EntityThumbnailCard) {
    const entity = filteredEntities.find((e) => e.id === card.entity.id);
    if (!entity) return;
    void store.queueEntity(entity, activeProvider?.id).then(() => goto(`/identify/${entity.id}`));
  }

  async function handleBulkQueue() {
    if (!activeProvider || selectedIds.length === 0) return;
    const toQueue = filteredEntities.filter((e) => selectedIds.includes(e.id));
    if (toQueue.length === 0) return;
    await store.startBulk(activeProvider.id, toQueue);
  }
</script>

<div class="flex flex-col gap-4">
  <!-- Toolbar: filter toggle + provider selector + queue action -->
  <div class="flex flex-wrap items-center gap-2.5">
    <div class="flex items-center gap-1.5">
      <KindIcon class="h-4 w-4 text-text-accent" />
      <span class="font-heading text-[0.86rem] font-semibold text-text-primary">{kindLabel}</span>
      <span class="font-mono text-[0.7rem] text-text-muted">
        {showAll ? allEntities.length : unorganizedCount}
      </span>
    </div>

    <div class="flex items-center gap-1 rounded-xs border border-border-subtle bg-surface-2 p-0.5">
      <button
        type="button"
        class={cn(
          "rounded-xs px-2 py-1 text-[0.72rem] font-medium transition-colors",
          !showAll
            ? "bg-accent-950/40 text-text-accent"
            : "text-text-muted hover:text-text-primary",
        )}
        onclick={() => (showAll = false)}
      >
        Unorganized
      </button>
      <button
        type="button"
        class={cn(
          "rounded-xs px-2 py-1 text-[0.72rem] font-medium transition-colors",
          showAll
            ? "bg-accent-950/40 text-text-accent"
            : "text-text-muted hover:text-text-primary",
        )}
        onclick={() => (showAll = true)}
      >
        Show all
      </button>
    </div>

    {#if kindProviders.length > 1}
      <div class="flex items-center gap-1 rounded-xs border border-border-subtle bg-surface-2 p-0.5">
        {#each kindProviders as provider (provider.id)}
          <button
            type="button"
            class={cn(
              "rounded-xs px-2 py-1 font-mono text-[0.68rem] transition-colors",
              activeProviderId === provider.id
                ? "bg-accent-950/40 text-text-accent"
                : "text-text-muted hover:text-text-primary",
            )}
            onclick={() => (selectedProviderId = provider.id)}
          >
            {provider.name}
          </button>
        {/each}
      </div>
    {:else if activeProvider}
      <span class="rounded-xs border border-border-accent bg-accent-950/30 px-2 py-0.5 font-mono text-[0.66rem] text-text-accent">
        {activeProvider.name}
      </span>
    {/if}

    <div class="flex-1"></div>

    {#if selectedIds.length > 0 && activeProvider}
      <button
        type="button"
        class="inline-flex h-7 items-center gap-1.5 rounded-xs border border-border-accent-strong bg-accent-950/40 px-2.5 text-[0.72rem] font-medium text-text-accent transition-colors hover:bg-accent-950/60 disabled:cursor-not-allowed disabled:opacity-40"
        disabled={store.bulkStarting}
        onclick={handleBulkQueue}
      >
        {#if store.bulkStarting}
          <Loader2 class="h-3 w-3 animate-spin" />
        {:else}
          <Sparkles class="h-3 w-3" />
        {/if}
        Queue {selectedIds.length}
      </button>
    {/if}
  </div>

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
      emptyTitle={showAll ? `No ${kindLabel.toLowerCase()} in library` : `All ${kindLabel.toLowerCase()} organized`}
      emptyMessage={showAll
        ? `No ${kindLabel.toLowerCase()} found in your library.`
        : `All ${kindLabel.toLowerCase()} have been organized. Toggle "Show all" to see everything.`}
      onCardActivate={handleCardActivate}
      onSelectionChange={(ids) => (selectedIds = ids)}
      bulkActions={activeProvider
        ? [
            {
              id: "identify-bulk",
              label: `Identify with ${activeProvider.name}`,
              onRun: (ids) => store.startBulk(activeProvider.id, filteredEntities.filter((e) => ids.includes(e.id))),
            },
          ]
        : []}
    />
  {/if}
</div>
