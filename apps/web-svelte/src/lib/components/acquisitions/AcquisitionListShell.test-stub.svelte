<script lang="ts">
  let {
    items,
    error = null,
    bulkActions = [],
  }: {
    items: Array<{ id: string }>;
    error?: string | null;
    bulkActions?: Array<{ run: (ids: string[]) => void | Promise<void> }>;
  } = $props();
</script>

{#if error}<p role="alert">{error}</p>{/if}
<button
  type="button"
  disabled={items.length === 0}
  onclick={() => void bulkActions[0]?.run(items.map((item) => item.id))}
>
  Select all downloads
</button>
