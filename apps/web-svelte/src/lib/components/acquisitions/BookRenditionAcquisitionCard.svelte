<script lang="ts">
  import { BookOpen, Headphones, Search } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import { BOOK_RENDITION, MONITOR_STATUS, type BookRenditionCode } from "$lib/api/generated/codes";
  import type { AcquisitionDetail, MonitorView } from "$lib/api/generated/model";
  import AcquisitionPanel from "$lib/components/acquisitions/AcquisitionPanel.svelte";
  import { acquisitionStatusDisplay } from "$lib/requests/acquisition-status-display";
  import {
    bookRenditionCanRequest,
    bookRenditionRows,
    type BookRenditionOwnership,
  } from "$lib/requests/book-rendition-acquisition";
  import { monitorIsActive, monitorTransitionIsLocked } from "$lib/requests/monitor-status";

  let {
    ownership,
    acquisitions,
    monitors,
    onRequest,
    onToggleMonitor,
    onChanged,
  }: {
    ownership: BookRenditionOwnership;
    acquisitions: readonly AcquisitionDetail[];
    monitors: readonly MonitorView[];
    onRequest: (rendition: BookRenditionCode) => void | Promise<void>;
    onToggleMonitor?: (monitor: MonitorView) => void | Promise<void>;
    onChanged?: () => void | Promise<void>;
  } = $props();

  const rows = $derived(bookRenditionRows(acquisitions, monitors, ownership));
  let requesting = $state<BookRenditionCode | null>(null);
  let monitorBusyId = $state<string | null>(null);
  let requestError = $state<{ rendition: BookRenditionCode; message: string } | null>(null);

  function renditionLabel(rendition: BookRenditionCode): string {
    return rendition === BOOK_RENDITION.audiobook ? "Audiobook" : "Ebook";
  }

  function monitorStatusLine(monitor: MonitorView): string {
    if (monitorIsActive(monitor)) return "Monitoring this rendition for releases.";
    if (monitor.status === MONITOR_STATUS.paused) return "Monitoring is paused for this rendition.";
    if (monitor.status === MONITOR_STATUS.fulfilled) return "This rendition's monitoring goal is fulfilled.";
    return "This rendition's monitoring state is updating.";
  }

  function monitorBadgeLabel(monitor: MonitorView): string {
    if (monitorIsActive(monitor)) return "Monitoring";
    if (monitor.status === MONITOR_STATUS.paused) return "Paused";
    if (monitor.status === MONITOR_STATUS.fulfilled) return "Fulfilled";
    return "Updating";
  }

  async function requestMissing(rendition: BookRenditionCode) {
    if (requesting) return;
    requesting = rendition;
    requestError = null;
    try {
      await onRequest(rendition);
    } catch (reason) {
      requestError = {
        rendition,
        message: reason instanceof Error ? reason.message : `Failed to request ${renditionLabel(rendition).toLowerCase()}`,
      };
    } finally {
      requesting = null;
    }
  }

  async function toggleMonitor(monitor: MonitorView) {
    if (!onToggleMonitor || monitorBusyId) return;
    monitorBusyId = monitor.id;
    requestError = null;
    try {
      await onToggleMonitor(monitor);
    } catch (reason) {
      requestError = {
        rendition: monitor.bookRendition === BOOK_RENDITION.audiobook
          ? BOOK_RENDITION.audiobook
          : BOOK_RENDITION.ebook,
        message: reason instanceof Error ? reason.message : "Failed to update rendition monitoring",
      };
    } finally {
      monitorBusyId = null;
    }
  }
</script>

