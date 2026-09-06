<script lang="ts">
  import { AlertTriangle, Loader2 } from "@lucide/svelte";
  import { Alert, AlertDialog, buttonVariants } from "@prismedia/ui-svelte";

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
  let cancelButton = $state<HTMLButtonElement | null>(null);

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

<AlertDialog.Root {open} onOpenChange={(next) => { if (!next && !busy) onClose(); }}>
  {#if open}
    <AlertDialog.Content escapeKeydownBehavior={busy ? "ignore" : "close"} aria-busy={busy}
      onOpenAutoFocus={(event) => {
        if (cancelButton) { event.preventDefault(); cancelButton.focus(); }
      }}>
      <AlertDialog.Header>
        {#if danger}
          <AlertDialog.Media><AlertTriangle aria-hidden="true" /></AlertDialog.Media>
        {/if}
        <AlertDialog.Title>{title}</AlertDialog.Title>
        <AlertDialog.Description>{message}</AlertDialog.Description>
      </AlertDialog.Header>
      {#if error}
        <Alert.Root variant="destructive"><Alert.Description>{error}</Alert.Description></Alert.Root>
      {/if}
      <AlertDialog.Footer>
        <AlertDialog.Cancel bind:ref={cancelButton} disabled={busy} class={buttonVariants({ variant: "secondary", size: "md" })}>Cancel</AlertDialog.Cancel>
        <AlertDialog.Action
          class={buttonVariants({ variant: danger ? "danger" : "primary", size: "md" })}
          onclick={(event) => {
            // The async action, not the primitive's click handler, owns dismissal.
            event.preventDefault();
            void confirm();
          }}
          disabled={busy}
        >
          {#if busy}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
          {confirmLabel}
        </AlertDialog.Action>
      </AlertDialog.Footer>
    </AlertDialog.Content>
  {/if}
</AlertDialog.Root>
