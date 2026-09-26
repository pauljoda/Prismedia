<script lang="ts">
  import { Check, Loader2, Puzzle, ScanSearch, X } from "@lucide/svelte";
  import { Button, Checkbox, buttonVariants } from "@prismedia/ui-svelte";
  import { IDENTIFY_QUEUE_STATE } from "$lib/api/generated/codes";
  import type { PluginProvider } from "$lib/api/identify-types";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import { displayNameForEntityKind } from "$lib/entities/entity-codes";
  import { mediaFamilyForKind, mediaFamilyOrder, type MediaFamily } from "$lib/entities/media-families";
  import IdentifyFamilyCard, { type IdentifyFamilyKind } from "./IdentifyFamilyCard.svelte";
  import IdentifyQueueRow from "./IdentifyQueueRow.svelte";
  import { useIdentifyStore } from "./identify-store.svelte";

  interface Props {
    /** Files not organized yet, per identifiable kind; kinds still being counted are absent. */
    unidentified: Record<string, number>;
  }

  let { unidentified }: Props = $props();

  const store = useIdentifyStore();

  let selectedIds = $state<Set<string>>(new Set());

  const providerById = $derived(new Map(store.providers.map((provider) => [provider.id, provider])));
  const families = $derived.by(() => {
    const grouped = new Map<string, { family: MediaFamily; kinds: IdentifyFamilyKind[]; providers: PluginProvider[] }>();
    for (const info of store.supportedKinds) {
      const family = mediaFamilyForKind(info.kind);
      let entry = grouped.get(family.key);
      if (!entry) {
        entry = { family, kinds: [], providers: [] };
        grouped.set(family.key, entry);
      }
      entry.kinds.push({
        kind: info.kind,
        label: info.label,
        unidentified: info.kind in unidentified ? (unidentified[info.kind] ?? 0) : null,
        pending: info.pending,
      });
      for (const provider of store.providersForKind(info.kind)) {
        if (!entry.providers.some((candidate) => candidate.id === provider.id)) entry.providers.push(provider);
      }
    }
    // Two kinds that share a short label inside one family (book and comic volumes) use their full names.
    for (const entry of grouped.values()) {
      const shared = new Set(
        entry.kinds.map((kind) => kind.label).filter((label, index, labels) => labels.indexOf(label) !== index),
      );
      for (const kind of entry.kinds) {
        if (shared.has(kind.label)) kind.label = displayNameForEntityKind(kind.kind);
      }
    }
    return [...grouped.values()].sort((left, right) => mediaFamilyOrder(left.family) - mediaFamilyOrder(right.family));
  });

  const allSelected = $derived(store.queue.length > 0 && selectedIds.size === store.queue.length);
  const selectedItems = $derived(store.queue.filter((item) => selectedIds.has(item.entityId)));
  const acceptable = $derived(
    selectedItems.filter((item) => item.state === IDENTIFY_QUEUE_STATE.proposal && item.proposal),
  );

  function toggle(entityId: string) {
    const next = new Set(selectedIds);
    if (next.has(entityId)) next.delete(entityId);
    else next.add(entityId);
    selectedIds = next;
  }

  function toggleAll() {
    selectedIds = allSelected ? new Set() : new Set(store.queue.map((item) => item.entityId));
  }

  async function acceptSelected() {
    await store.acceptQueueProposals(acceptable);
    selectedIds = new Set();
  }

  function rejectSelected() {
    for (const id of selectedIds) void store.rejectQueueItem(id);
    selectedIds = new Set();
  }
</script>

<div class="flex min-w-0 flex-col gap-6">
  {#if store.queue.length > 0}
    <section class="flex flex-col gap-2" aria-labelledby="identify-queue">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="flex items-center gap-3">
          <Checkbox size="sm" checked={allSelected} aria-label="Select all queued items" onchange={toggleAll} />
          <h2 id="identify-queue" class="font-heading text-base font-semibold text-text-primary">
            Queue <span class="ml-1 font-mono text-caption font-normal text-text-muted">{store.queue.length}</span>
          </h2>
          {#if store.bulkAccepting}
            <span class="flex items-center gap-1.5 font-mono text-[0.7rem] text-text-muted">
              <Loader2 class="size-3 animate-spin motion-reduce:animate-none" aria-hidden="true" />
              {store.bulkAcceptDone}/{store.bulkAcceptTotal}
            </span>
          {/if}
        </div>
        {#if selectedIds.size > 0}
          <div class="flex items-center gap-1.5">
            {#if acceptable.length > 0}
              <Button variant="secondary" size="sm" disabled={store.bulkAccepting} onclick={() => void acceptSelected()}>
                <Check aria-hidden="true" />
                Accept {acceptable.length}
              </Button>
            {/if}
            <Button variant="ghost" size="sm" disabled={store.bulkAccepting} onclick={rejectSelected}>
              <X aria-hidden="true" />
              Reject {selectedIds.size}
            </Button>
          </div>
        {/if}
      </div>
      <ul
        class="flex flex-col divide-y divide-[var(--color-border-subtle)] overflow-hidden rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)]"
      >
        {#each store.queue as item (item.entityId)}
          <IdentifyQueueRow
            {item}
            provider={item.provider ? (providerById.get(item.provider) ?? null) : null}
            selected={selectedIds.has(item.entityId)}
            onToggle={() => toggle(item.entityId)}
            onReview={() => store.reviewQueueItem(item)}
          />
        {/each}
      </ul>
    </section>
  {/if}

  {#if families.length === 0}
    {#if store.queue.length === 0}
      <StatePlaceholder icon={ScanSearch} title="No identify providers">
        <a href="/plugins" class={buttonVariants({ variant: "secondary", size: "sm" })}>
          <Puzzle aria-hidden="true" />
          Plugins
        </a>
      </StatePlaceholder>
    {/if}
  {:else}
    <section class="flex flex-col gap-2" aria-labelledby="identify-families">
      <h2 id="identify-families" class="font-heading text-base font-semibold text-text-primary">
        Families <span class="ml-1 font-mono text-caption font-normal text-text-muted">{families.length}</span>
      </h2>
      <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {#each families as entry (entry.family.key)}
          <IdentifyFamilyCard
            family={entry.family}
            kinds={entry.kinds}
            providers={entry.providers}
            onOpenKind={(kind) => store.navigateToKind(kind)}
          />
        {/each}
      </div>
    </section>
  {/if}
</div>
