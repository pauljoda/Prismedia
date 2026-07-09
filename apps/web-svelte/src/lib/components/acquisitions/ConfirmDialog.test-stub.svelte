<script lang="ts">
  let {
    open,
    confirmLabel = "Confirm",
    onConfirm,
  }: {
    open: boolean;
    confirmLabel?: string;
    onConfirm: () => void | Promise<void>;
  } = $props();
</script>

{#if open}
  <button type="button" aria-label={`Confirm ${confirmLabel}`} onclick={() => void onConfirm()}>
    {confirmLabel}
  </button>
{/if}