<div class="rendition-list">
  {#each rows as row (row.rendition)}
    {@const label = renditionLabel(row.rendition)}
    {@const status = acquisitionStatusDisplay(row.acquisition?.summary.status)}
    {@const RenditionIcon = row.rendition === BOOK_RENDITION.audiobook ? Headphones : BookOpen}
    <section class="rendition-row" aria-label={`${label} acquisition`}>
      <header class="rendition-header">
        <div class="rendition-identity">
          <span class="rendition-icon"><RenditionIcon class="h-4 w-4" /></span>
          <div>
            <h3>{label}</h3>
            <p>
              {row.rendition === BOOK_RENDITION.audiobook
                ? "Narrated audio that shares this Book's metadata and artwork."
                : "Readable text, document, comic, or image-archive content."}
            </p>
          </div>
        </div>
        <div class="rendition-actions">
          {#if row.owned}
            <Badge variant="success">In library</Badge>
          {:else if row.acquisition}
            <Badge variant={status.tone === "failed" ? "error" : status.tone === "attention" ? "warning" : "accent"}>
              {status.label}
            </Badge>
          {:else if row.monitor}
            <Badge variant={monitorIsActive(row.monitor) ? "accent" : "default"}>
              {monitorBadgeLabel(row.monitor)}
            </Badge>
          {:else}
            <Badge>Missing</Badge>
          {/if}
          {#if bookRenditionCanRequest(row)}
            <Button
              type="button"
              size="sm"
              variant="primary"
              class="no-lift gap-1.5"
              disabled={requesting !== null}
              onclick={() => void requestMissing(row.rendition)}
            >
              <Search class="h-3.5 w-3.5" />
              {requesting === row.rendition ? "Requesting…" : `Request ${label.toLowerCase()}`}
            </Button>
          {/if}
          {#if row.monitor && onToggleMonitor}
            <Button
              type="button"
              size="sm"
              variant="secondary"
              class="no-lift"
              disabled={monitorBusyId !== null || monitorTransitionIsLocked(row.monitor)}
              onclick={() => void toggleMonitor(row.monitor!)}
            >
              {monitorBusyId === row.monitor.id
                ? "Updating…"
                : monitorTransitionIsLocked(row.monitor)
                  ? "Updating…"
                  : monitorIsActive(row.monitor)
                    ? `Stop monitoring ${label.toLowerCase()}`
                    : `Resume monitoring ${label.toLowerCase()}`}
            </Button>
          {/if}
        </div>
      </header>

      {#if row.monitor}
        <p class="monitor-line">{monitorStatusLine(row.monitor)}</p>
      {/if}

      {#if requestError?.rendition === row.rendition}
        <p role="alert" class="request-error">{requestError.message}</p>
      {/if}

      {#if row.acquisition}
        {#key row.acquisition.summary.id}
          <div class="acquisition-detail">
            <AcquisitionPanel
              acquisitionId={row.acquisition.summary.id}
              detail={row.acquisition}
              onCancelled={onChanged}
              onImported={onChanged}
            />
          </div>
        {/key}
      {/if}
    </section>
  {/each}
</div>

<style>
  .rendition-list {
    display: grid;
    gap: 0.75rem;
  }

  .rendition-row {
    display: grid;
    gap: 0.75rem;
    min-width: 0;
    padding: 0.85rem;
    border: 1px solid var(--color-border-subtle);
    border-radius: var(--radius-sm);
    background: linear-gradient(135deg, var(--color-surface-2), var(--color-surface-1));
  }

  .rendition-header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 1rem;
  }

  .rendition-identity {
    display: flex;
    min-width: 0;
    align-items: flex-start;
    gap: 0.65rem;
  }

  .rendition-icon {
    display: grid;
    width: 2rem;
    height: 2rem;
    flex: 0 0 auto;
    place-items: center;
    border: 1px solid var(--color-border-accent);
    border-radius: var(--radius-xs);
    color: var(--color-text-accent);
    background: var(--color-accent-overlay-faint);
    box-shadow: 0 0 12px var(--color-accent-overlay-faint);
  }

  h3 {
    font-family: var(--font-heading, Geist, sans-serif);
    font-size: 0.9rem;
    font-weight: 650;
    color: var(--color-text-primary);
  }

  .rendition-identity p,
  .monitor-line {
    margin-top: 0.15rem;
    font-size: 0.72rem;
    line-height: 1.45;
    color: var(--color-text-muted);
  }

  .request-error {
    font-size: 0.72rem;
    color: var(--color-error-text);
  }

  .rendition-actions {
    display: flex;
    flex: 0 0 auto;
    flex-wrap: wrap;
    align-items: center;
    justify-content: flex-end;
    gap: 0.45rem;
  }

  .acquisition-detail {
    min-width: 0;
    padding-top: 0.75rem;
    border-top: 1px solid var(--color-border-subtle);
  }

  @media (max-width: 40rem) {
    .rendition-header {
      flex-direction: column;
    }

    .rendition-actions {
      width: 100%;
      justify-content: flex-start;
      padding-left: 2.65rem;
    }
  }
</style>
