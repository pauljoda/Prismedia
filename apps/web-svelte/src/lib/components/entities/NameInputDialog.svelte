<script lang="ts">
  import { Loader2, X } from "@lucide/svelte";
  import { Button, Dialog, textInputVariants } from "@prismedia/ui-svelte";

  interface Props {
    open: boolean;
    title: string;
    initialValue?: string;
    placeholder?: string;
    confirmLabel?: string;
    maxlength?: number;
    /** Submit handler. May be async; the dialog shows a spinner and surfaces thrown errors. */
    onConfirm: (value: string) => void | Promise<void>;
    onClose: () => void;
  }

  let {
    open,
    title,
    initialValue = "",
    placeholder = "Name",
    confirmLabel = "Create",
    maxlength = 120,
    onConfirm,
    onClose,
  }: Props = $props();

  let inputRef = $state<HTMLInputElement | null>(null);
  let draft = $state("");
  let busy = $state(false);
  let error = $state<string | null>(null);

  $effect(() => {
    if (open) {
      draft = initialValue;
      error = null;
      busy = false;
      queueMicrotask(() => inputRef?.focus());
    }
  });

  async function confirm() {
    const trimmed = draft.trim();
    if (!trimmed || busy) return;
    busy = true;
    error = null;
    try {
      await onConfirm(trimmed);
      onClose();
    } catch (err) {
      error = err instanceof Error ? err.message : String(err);
      busy = false;
    }
  }
</script>

<Dialog {open} {onClose} ariaLabel={title} dismissible={!busy} class="w-[min(92vw,26rem)]">
  <form
    method="dialog"
    class="flex flex-col gap-4 p-5"
    onsubmit={(e) => {
      e.preventDefault();
      void confirm();
    }}
  >
    <div class="flex items-start justify-between gap-4">
      <h2 class="font-heading text-base font-semibold tracking-wide text-text-primary">{title}</h2>
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

    <input
      bind:this={inputRef}
      bind:value={draft}
      class={textInputVariants({ size: "lg" })}
      {placeholder}
      {maxlength}
      disabled={busy}
    />

    {#if error}
      <p class="text-body-sm text-error-400">{error}</p>
    {/if}

    <div class="flex justify-end gap-2">
      <Button type="button" variant="ghost" size="md" onclick={onClose} disabled={busy}>Cancel</Button>
      <Button type="submit" variant="primary" size="md" disabled={!draft.trim() || busy}>
        {#if busy}
          <Loader2 class="h-4 w-4 animate-spin" />
        {/if}
        {confirmLabel}
      </Button>
    </div>
  </form>
</Dialog>
