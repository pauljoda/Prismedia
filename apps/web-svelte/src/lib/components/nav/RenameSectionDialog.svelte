<script lang="ts">
  import { X } from "@lucide/svelte";
  import { Button, Dialog, textInputVariants } from "@prismedia/ui-svelte";

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

  let draft = $state("");
  let inputRef = $state<HTMLInputElement | null>(null);

  $effect(() => {
    if (open) {
      draft = value;
      // Focus and select after the dialog paints.
      queueMicrotask(() => inputRef?.select());
    }
  });

  function confirm() {
    const trimmed = draft.trim();
    if (!trimmed) return;
    onConfirm(trimmed);
    onClose();
  }
  function handleKeydown(event: KeyboardEvent) {
    if (event.key === "Enter") {
      event.preventDefault();
      confirm();
    }
  }
</script>

<Dialog {open} {onClose} ariaLabel={title} class="w-[min(92vw,26rem)]">
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
      <Button
        variant="ghost"
        size="icon"
        onclick={onClose}
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
      placeholder="Section name"
      onkeydown={handleKeydown}
      maxlength={40}
    />

    <div class="flex justify-end gap-2">
      <Button type="button" variant="ghost" size="md" onclick={onClose}>Cancel</Button>
      <Button type="submit" variant="primary" size="md" disabled={!draft.trim()}>{confirmLabel}</Button>
    </div>
  </form>
</Dialog>
