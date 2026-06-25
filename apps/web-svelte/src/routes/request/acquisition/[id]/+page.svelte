<script lang="ts">
  import { onDestroy } from "svelte";
  import { ChevronLeft, Download, Loader2, X } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import { page } from "$app/state";
  import EntityDetail from "$lib/components/entities/EntityDetail.svelte";
  import type { EntityDetailActionButton } from "$lib/components/entities/entity-detail-types";
  import { ACQUISITION_STATUS } from "$lib/api/generated/codes";
  import type { AcquisitionDetail, ReleaseCandidateView } from "$lib/api/generated/model";
  import { cancelAcquisition, fetchAcquisition, queueAcquisitionCandidate } from "$lib/api/acquisitions";
  import { acquisitionToDetailCard } from "$lib/requests/acquisition-entity-card";
  import { ACTIVE_ACQUISITION_STATUSES, acquisitionStatusLabel } from "$lib/requests/review-cards";

  const id = $derived(page.params.id ?? "");

  let detail = $state<AcquisitionDetail | null>(null);
  let error = $state<string | null>(null);
  let busy = $state(false);
  let pollTimer: ReturnType<typeof setInterval> | null = null;

  const card = $derived(detail ? acquisitionToDetailCard(detail.summary) : null);
  const status = $derived(detail?.summary.status ?? null);
  const isActive = $derived(status ? ACTIVE_ACQUISITION_STATUSES.includes(status) : false);
  const canChoose = $derived(status === ACQUISITION_STATUS.awaitingSelection);

  async function load() {
    if (!id) return;
    try {
      detail = await fetchAcquisition(id);
      error = null;
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to load acquisition";
    }
  }

  async function queue(candidate: ReleaseCandidateView) {
    if (busy) return;
    busy = true;
    try {
      detail = await queueAcquisitionCandidate(id, candidate.id);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to queue release";
    } finally {
      busy = false;
    }
  }

  async function cancel() {
    if (busy) return;
    busy = true;
    try {
      detail = await cancelAcquisition(id);
    } catch (err) {
      error = err instanceof Error ? err.message : "Failed to cancel";
    } finally {
      busy = false;
    }
  }

  function formatSize(bytes: number): string {
    if (!bytes || bytes <= 1) return "—";
    const mb = bytes / 1_000_000;
    return mb >= 1000 ? `${(mb / 1000).toFixed(2)} GB` : `${mb.toFixed(1)} MB`;
  }

  const actionButtons = $derived<EntityDetailActionButton[]>(
    isActive || canChoose
      ? [{ id: "cancel", label: "Cancel", icon: X, variant: "danger", onClick: cancel, disabled: busy }]
      : [],
  );

  // Reload when the route id changes, and poll while mid-flight.
  $effect(() => {
    if (id) void load();
  });
  $effect(() => {
    if (isActive && !pollTimer) {
      pollTimer = setInterval(load, 4000);
    } else if (!isActive && pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  });
  onDestroy(() => {
    if (pollTimer) clearInterval(pollTimer);
  });
</script>

<svelte:head><title>{detail?.summary.title ?? "Acquisition"} · Prismedia</title></svelte:head>

<div class="space-y-4">
  <a
    href="/request"
    class="inline-flex items-center gap-1 text-[0.78rem] font-medium text-text-muted transition-colors hover:text-text-primary"
  >
    <ChevronLeft class="h-3.5 w-3.5" />
    Back to Request
  </a>

  {#if error}
    <div class="surface-panel border-l-2 border-error px-4 py-2.5 text-sm text-error-text">{error}</div>
  {/if}

  {#if !card || !detail}
    <div class="flex items-center justify-center gap-2.5 p-10 text-text-muted">
      <Loader2 class="h-4 w-4 animate-spin" />
      <span class="text-sm">Loading…</span>
    </div>
  {:else}
    <EntityDetail {card} posterSize="large" showFlagActions={false} {actionButtons}>
      {#snippet heroBadges()}
        {#if detail}
          <Badge variant={status === ACQUISITION_STATUS.imported ? "success" : status === ACQUISITION_STATUS.failed ? "error" : "accent"}>
            {acquisitionStatusLabel(detail.summary.status)}
          </Badge>
          {#if detail.summary.statusMessage}
            <span class="text-sm text-text-muted">{detail.summary.statusMessage}</span>
          {/if}
        {/if}
      {/snippet}

      {#snippet afterBody()}
        {#if detail}
        <section class="space-y-3">
          <h2 class="text-kicker text-text-primary">
            Releases
            <span class="ml-1.5 font-mono text-[0.68rem] font-normal text-text-muted">{detail.candidates.length}</span>
          </h2>

          {#if status === ACQUISITION_STATUS.searching}
            <p class="flex items-center gap-2 text-sm text-text-muted">
              <Loader2 class="h-4 w-4 animate-spin" /> Searching indexers…
            </p>
          {:else if detail.candidates.length === 0}
            <p class="text-sm text-text-muted">No release candidates found.</p>
          {:else}
            <div class="overflow-x-auto rounded-sm border border-border-subtle">
              <table class="w-full text-sm">
                <thead class="bg-surface-1 text-left text-[0.7rem] uppercase tracking-wide text-text-muted">
                  <tr>
                    <th class="px-3 py-2">Release</th>
                    <th class="px-3 py-2">Indexer</th>
                    <th class="px-3 py-2 text-right">Size</th>
                    <th class="px-3 py-2 text-right">Seeders</th>
                    <th class="px-3 py-2 text-right">Score</th>
                    <th class="px-3 py-2"></th>
                  </tr>
                </thead>
                <tbody>
                  {#each detail.candidates as c (c.id)}
                    <tr class="border-t border-border-subtle {c.accepted ? '' : 'opacity-55'}">
                      <td class="px-3 py-2">
                        <div class="max-w-[26rem] truncate text-text-primary" title={c.title}>{c.title}</div>
                        {#if !c.accepted && c.rejections.length > 0}
                          <div class="text-[0.7rem] text-warning-text">{c.rejections.map((r) => String(r).replace(/-/g, " ")).join(", ")}</div>
                        {/if}
                      </td>
                      <td class="px-3 py-2 text-text-muted">{c.indexerName}</td>
                      <td class="px-3 py-2 text-right text-text-muted">{formatSize(Number(c.sizeBytes))}</td>
                      <td class="px-3 py-2 text-right text-text-muted">{c.seeders ?? "—"}</td>
                      <td class="px-3 py-2 text-right font-mono text-[0.72rem] text-text-muted">{Number(c.score).toFixed(0)}</td>
                      <td class="px-3 py-2 text-right">
                        {#if c.accepted && canChoose}
                          <Button size="sm" onclick={() => queue(c)} disabled={busy} class="gap-1.5">
                            <Download class="h-3.5 w-3.5" />
                            Download
                          </Button>
                        {/if}
                      </td>
                    </tr>
                  {/each}
                </tbody>
              </table>
            </div>
          {/if}
        </section>
        {/if}
      {/snippet}
    </EntityDetail>
  {/if}
</div>
