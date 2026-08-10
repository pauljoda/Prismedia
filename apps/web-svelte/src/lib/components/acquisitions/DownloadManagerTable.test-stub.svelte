<script lang="ts">
  import type { DownloadManagerEntry } from "./download-tree";

  let {
    entries,
    error = null,
    onRemove,
    onSelect,
  }: {
    entries: DownloadManagerEntry[];
    error?: string | null;
    onRemove: (ids: string[]) => void;
    onSelect: (key: string) => void;
  } = $props();
</script>

{#if error}<p role="alert">{error}</p>{/if}
<output data-testid="entry-count">{entries.length}</output>
<button
  type="button"
  disabled={entries.length === 0}
  onclick={() => onRemove(entries.map((entry) => entry.item.id))}
  aria-label="Select all downloads"
>
  Select all downloads
</button>
<button type="button" onclick={() => onSelect("entity:series")} aria-label="Inspect series downloads">
  Inspect series downloads
</button>
