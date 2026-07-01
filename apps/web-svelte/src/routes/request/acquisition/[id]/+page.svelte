<script lang="ts">
  import { ChevronLeft, Loader2 } from "@lucide/svelte";
  import { page } from "$app/state";
  import EntityDetail from "$lib/components/entities/EntityDetail.svelte";
  import AcquisitionPanel from "$lib/components/acquisitions/AcquisitionPanel.svelte";
  import { fetchAcquisition } from "$lib/api/acquisitions";
  import type { AcquisitionDetail as AcquisitionDetailModel } from "$lib/api/generated/model";
  import { acquisitionToDetailCard } from "$lib/requests/acquisition-entity-card";
  import { resolveEntityHref } from "$lib/entities/entity-routes";
  import { ENTITY_KIND } from "$lib/entities/entity-codes";

  const id = $derived(page.params.id ?? "");

  // One initial fetch supplies the hero; from then on the panel owns loading/polling/actions and its
  // bound `detail` keeps the hero in sync. Managing state increasingly happens on the entity's own
  // detail page — this route remains for reaching an acquisition from the request queue/history.
  let detail = $state<AcquisitionDetailModel | null>(null);
  let error = $state<string | null>(null);

  let loadedId = $state("");
  $effect(() => {
    if (!id || id === loadedId) return;
    loadedId = id;
    detail = null;
    error = null;
    fetchAcquisition(id).then(
      (loaded) => (detail = loaded),
      (err) => (error = err instanceof Error ? err.message : "Failed to load acquisition"),
    );
  });

  const card = $derived(detail ? acquisitionToDetailCard(detail.summary) : null);
  // When the acquisition backs a wanted/imported library entity, link to its real home in the library.
  const entityHref = $derived(
    detail?.summary.entityId ? resolveEntityHref(ENTITY_KIND.book, detail.summary.entityId) : null,
  );
</script>

<svelte:head><title>{detail?.summary.title ?? "Acquisition"} · Prismedia</title></svelte:head>

<div class="space-y-4">
  <div class="flex flex-wrap items-center justify-between gap-2">
    <a
      href="/request"
      class="inline-flex items-center gap-1 text-[0.78rem] font-medium text-text-muted transition-colors hover:text-text-primary"
    >
      <ChevronLeft class="h-3.5 w-3.5" />
      Back to Request
    </a>
    {#if entityHref}
      <a
        href={entityHref}
        class="text-[0.78rem] font-medium text-text-accent transition-colors hover:text-text-primary"
      >
        View in library →
      </a>
    {/if}
  </div>

  {#if error && !detail}
    <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">{error}</div>
  {:else if !card}
    <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
      <Loader2 class="h-4 w-4 animate-spin" />
      <span class="text-sm">Loading…</span>
    </div>
  {:else}
    <EntityDetail {card} posterSize="large" showFlagActions={false}>
      {#snippet afterBody()}
        <AcquisitionPanel acquisitionId={id} bind:detail />
      {/snippet}
    </EntityDetail>
  {/if}
</div>
