<script lang="ts">
  let {
    open,
    title = "Confirm",
    message = "",
    confirmLabel = "Confirm",
    onConfirm,
  }: {
    open: boolean;
    title?: string;
    message?: string;
    confirmLabel?: string;
    onConfirm: () => void | Promise<void>;
  } = $props();
</script>

{#if open}
  <div role="dialog" aria-label={title}>
    <p>{message}</p>
    <button type="button" aria-label={`Confirm ${confirmLabel}`} onclick={() => void onConfirm()}>
      {confirmLabel}
    </button>
  </div>
{/if}
