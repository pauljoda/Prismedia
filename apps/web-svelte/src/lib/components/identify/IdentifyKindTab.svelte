<script lang="ts">
  import {
    Loader2,
    Sparkles,
  } from "@lucide/svelte";
  import { Button, cn } from "@prismedia/ui-svelte";
  import EntityGrid from "$lib/components/entities/EntityGrid.svelte";
  import { entityCardToThumbnailCard } from "$lib/entities/entity-grid";
  import { fetchIdentifyEntities } from "$lib/api/identify-client";
  import type { EntityCard } from "$lib/api/entities";
  import IdentifyProviderSelect from "./IdentifyProviderSelect.svelte";
  import { useIdentifyStore } from "./identify-store.svelte";
  import { entityKindIcon } from "$lib/entities/entity-kind-icons";
  import { entityAccentForKind } from "$lib/entities/entity-accent";

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
  const kindAccent = $derived(entityAccentForKind(entityKind).primary);
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
      <KindIcon class="h-4 w-4" color={kindAccent} aria-hidden="true" />
      <span class="font-heading text-[0.86rem] font-semibold text-text-primary">{kindLabel}</span>
      <span class="font-mono text-[0.7rem] text-text-muted">
        {showAll ? allEntities.length : unorganizedCount}
      </span>
    </div>

    <div class="flex items-center gap-1 rounded-xs border border-border-subtle bg-surface-2 p-0.5">
      <Button variant="outline" size="sm"
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
      </Button>
      <Button variant="outline" size="sm"
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
      </Button>
    </div>

    {#if kindProviders.length > 0}
      <IdentifyProviderSelect
        providers={kindProviders}
        selectedId={activeProviderId}
        onChange={(providerId) => (selectedProviderId = providerId)}
        compact
      />
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
      emptyMessage=""
      onSelectionChange={(ids) => (selectedIds = ids)}
      initialSelectionActive
      cardLinks={false}
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

  <!-- The one thing to do here: identify what is selected. Floating so it is never scrolled away. -->
  {#if !loading && activeProvider && cards.length > 0}
    <div class="pointer-events-none sticky bottom-24 z-30 flex justify-center md:bottom-5">
      <div class="app-glass pointer-events-auto flex items-center gap-3 rounded-[var(--radius-lg)] border py-2 pr-2 pl-4">
        <span class="font-mono text-[0.72rem] text-text-muted">
          <span class={selectedIds.length > 0 ? "text-text-primary" : undefined}>{selectedIds.length}</span> selected
        </span>
        <Button
          variant="primary"
          disabled={selectedIds.length === 0 || store.bulkStarting}
          onclick={handleBulkQueue}
        >
          {#if store.bulkStarting}<Loader2 class="animate-spin" aria-hidden="true" />{:else}<Sparkles aria-hidden="true" />{/if}
          Identify {selectedIds.length > 0 ? selectedIds.length : ""} with {activeProvider.name}
        </Button>
      </div>
    </div>
  {/if}
</div>
