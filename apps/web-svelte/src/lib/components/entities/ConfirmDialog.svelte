<script lang="ts">
  import { AlertTriangle, Loader2, X } from "@lucide/svelte";
  import { Button, Dialog } from "@prismedia/ui-svelte";

  interface Props {
    open: boolean;
    title: string;
    message: string;
    confirmLabel?: string;
    danger?: boolean;
    /** Confirm handler. May be async; the dialog shows a spinner and surfaces thrown errors. */
    onConfirm: () => void | Promise<void>;
    onClose: () => void;
  }

  let {
    open,
    title,
    message,
    confirmLabel = "Confirm",
    danger = false,
    onConfirm,
    onClose,
  }: Props = $props();

  let busy = $state(false);
  let error = $state<string | null>(null);

  $effect(() => {
    if (open) {
      error = null;
      busy = false;
    }
  });

  async function confirm() {
    if (busy) return;
    busy = true;
    error = null;
    try {
      await onConfirm();
      onClose();
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
      busy = false;
    }
  }
</script>

<Dialog {open} {onClose} ariaLabel={title} dismissible={!busy} class="w-[min(92vw,26rem)]">
  <div class="flex flex-col gap-4 p-5">
    <div class="flex items-start justify-between gap-4">
      <h2 class="flex items-center gap-2 font-heading text-base font-semibold tracking-wide text-text-primary">
        {#if danger}
          <AlertTriangle class="h-4 w-4 text-error-400" />
        {/if}
        {title}
      </h2>
      <Button
        variant="ghost"
        size="icon"
        onclick={onClose}
        disabled={busy}
        class="shrink-0"
        aria-label="Cancel"
      >
        <X class="h-4 w-4" />
      </Button>
    </div>

    <p class="text-body-sm text-text-muted">{message}</p>

    {#if error}
      <p class="text-body-sm text-error-400">{error}</p>
    {/if}

    <div class="flex justify-end gap-2">
      <Button type="button" variant="ghost" size="md" onclick={onClose} disabled={busy}>Cancel</Button>
      <Button
        type="button"
        variant={danger ? "danger" : "primary"}
        size="md"
        onclick={() => void confirm()}
        disabled={busy}
      >
        {#if busy}
          <Loader2 class="h-4 w-4 animate-spin" />
        {/if}
        {confirmLabel}
      </Button>
    </div>
  </div>
</Dialog>
