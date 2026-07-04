<script lang="ts">
  import { Badge } from "@prismedia/ui-svelte";
  import type { AcquisitionHistoryView } from "$lib/api/generated/model";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import { formatRelativeTime } from "$lib/utils/format";
  import {
    acquisitionHistoryEventLabel,
    acquisitionHistoryEventVariant,
  } from "$lib/requests/acquisition-history";

  /**
   * Renders the durable acquisition activity log as compact rows: an event badge, the item title, the
   * release/indexer/client context, and a relative timestamp. The full message shows on hover. Shared by
   * the per-entity History section in the acquisition panel and the global History tab in the request hub;
   * `showKind` turns on the media-kind badge for the mixed-kind global view.
   */
  let {
    entries,
    showKind = false,
  }: {
    entries: AcquisitionHistoryView[];
    /** Show a media-kind badge per row (for the mixed-kind global log). */
    showKind?: boolean;
  } = $props();
</script>

<div class="overflow-hidden rounded-sm border border-border-subtle">
  {#each entries as entry (entry.id)}
    <div
      class="flex items-start justify-between gap-3 border-b border-border-subtle px-3 py-2 last:border-b-0"
      title={entry.message ?? undefined}
    >
      <div class="flex min-w-0 flex-col gap-1">
        <div class="flex flex-wrap items-center gap-1.5">
          <Badge variant={acquisitionHistoryEventVariant(entry.event)}>
            {acquisitionHistoryEventLabel(entry.event)}
          </Badge>
          {#if showKind}
            <Badge variant="default">{labelForEntityKind(entry.kind)}</Badge>
          {/if}
          <span class="truncate text-sm font-medium text-text-primary">{entry.title}</span>
        </div>
        <div class="flex flex-wrap items-center gap-x-2.5 gap-y-0.5 text-[0.72rem] text-text-muted">
          {#if entry.releaseTitle}
            <span class="truncate font-mono">{entry.releaseTitle}</span>
          {/if}
          {#if entry.qualityCode}
            <span class="font-mono text-text-secondary">{entry.qualityCode}</span>
          {/if}
          {#if entry.indexerName}
            <span>via {entry.indexerName}</span>
          {/if}
          {#if entry.downloadClientName}
            <span>→ {entry.downloadClientName}</span>
          {/if}
        </div>
        {#if entry.message}
          <p class="truncate text-[0.72rem] text-text-muted">{entry.message}</p>
        {/if}
      </div>
      <span class="shrink-0 whitespace-nowrap font-mono text-[0.68rem] text-text-muted">
        {formatRelativeTime(entry.createdAt)}
      </span>
    </div>
  {/each}
</div>
