<script lang="ts">
  import { onMount, tick } from "svelte";
  import { Check, ScanSearch, Sparkles, X } from "@lucide/svelte";
  import { Button } from "@prismedia/ui-svelte";
  import { IDENTIFY_QUEUE_STATE } from "$lib/api/generated/codes";
  import type { PluginProvider } from "$lib/api/identify-types";
  import { fetchEntities } from "$lib/api/entities";
  import BackLink from "$lib/components/BackLink.svelte";
  import StatePlaceholder from "$lib/components/StatePlaceholder.svelte";
  import IdentifyFamilyCard, {
    type IdentifyFamilyKind,
  } from "$lib/components/concepts/identify/IdentifyFamilyCard.svelte";
  import IdentifyQueueRow from "$lib/components/concepts/identify/IdentifyQueueRow.svelte";
  import IdentifyKindTab from "$lib/components/identify/IdentifyKindTab.svelte";
  import { useIdentifyStore } from "$lib/components/identify/identify-store.svelte";
  import ManagePageHeader from "$lib/components/manage/ManagePageHeader.svelte";
  import { displayNameForEntityKind } from "$lib/entities/entity-codes";
  import { mediaFamilyForKind, mediaFamilyOrder, type MediaFamily } from "$lib/entities/media-families";
  import { useNsfw } from "$lib/nsfw/store.svelte";

  const store = useIdentifyStore();
  const nsfw = useNsfw();

  let unidentified = $state<Record<string, number>>({});
  let selectedIds = $state<Set<string>>(new Set());
  let selectedKind = $state<string | null>(null);
  let kindPanel = $state<HTMLElement | null>(null);
  let countedFor = "";

  onMount(() => {
    void store.enterDashboardRoute();
  });

  /** Counts files that are not organized yet, one cheap `limit: 1` request per identifiable kind. */
  $effect(() => {
    const kinds = store.supportedKinds.map((entry) => entry.kind);
    const hideNsfw = nsfw.mode === "off";
    const key = `${hideNsfw}:${kinds.join(",")}`;
    if (kinds.length === 0 || key === countedFor) return;
    countedFor = key;
    for (const kind of kinds) {
      fetchEntities({ kind, organized: false, hasFile: true, wanted: false, limit: 1, hideNsfw }).then(
        (response) => (unidentified = { ...unidentified, [kind]: Number(response.totalCount) || 0 }),
        () => undefined,
      );
    }
  });

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
  const totalUnidentified = $derived(Object.values(unidentified).reduce((sum, count) => sum + count, 0));

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

  async function acceptSelected() {
    await store.acceptQueueProposals(acceptable);
    selectedIds = new Set();
  }

  function rejectSelected() {
    for (const id of selectedIds) void store.rejectQueueItem(id);
    selectedIds = new Set();
  }

  async function openKind(kind: string) {
    selectedKind = selectedKind === kind ? null : kind;
    await tick();
    if (selectedKind) kindPanel?.scrollIntoView({ behavior: "smooth", block: "start" });
  }
</script>

<svelte:head><title>Identify by family · Concepts · Prismedia</title></svelte:head>

<div class="mx-auto flex w-full max-w-7xl min-w-0 flex-col gap-6 pb-16">
  <BackLink fallback="/concepts" label="Concepts" variant="text" />

  <ManagePageHeader icon={ScanSearch} title="Identify">
    {#snippet status()}
      <span class="flex flex-wrap gap-x-3 font-mono text-[0.72rem] text-text-muted">
        <span class={store.reviewableCount > 0 ? "text-text-primary" : undefined}>{store.reviewableCount} to review</span>
        {#if store.queuedCount > 0}<span>{store.queuedCount} queued</span>{/if}
        {#if store.searchingCount > 0}<span>{store.searchingCount} searching</span>{/if}
        <span>{new Intl.NumberFormat().format(totalUnidentified)} unidentified</span>
      </span>
    {/snippet}
    {#snippet actions()}
      <Button variant="secondary" size="sm" disabled={store.reviewableCount === 0} onclick={() => store.resumeNext()}>
        <Sparkles aria-hidden="true" />
        Review next
      </Button>
    {/snippet}
  </ManagePageHeader>

  {#if store.queue.length > 0}
    <section class="flex flex-col gap-2" aria-labelledby="identify-queue">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <h2 id="identify-queue" class="font-heading text-base font-semibold text-text-primary">
          Queue <span class="ml-1 font-mono text-caption font-normal text-text-muted">{store.queue.length}</span>
        </h2>
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

  <section class="flex flex-col gap-2" aria-labelledby="identify-families">
    <h2 id="identify-families" class="font-heading text-base font-semibold text-text-primary">
      Families <span class="ml-1 font-mono text-caption font-normal text-text-muted">{families.length}</span>
    </h2>
    {#if families.length === 0}
      <StatePlaceholder icon={ScanSearch} title="No identify providers" />
    {:else}
      <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {#each families as entry (entry.family.key)}
          <IdentifyFamilyCard
            family={entry.family}
            kinds={entry.kinds}
            providers={entry.providers}
            {selectedKind}
            onOpenKind={(kind) => void openKind(kind)}
          />
        {/each}
      </div>
    {/if}
  </section>

  {#if selectedKind}
    <section bind:this={kindPanel} class="scroll-mt-20 rounded-[var(--radius-md)] border border-[var(--color-border-subtle)] bg-[var(--color-surface-1)] p-4">
      {#key selectedKind}
        <IdentifyKindTab entityKind={selectedKind} />
      {/key}
    </section>
  {/if}
</div>
