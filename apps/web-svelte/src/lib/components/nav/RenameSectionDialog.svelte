<script lang="ts">
  import { X } from "@lucide/svelte";
  import { Button, textInputVariants } from "@prismedia/ui-svelte";

  interface Props {
    open: boolean;
    title: string;
    /** Initial text value. */
    value: string;
    confirmLabel?: string;
    onConfirm: (value: string) => void;
    onClose: () => void;
  }

  let { open, title, value, confirmLabel = "Save", onConfirm, onClose }: Props = $props();

  let dialogRef = $state<HTMLDialogElement | null>(null);
  let draft = $state("");
  let inputRef = $state<HTMLInputElement | null>(null);

  $effect(() => {
    if (!dialogRef) return;
    if (open) {
      draft = value;
      dialogRef.showModal();
      // Focus and select after the dialog paints.
      queueMicrotask(() => inputRef?.select());
    } else if (dialogRef.open) {
      dialogRef.close();
    }
  });

  function confirm() {
    const trimmed = draft.trim();
    if (!trimmed) return;
    onConfirm(trimmed);
    onClose();
  }

  function handleBackdropClick(event: MouseEvent) {
    if (event.target === dialogRef) onClose();
  }

  function handleKeydown(event: KeyboardEvent) {
    if (event.key === "Enter") {
      event.preventDefault();
      confirm();
    }
  }
</script>

<dialog
  bind:this={dialogRef}
  onclick={handleBackdropClick}
  onclose={onClose}
  aria-label={title}
  class="app-dialog-surface fixed inset-0 m-auto h-fit w-[min(92vw,26rem)] p-0 text-text-primary open:block"
>
  <form
    method="dialog"
    class="flex flex-col gap-4 p-5"
    onsubmit={(e) => {
      e.preventDefault();
      confirm();
    }}
  >
    <div class="flex items-start justify-between gap-4">
      <h2 class="font-heading text-base font-semibold tracking-wide text-text-primary">{title}</h2>
      <button
        type="button"
        onclick={onClose}
        class="flex h-8 w-8 shrink-0 items-center justify-center rounded-sm text-text-muted transition-colors hover:bg-surface-2 hover:text-text-primary"
        aria-label="Cancel"
      >
        <X class="h-4 w-4" />
      </button>
    </div>

    <input
      bind:this={inputRef}
      bind:value={draft}
      class={textInputVariants({ size: "lg" })}
      placeholder="Section name"
      onkeydown={handleKeydown}
      maxlength={40}
    />

    <div class="flex justify-end gap-2">
      <Button type="button" variant="ghost" size="md" onclick={onClose}>Cancel</Button>
      <Button type="submit" variant="primary" size="md" disabled={!draft.trim()}>{confirmLabel}</Button>
    </div>
  </form>
</dialog>
