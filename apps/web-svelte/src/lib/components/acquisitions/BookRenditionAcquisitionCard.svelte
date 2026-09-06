<script lang="ts">
  import { BookOpen, Headphones, Search } from "@lucide/svelte";
  import { Alert, Badge, Button, Item } from "@prismedia/ui-svelte";
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

<Item.Group class="gap-4">
  {#each rows as row (row.rendition)}
    {@const label = renditionLabel(row.rendition)}
    {@const status = acquisitionStatusDisplay(row.acquisition?.summary.status)}
    {@const RenditionIcon = row.rendition === BOOK_RENDITION.audiobook ? Headphones : BookOpen}
    <section class="flex min-w-0 flex-col gap-4" aria-label={`${label} acquisition`}>
      <Item.Root variant="outline" class="@container p-4">
        <Item.Media variant="icon"><RenditionIcon /></Item.Media>
        <Item.Content class="min-w-0">
          <Item.Title role="heading" aria-level={3}>{label}</Item.Title>
          {#if row.owned}
            <Item.Description>In library</Item.Description>
          {:else if row.acquisition}
            <div><Badge variant={status.tone === "failed" ? "error" : status.tone === "attention" ? "warning" : "default"}>{status.label}</Badge></div>
          {:else if row.monitor}
            <Item.Description>{monitorStatusLine(row.monitor)}</Item.Description>
          {:else}
            <Item.Description>Not in library</Item.Description>
          {/if}
          {#if row.monitor && (row.owned || row.acquisition)}
            <Item.Description>{monitorStatusLine(row.monitor)}</Item.Description>
          {/if}
        </Item.Content>
        <Item.Actions class="flex-wrap @max-[32rem]:w-full @max-[32rem]:[&>button]:flex-1">
          {#if bookRenditionCanRequest(row)}
            <Button
              type="button"
              variant="secondary"
              disabled={requesting !== null}
              onclick={() => void requestMissing(row.rendition)}
            >
              <Search data-icon="inline-start" />
              {requesting === row.rendition ? "Requesting…" : `Request ${label.toLowerCase()}`}
            </Button>
          {/if}
          {#if row.monitor && onToggleMonitor}
            <Button
              type="button"
              variant="secondary"
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
        </Item.Actions>
      </Item.Root>

      {#if requestError?.rendition === row.rendition}
        <Alert.Root variant="destructive"><Alert.Description>{requestError.message}</Alert.Description></Alert.Root>
      {/if}

      {#if row.acquisition}
        {#key row.acquisition.summary.id}
          <div class="min-w-0">
            <AcquisitionPanel
              acquisitionId={row.acquisition.summary.id}
              detail={row.acquisition}
              onCancelled={onChanged}
              onImported={onChanged}
              onReset={onChanged}
            />
          </div>
        {/key}
      {/if}
    </section>
  {/each}
</Item.Group>
