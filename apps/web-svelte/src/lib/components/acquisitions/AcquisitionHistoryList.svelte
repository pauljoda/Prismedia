<script lang="ts">
  import { RotateCcw } from "@lucide/svelte";
  import { Badge, Button } from "@prismedia/ui-svelte";
  import type { AcquisitionBlocklistEntry, AcquisitionHistoryView } from "$lib/api/generated/model";
  import { labelForEntityKind } from "$lib/entities/entity-codes";
  import { formatRelativeTime } from "$lib/utils/format";
  import {
    acquisitionHistoryEventLabel,
    acquisitionHistoryEventVariant,
  } from "$lib/requests/acquisition-history";
  import { findBlocklistEntryForHistory } from "$lib/requests/acquisition-blocklist";

  /**
   * Renders the durable acquisition activity log as compact rows: an event badge, the item title, the
   * release/indexer/client context, and a relative timestamp. The full message shows on hover. Shared by
   * the per-entity History section in the acquisition panel and the global History tab in the request hub;
   * `showKind` turns on the media-kind badge for the mixed-kind global view.
   */
  let {
    entries,
    showKind = false,
    blocklistEntries = [],
    removingBlocklistIds = [],
    onRemoveBlocklist,
  }: {
    entries: AcquisitionHistoryView[];
    /** Show a media-kind badge per row (for the mixed-kind global log). */
    showKind?: boolean;
    /** Current live blocklist rows; enables the inline Allow again action for Blocklisted events. */
    blocklistEntries?: AcquisitionBlocklistEntry[];
    removingBlocklistIds?: string[];
    onRemoveBlocklist?: (id: string) => void | Promise<void>;
  } = $props();
</script>

<div class="history-list">
  {#each entries as entry (entry.id)}
    {@const blocklistEntry = findBlocklistEntryForHistory(entry, blocklistEntries)}
    <div class="history-row" title={entry.message ?? undefined}>
      <div class="history-content">
        <div class="history-heading">
          <Badge variant={acquisitionHistoryEventVariant(entry.event)}>
            {acquisitionHistoryEventLabel(entry.event)}
          </Badge>
          {#if showKind}
            <Badge variant="default">{labelForEntityKind(entry.kind)}</Badge>
          {/if}
          <span class="history-title">{entry.title}</span>
        </div>
        <div class="history-meta">
          {#if entry.releaseTitle}
            <span class="history-release">{entry.releaseTitle}</span>
          {/if}
          {#if entry.qualityCode}
            <span class="history-quality">{entry.qualityCode}</span>
          {/if}
          {#if entry.indexerName}
            <span>via {entry.indexerName}</span>
          {/if}
          {#if entry.downloadClientName}
            <span>→ {entry.downloadClientName}</span>
          {/if}
        </div>
        {#if entry.message}
          <p class="history-message">{entry.message}</p>
        {/if}
      </div>
      <div class="history-actions">
        <span class="history-time">
          {formatRelativeTime(entry.createdAt)}
        </span>
        {#if blocklistEntry && onRemoveBlocklist}
          <Button
            type="button"
            size="sm"
            variant="ghost"
            class="no-lift gap-1.5"
            disabled={removingBlocklistIds.includes(blocklistEntry.id)}
            onclick={() => void onRemoveBlocklist?.(blocklistEntry.id)}
            aria-label={`Allow ${blocklistEntry.title ?? "release"} again`}
            title="Remove this release from the blocklist"
          >
            <RotateCcw class="h-3.5 w-3.5" />
            Allow again
          </Button>
        {/if}
      </div>
    </div>
  {/each}
</div>

<style>
  .history-list {
    min-width: 0;
    max-width: 100%;
    overflow: hidden;
    border: 1px solid var(--color-border-subtle, rgb(255 255 255 / 0.08));
    border-radius: var(--radius-sm, 6px);
  }

  .history-row {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 0.75rem;
    min-width: 0;
    max-width: 100%;
    padding: 0.5rem 0.75rem;
    border-bottom: 1px solid var(--color-border-subtle, rgb(255 255 255 / 0.08));
  }

  .history-row:last-child {
    border-bottom: 0;
  }

  .history-content {
    display: flex;
    min-width: 0;
    max-width: 100%;
    flex: 1 1 auto;
    flex-direction: column;
    gap: 0.25rem;
  }

  .history-heading,
  .history-meta {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    min-width: 0;
    max-width: 100%;
  }

  .history-heading {
    gap: 0.375rem;
  }

  .history-meta {
    gap: 0.125rem 0.625rem;
    font-size: 0.72rem;
    color: var(--color-text-muted, rgb(196 201 212 / 0.72));
  }

  .history-title,
  .history-release,
  .history-message {
    min-width: 0;
    max-width: 100%;
    white-space: normal;
    overflow-wrap: anywhere;
    word-break: normal;
  }

  .history-title {
    flex: 1 1 8rem;
    font-size: 0.875rem;
    font-weight: 500;
    color: var(--color-text-primary, rgb(244 239 230 / 0.94));
  }

  .history-release {
    flex: 1 1 12rem;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
  }

  .history-quality {
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    color: var(--color-text-secondary, rgb(214 219 228 / 0.78));
  }

  .history-message {
    margin: 0;
    font-size: 0.72rem;
    color: var(--color-text-muted, rgb(196 201 212 / 0.72));
  }

  .history-time {
    flex: 0 0 auto;
    white-space: nowrap;
    font-family: var(--font-mono, "JetBrains Mono", monospace);
    font-size: 0.68rem;
    color: var(--color-text-muted, rgb(196 201 212 / 0.72));
  }

  .history-actions {
    display: flex;
    flex: 0 0 auto;
    align-items: flex-end;
    gap: 0.35rem;
    flex-direction: column;
  }

  @media (max-width: 640px) {
    .history-row {
      flex-direction: column;
      gap: 0.35rem;
    }

    .history-meta {
      align-items: flex-start;
    }

    .history-time {
      align-self: flex-start;
    }

    .history-actions {
      width: 100%;
      align-items: flex-start;
      flex-direction: row;
      justify-content: space-between;
    }
  }
</style>
