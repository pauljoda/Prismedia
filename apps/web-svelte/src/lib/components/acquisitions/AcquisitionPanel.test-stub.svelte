<script lang="ts">
  let {
    acquisitionId,
    onImported,
  }: {
    acquisitionId: string;
    onImported?: () => void;
  } = $props();
</script>

<span data-testid="acquisition-panel">{acquisitionId}</span>
<button type="button" onclick={() => onImported?.()}>Notify imported</button>
